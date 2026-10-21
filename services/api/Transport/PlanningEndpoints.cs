using System.Security.Claims;
using DegreePilot.Api.Application;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Transport;

public static class PlanningEndpoints
{
    public static void MapPlanningEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization("Student");
        static string Actor(HttpContext c) => c.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        static IResult Invalid() => Results.Problem(statusCode: 400, title: "Invalid planning request.");

        group.MapGet("/terms", async (AcademicDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Terms.AsNoTracking().OrderBy(t => t.StartsOn).ToArrayAsync(ct)).Select(PlanningFixtures.ToDomain)));
        group.MapGet("/courses/{code}/eligibility", async (string code, string term, HttpContext http,
            PlanningService service, EligibilityEngine engine, CancellationToken ct) =>
        {
            var context = await service.LoadAsync(Actor(http), term, ct);
            return context is null ? Results.NotFound() : Results.Ok(engine.Evaluate(context, code));
        });
        group.MapGet("/courses", async (string term, string? search, string? category, string? status,
            HttpContext http, PlanningService service, EligibilityEngine engine, CancellationToken ct) =>
        {
            var context = await service.LoadAsync(Actor(http), term, ct);
            if (context is null) return Results.NotFound();
            var issues = engine.ValidateContext(context);
            if (issues.Length > 0) return Results.Json(new { issues }, statusCode: 422);
            var rows = context.Courses.Where(c => (string.IsNullOrWhiteSpace(search) ||
                c.Code.Contains(search, StringComparison.OrdinalIgnoreCase) || c.Title.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(category) || c.Category == category)).Select(c => new CourseEligibility(c, engine.Evaluate(context, c.Code)));
            return Results.Ok(rows.Where(r => string.IsNullOrEmpty(status) || r.Eligibility.Status == status));
        });
        group.MapGet("/terms/{termCode}/sections", async (string termCode, HttpContext http,
            PlanningService service, EligibilityEngine engine, CancellationToken ct) =>
        {
            var context = await service.LoadAsync(Actor(http), termCode, ct);
            return context is null ? Results.NotFound() : Results.Ok(new
            {
                context.CatalogVersion,
                term = context.Term,
                options = engine.Options(context),
                sections = context.Term.IsPublished ? context.Sections : [],
                issues = engine.ValidateContext(context)
            });
        });
        group.MapPost("/plans/generate", async (PlanRequest request, HttpContext http, PlanningService service, CancellationToken ct) =>
        {
            if (SchedulingRules.ValidateRequest(request).Length > 0) return Invalid();
            var context = await service.LoadAsync(Actor(http), request.TermCode, ct);
            return context is null ? Results.NotFound() : Results.Ok(await service.GenerateAsync(Actor(http), context, request, ct));
        }).RequireRateLimiting("PlannerBudget");
        group.MapPost("/plans/validate", async (SelectionRequest request, HttpContext http, PlanningService service, PlanValidator validator, CancellationToken ct) =>
        {
            if (request.Constraints is null || request.SelectionIds is null || request.SelectionIds.Length > 30 || SchedulingRules.ValidateRequest(request.Constraints).Length > 0) return Invalid();
            var context = await service.LoadAsync(Actor(http), request.Constraints.TermCode, ct);
            return context is null ? Results.NotFound() : Results.Ok(validator.Validate(context, request.Constraints, request.SelectionIds));
        });
        group.MapPost("/plans/save", async (SelectionRequest request, HttpContext http, PlanningService service, CancellationToken ct) =>
        {
            if (request.Constraints is null || request.SelectionIds is null || request.SelectionIds.Length > 30 || SchedulingRules.ValidateRequest(request.Constraints).Length > 0) return Invalid();
            var context = await service.LoadAsync(Actor(http), request.Constraints.TermCode, ct);
            if (context is null) return Results.NotFound();
            var result = await service.SaveAsync(Actor(http), context, request, ct);
            return result.Status == "FEASIBLE" ? Results.Ok(result) : Results.Json(result, statusCode: 422);
        });
        group.MapGet("/students/me/plans", async (HttpContext http, AcademicDbContext db, PlanningService service, CancellationToken ct) =>
        {
            var actor = Actor(http);
            var plans = await db.Plans.AsNoTracking().Where(p => p.StudentId == actor).ToArrayAsync(ct);
            var results = new List<PlanResult>();
            foreach (var plan in plans.OrderByDescending(p => p.CreatedAt)) results.Add(await service.ReadAsync(plan, ct));
            return Results.Ok(results);
        });
        group.MapGet("/plans/{id:guid}", async (Guid id, HttpContext http, AcademicDbContext db, PlanningService service, CancellationToken ct) =>
        {
            var actor = Actor(http);
            var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.StudentId == actor, ct);
            return plan is null ? Results.NotFound() : Results.Ok(await service.ReadAsync(plan, ct));
        });
    }
}
