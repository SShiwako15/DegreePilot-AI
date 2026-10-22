using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Application;

public sealed class DegreePlanningService(AcademicDbContext db, AuditEngine audit, EligibilityEngine eligibility,
    DegreePlanValidator validator, IDegreePlannerClient solver, TimeProvider clock)
{
    public bool Valid(DegreePlanRequest r)
    {
        var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).Date;
        return SchedulingRules.ValidTermCode(r.StartTermCode) && r.HorizonTerms is >= 1 and <= 12 &&
            int.Parse(r.StartTermCode[..4], CultureInfo.InvariantCulture) >= today.Year &&
            int.Parse(r.StartTermCode[..4], CultureInfo.InvariantCulture) <= today.Year + 6 &&
            SchedulingRules.ValidateRequest(new(r.StartTermCode, r.MinCredits, r.MaxCredits)).Length == 0;
    }

    public async Task<DegreeContext?> LoadAsync(string actor, DegreePlanRequest request, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == actor, ct);
        if (student is null) return null;
        var edition = await db.Catalogs.AsNoTracking().SingleOrDefaultAsync(c => c.Id == student.CatalogVersion && c.Status == "published", ct);
        var courses = await db.Courses.AsNoTracking().Where(c => c.CatalogVersion == student.CatalogVersion).OrderBy(c => c.Code).ToArrayAsync(ct);
        var attempts = (await db.Attempts.AsNoTracking().Where(a => a.StudentId == actor).OrderBy(a => a.CourseCode).ThenBy(a => a.Term).ToArrayAsync(ct))
            .Select(a => new Attempt(a.CourseCode, a.Term, a.Grade)).ToArray();
        var terms = await db.Terms.AsNoTracking().ToArrayAsync(ct);
        var allSections = await db.Sections.AsNoTracking().Where(s => s.CatalogVersion == student.CatalogVersion).OrderBy(s => s.Id).ToArrayAsync(ct);
        try
        {
            var program = edition is null ? null : JsonSerializer.Deserialize<ProgramDefinition>(edition.RulesJson, FixtureReader.Json);
            var definitions = courses.Select(FixtureReader.ToDomain).ToArray();
            var start = AuditEngine.TermOrder(request.StartTermCode);
            var contexts = Enumerable.Range(0, request.HorizonTerms).Select(offset =>
            {
                var order = start + offset;
                var year = order / 2;
                var spring = order % 2 == 0;
                var code = $"{year}-{(spring ? "SPRING" : "FALL")}";
                var existing = terms.SingleOrDefault(t => t.Code == code);
                // Nominal synthetic dates support the future-term guard, never an official calendar.
                var term = existing is null ? new TermDefinition(code, $"{(spring ? "Spring" : "Fall")} {year} (tentative)",
                    spring ? "Spring" : "Fall", new(year, spring ? 1 : 8, 15), new(year, spring ? 5 : 12, 15), "America/New_York", false)
                    : PlanningFixtures.ToDomain(existing);
                return new PlanningContext(student.CatalogVersion, program, definitions, attempts, term,
                    allSections.Where(s => s.TermCode == code).Select(PlanningFixtures.ToDomain).ToArray());
            }).ToArray();
            return new(student.CatalogVersion, program, definitions, attempts, contexts);
        }
        catch (JsonException) { return new(student.CatalogVersion, null, [], [], []); }
    }

    public async Task<DegreePlanResult?> GenerateAsync(string actor, DegreePlanRequest request, CancellationToken ct)
    {
        var context = await LoadAsync(actor, request, ct);
        if (context is null) return null;
        var fingerprint = Fingerprint(context, request);
        DegreePlanResult Failure(string status, string code, string message) => new(status, context.CatalogVersion, fingerprint, [], [new(code, message)]);
        var initial = audit.Calculate(context.Program, context.Courses, context.Attempts, context.CatalogVersion);
        if (initial.Status != "valid" || context.Terms.Length != request.HorizonTerms || context.Courses.Length > 200 ||
            context.Courses.Any(c => c.Code.Length > 30) || context.Terms.Any(t => eligibility.ValidateContext(t).Length > 0))
            return Failure("INVALID", "CATALOG_INVALID", "The pinned catalog or section data cannot support a trusted roadmap.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).Date);
        if (context.Terms[0].Term.StartsOn <= today || context.Attempts.Any(a => AuditEngine.TermOrder(a.Term) >= AuditEngine.TermOrder(request.StartTermCode)))
            return Failure("INVALID", "TERM_INVALID", "Choose a future term after every recorded attempt.");
        var problem = Problem(context, request, initial);
        if (problem.Terms.Any(t => t.Options.Length > 300)) return Failure("INVALID", "OPTIONS_LIMIT", "The published offering exceeds the bounded research model.");
        var proposal = await solver.SolveAsync(problem, ct);
        if (proposal.Status != "FEASIBLE")
        {
            if (proposal.Selections?.Length > 0) return Failure("ERROR", "DEGREE_RESULT_INVALID", "A non-feasible solver result contained selections.");
            return proposal.Status switch
            {
                "INFEASIBLE" => Failure("INFEASIBLE", "HORIZON_INFEASIBLE", "No complete degree plan satisfies this finite horizon and its supplied offerings/load bounds. This does not prove graduation is impossible."),
                "TIMEOUT" => Failure("TIMEOUT", "SOLVER_TIMEOUT", "No feasible roadmap was found within the bound; infeasibility is not established."),
                "UNAVAILABLE" => Failure("UNAVAILABLE", "PLANNER_UNAVAILABLE", "The research solver is unavailable. The single-term manual planner remains available."),
                _ => Failure("ERROR", "SOLVER_ERROR", "No trusted roadmap was returned.")
            };
        }
        var current = await LoadAsync(actor, request, ct);
        if (current is null || Fingerprint(current, request) != fingerprint)
            return Failure("ERROR", "PLANNING_DATA_CHANGED", "Academic records or offerings changed during solving. Refresh and retry.");
        var result = validator.Validate(current, request, proposal, fingerprint);
        if (result.Status != "FEASIBLE") return result;
        var id = Guid.NewGuid();
        AuditTrail.Add(db, clock, actor, "degree-plan.generate", id.ToString(), hash: Fingerprint(result, request));
        await db.SaveChangesAsync(ct);
        return result with { Id = id };
    }

    private static string Fingerprint(object context, DegreePlanRequest request) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { context, request }))));

    public static DegreeSolverRequest Problem(DegreeContext c, DegreePlanRequest r, DegreeAudit initial) => new(c.CatalogVersion,
        c.Courses.Select(course => new DegreeSolverCourse(course.Code, course.Credits, course.Category, course.PrerequisiteGroups)).ToArray(),
        initial.EarnedCourses.Select(e => e.Code).ToArray(), c.Attempts.Where(a => a.Grade is "A" or "B" or "C").Select(a => a.CourseCode).Distinct().ToArray(),
        c.Program!.CoreCourseCodes, new()
        {
            ["core"] = c.Courses.Where(course => course.Category == "core").Sum(course => course.Credits),
            ["cs_elective"] = c.Program.MinCsElectiveCredits,
            ["gen_ed"] = c.Program.MinGenEdCredits,
            ["free_elective"] = c.Program.MinFreeElectiveCredits
        },
        c.Program.MinTotalCredits, c.Terms.Select(t => new DegreeSolverTerm(t.Term.Code, t.Term.IsPublished,
            t.Term.IsPublished ? t.Sections.Where(s => s.AvailableSeats > 0).Select(s => new SolverOption(s.Id, s.CourseCode,
                c.Courses.Single(course => course.Code == s.CourseCode).Credits, c.Program.CoreCourseCodes.Contains(s.CourseCode), s.Days,
                SchedulingRules.Minute(s.StartLocal), SchedulingRules.Minute(s.EndLocal))).ToArray()
            : c.Courses.Where(course => course.OfferedTerms.Contains(t.Term.Kind)).Select(course => new SolverOption($"tentative:{course.Code}",
                course.Code, course.Credits, course.Category == "core", [], null, null)).ToArray())).ToArray(), r.MinCredits, r.MaxCredits);
}
