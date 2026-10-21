using System.Text.Json;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Application;

public sealed class PlanningService(AcademicDbContext db, EligibilityEngine eligibility,
    PlanValidator validator, IPlannerClient planner, TimeProvider clock)
{
    public async Task<PlanningContext?> LoadAsync(string studentId, string termCode, CancellationToken ct = default)
    {
        var student = await db.Students.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == studentId, ct);
        var term = await db.Terms.AsNoTracking().SingleOrDefaultAsync(t => t.Code == termCode, ct);
        if (student is null || term is null) return null;
        var edition = await db.Catalogs.AsNoTracking().SingleOrDefaultAsync(c => c.Id == student.CatalogVersion, ct);
        var courses = await db.Courses.AsNoTracking().Where(c => c.CatalogVersion == student.CatalogVersion).ToArrayAsync(ct);
        var attempts = await db.Attempts.AsNoTracking().Where(a => a.StudentId == studentId).ToArrayAsync(ct);
        var sections = await db.Sections.AsNoTracking().Where(s => s.CatalogVersion == student.CatalogVersion && s.TermCode == termCode).ToArrayAsync(ct);
        try
        {
            return new(student.CatalogVersion,
                edition is null ? null : JsonSerializer.Deserialize<ProgramDefinition>(edition.RulesJson, FixtureReader.Json),
                courses.Select(FixtureReader.ToDomain).ToArray(), attempts.Select(a => new Attempt(a.CourseCode, a.Term, a.Grade)).ToArray(),
                PlanningFixtures.ToDomain(term), sections.Select(PlanningFixtures.ToDomain).ToArray());
        }
        catch (JsonException)
        {
            return new(student.CatalogVersion, null, [], [], PlanningFixtures.ToDomain(term), []);
        }
    }

    public async Task<PlanResult> GenerateAsync(string studentId, PlanningContext context, PlanRequest request, CancellationToken ct)
    {
        PlanResult Failure(string status, params RuleIssue[] issues) => new(status, context.CatalogVersion,
            request.TermCode, !context.Term.IsPublished, 0, [], issues);
        var errors = validator.CheckRequest(context, request);
        if (errors.Length > 0) return Failure("INVALID", errors);
        var candidates = eligibility.Options(context);
        var uniqueCredits = candidates.GroupBy(o => o.CourseCode).Sum(g => g.First().Credits);
        if (uniqueCredits < request.MinCredits) return Failure("INFEASIBLE",
            [new("INSUFFICIENT_ELIGIBLE_CREDITS", $"Only {uniqueCredits} distinct eligible credits have available offerings; requested at least {request.MinCredits}."),
            .. context.Courses.Select(c => eligibility.Evaluate(context, c.Code)).Where(e => !e.Eligible && e.Status != "completed").SelectMany(e => e.Reasons).Distinct()]);
        var result = await planner.SolveAsync(new(candidates.Select(o => new SolverOption(o.Id, o.CourseCode,
            o.Credits, o.Category == "core", o.Days, o.StartLocal is null ? null : SchedulingRules.Minute(o.StartLocal),
            o.EndLocal is null ? null : SchedulingRules.Minute(o.EndLocal))).ToArray(), request.MinCredits,
            request.MaxCredits, request.PreferredDays ?? [], request.FewerCampusDays), ct);
        if (result.Status == "FEASIBLE" && result.SelectionIds is not null)
        {
            var current = await LoadAsync(studentId, request.TermCode, ct);
            if (current is null || current.CatalogVersion != context.CatalogVersion)
                return Failure("ERROR", new RuleIssue("PLANNING_DATA_CHANGED", "Planning data changed. Refresh and retry."));
            var validated = validator.Validate(current, request, result.SelectionIds);
            return validated.Status == "FEASIBLE" ? validated : Failure("ERROR",
                [new("SOLVER_RESULT_INVALID", "The backend rejected an invalid solver proposal."), .. validated.Issues]);
        }
        if (result.SelectionIds?.Length > 0) return Failure("ERROR", new RuleIssue("SOLVER_RESULT_INVALID", "Non-feasible solver results cannot contain selections."));
        return result.Status switch
        {
            "INFEASIBLE" => Failure("INFEASIBLE", new RuleIssue("NO_VALID_COMBINATION", "No combination satisfies the credit bounds, distinct courses, seat availability, and published meeting constraints.")),
            "TIMEOUT" => Failure("TIMEOUT", new RuleIssue("SOLVER_TIMEOUT", "The planning time limit elapsed without a feasible proposal. This does not prove infeasibility.")),
            "UNAVAILABLE" => Failure("UNAVAILABLE", new RuleIssue("PLANNER_UNAVAILABLE", "The solver service is unavailable. Select courses manually and validate them below.")),
            _ => Failure("ERROR", new RuleIssue("SOLVER_ERROR", "The solver could not produce a trusted result. Manual validation remains available."))
        };
    }

    public async Task<PlanResult> SaveAsync(string studentId, PlanningContext context, SelectionRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var current = await LoadAsync(studentId, request.Constraints.TermCode, ct);
        if (current is null || current.CatalogVersion != context.CatalogVersion)
            return new("INVALID", context.CatalogVersion, request.Constraints.TermCode, !context.Term.IsPublished,
                0, [], [new("PLANNING_DATA_CHANGED", "Planning data changed. Refresh and retry.")]);
        context = current;
        var result = validator.Validate(context, request.Constraints, request.SelectionIds);
        if (result.Status != "FEASIBLE") return result;
        var plan = new SchedulePlan
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            CatalogVersion = context.CatalogVersion,
            TermCode = context.Term.Code,
            ConstraintsJson = JsonSerializer.Serialize(request.Constraints),
            SelectionIdsJson = JsonSerializer.Serialize(request.SelectionIds),
            CreatedAt = clock.GetUtcNow()
        };
        db.Plans.Add(plan);
        AuditTrail.Add(db, clock, studentId, "plan.save", plan.Id.ToString());
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result with { Id = plan.Id };
    }

    public async Task<PlanResult> ReadAsync(SchedulePlan plan, CancellationToken ct)
    {
        var context = await LoadAsync(plan.StudentId, plan.TermCode, ct);
        try
        {
            var constraints = JsonSerializer.Deserialize<PlanRequest>(plan.ConstraintsJson);
            var ids = JsonSerializer.Deserialize<string[]>(plan.SelectionIdsJson);
            if (context is not null && context.CatalogVersion == plan.CatalogVersion && constraints is not null && ids is not null)
                return validator.Validate(context, constraints, ids, requireFuture: false) with { Id = plan.Id };
        }
        catch (JsonException) { }
        return new("INVALID", plan.CatalogVersion, plan.TermCode, context?.Term.IsPublished != true, 0, [],
            [new("PLAN_INVALID", "Saved plan data is no longer valid for its pinned catalog.")], plan.Id);
    }
}

