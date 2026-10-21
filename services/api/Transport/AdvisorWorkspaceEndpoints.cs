using System.Data;
using System.Security.Claims;
using System.Text.Json;
using DegreePilot.Api.Application;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Transport;

public static class AdvisorWorkspaceEndpoints
{
    public static void MapAdvisorWorkspaceEndpoints(this WebApplication app)
    {
        static string Actor(HttpContext c) => c.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var group = app.MapGroup("/api/advisor-workspace").RequireAuthorization("Advisor");
        group.MapGet("/students", async (HttpContext http, AcademicDbContext db, CancellationToken ct) =>
        {
            var actor = Actor(http);
            return Results.Ok(await (from a in db.AdvisorAssignments.AsNoTracking()
                                     join u in db.Users.AsNoTracking() on a.StudentId equals u.Id
                                     join s in db.Students.AsNoTracking() on a.StudentId equals s.UserId
                                     where a.AdvisorId == actor && a.Active
                                     select new { u.Id, u.DisplayName, s.CatalogVersion }).ToArrayAsync(ct));
        });
        group.MapGet("/students/{studentId}/planning/{term}", async (string studentId, string term,
            HttpContext http, WorkspaceAccess access, PlanningService service, EligibilityEngine eligibility, CancellationToken ct) =>
        {
            if (!await access.AssignedAsync(Actor(http), studentId, ct)) return Results.NotFound();
            var context = await service.LoadAsync(studentId, term, ct);
            return context is null ? Results.NotFound() : Results.Ok(new
            {
                context.CatalogVersion,
                context.Term,
                options = eligibility.Options(context),
                issues = eligibility.ValidateContext(context)
            });
        });
        group.MapGet("/terms", async (AcademicDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Terms.AsNoTracking().OrderBy(t => t.StartsOn).ToArrayAsync(ct)).Select(PlanningFixtures.ToDomain)));
        group.MapPost("/students/{studentId}/suggestions", async (string studentId, SelectionRequest request,
            HttpContext http, AcademicDbContext db, WorkspaceAccess access, PlanningService service, PlanValidator validator, TimeProvider clock, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var actor = Actor(http);
            if (!await access.AssignedAsync(actor, studentId, ct)) return Results.NotFound();
            if (request.Constraints is null || request.SelectionIds is null || request.SelectionIds.Length > 30)
                return Results.Problem(statusCode: 400, title: "Invalid suggestion.");
            var context = await service.LoadAsync(studentId, request.Constraints.TermCode, ct);
            if (context is null) return Results.NotFound();
            var result = validator.Validate(context, request.Constraints, request.SelectionIds);
            if (result.Status != "FEASIBLE") return Results.Json(result, statusCode: 422);
            var suggestion = new AdvisorSuggestion
            {
                Id = Guid.NewGuid(),
                AdvisorId = actor,
                StudentId = studentId,
                CatalogVersion = context.CatalogVersion,
                TermCode = context.Term.Code,
                CreatedAt = clock.GetUtcNow(),
                ConstraintsJson = JsonSerializer.Serialize(request.Constraints),
                SelectionIdsJson = JsonSerializer.Serialize(request.SelectionIds)
            };
            db.AdvisorSuggestions.Add(suggestion);
            AuditTrail.Add(db, clock, actor, "advisor.suggest", suggestion.Id.ToString());
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { suggestion.Id, plan = result });
        });
        app.MapGet("/api/students/{studentId}/plans", async (string studentId, HttpContext http, WorkspaceAccess access,
            AcademicDbContext db, PlanningService service, CancellationToken ct) =>
        {
            if (!await access.CanReadAsync(http.User, studentId, ct)) return Results.NotFound();
            var plans = await db.Plans.AsNoTracking().Where(p => p.StudentId == studentId).OrderByDescending(p => p.CreatedAt).ToArrayAsync(ct);
            var result = new List<PlanResult>();
            foreach (var plan in plans) result.Add(await service.ReadAsync(plan, ct));
            return Results.Ok(result);
        }).RequireAuthorization("AcademicReader");
        app.MapGet("/api/students/{studentId}/suggestions", async (string studentId, HttpContext http, WorkspaceAccess access,
            AcademicDbContext db, PlanningService service, CancellationToken ct) =>
        {
            if (!await access.CanReadAsync(http.User, studentId, ct)) return Results.NotFound();
            var suggestions = await db.AdvisorSuggestions.AsNoTracking().Where(s => s.StudentId == studentId).OrderByDescending(s => s.CreatedAt).ToArrayAsync(ct);
            var results = new List<object>();
            foreach (var s in suggestions)
                results.Add(new
                {
                    s.Id,
                    s.AdvisorId,
                    s.CreatedAt,
                    constraints = JsonSerializer.Deserialize<PlanRequest>(s.ConstraintsJson),
                    plan = await service.ReadAsync(new SchedulePlan
                    {
                        Id = s.Id,
                        StudentId = studentId,
                        CatalogVersion = s.CatalogVersion,
                        TermCode = s.TermCode,
                        ConstraintsJson = s.ConstraintsJson,
                        SelectionIdsJson = s.SelectionIdsJson,
                        CreatedAt = s.CreatedAt
                    }, ct)
                });
            return Results.Ok(results);
        }).RequireAuthorization("AcademicReader");
    }
}
