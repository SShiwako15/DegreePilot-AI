using System.Security.Claims;
using DegreePilot.Api.Application;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Transport;

public static class AdvisorEndpoints
{
    public static void MapAdvisorEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization("Student");
        static string Actor(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        group.MapGet("/advisor/status", (IAdvisorClient client, CancellationToken ct) => client.StatusAsync(ct));
        group.MapPost("/advisor/chat", async (ChatRequest request, HttpContext http, AdvisorService service, CancellationToken ct) =>
        {
            if (!AdvisorRules.Valid(request)) return Results.Problem(statusCode: 400, title: "Use a question of 1–1000 characters, at most four prior questions, and a supported term code.");
            var result = await service.AnswerAsync(Actor(http), request, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireRateLimiting("AdvisorBudget");
        group.MapGet("/policies", async (HttpContext http, AcademicDbContext db, PolicyRetrieval retrieval, CancellationToken ct) =>
        {
            var actor = Actor(http);
            var version = await db.Students.Where(s => s.UserId == actor).Select(s => s.CatalogVersion).SingleOrDefaultAsync(ct);
            return version is null ? Results.NotFound() : Results.Ok(await retrieval.ReadAsync(version, ct: ct));
        });
        group.MapGet("/policies/sections/{id}", async (string id, HttpContext http, AcademicDbContext db, PolicyRetrieval retrieval, CancellationToken ct) =>
        {
            var actor = Actor(http);
            var version = await db.Students.Where(s => s.UserId == actor).Select(s => s.CatalogVersion).SingleOrDefaultAsync(ct);
            var sections = version is null ? [] : await retrieval.ReadAsync(version, [id], ct);
            return sections.Length != 1 ? Results.NotFound() : Results.Ok(sections[0]);
        });
    }
}
