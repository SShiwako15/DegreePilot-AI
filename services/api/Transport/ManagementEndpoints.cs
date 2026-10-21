using System.Security.Claims;
using System.Text.Json;
using DegreePilot.Api.Application;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Transport;

public static class ManagementEndpoints
{
    public static void MapManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization("Admin");
        static string Actor(HttpContext c) => c.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        group.MapGet("/directory", async (AcademicDbContext db, CancellationToken ct) => Results.Ok(new
        {
            // Only the assignment directory: no emails, password hashes or academic records.
            users = await db.Users.AsNoTracking().Where(u => u.Role != "admin")
                .Select(u => new { u.Id, u.DisplayName, u.Role }).OrderBy(u => u.Id).ToArrayAsync(ct),
            assignments = await db.AdvisorAssignments.AsNoTracking().OrderBy(a => a.StudentId).ToArrayAsync(ct)
        }));
        group.MapPost("/assignments", async (AssignmentRequest r, HttpContext http, ManagementService service, CancellationToken ct) =>
        { await service.AssignAsync(Actor(http), r, ct); return Results.NoContent(); });
        group.MapGet("/catalogs", async (AcademicDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Catalogs.AsNoTracking().OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.Status, c.Revision, c.ProgramId, c.ApprovalHash }).ToArrayAsync(ct)));
        group.MapGet("/catalogs/{version}", async (string version, AcademicDbContext db, CancellationToken ct) =>
        {
            var edition = await db.Catalogs.AsNoTracking().SingleOrDefaultAsync(c => c.Id == version, ct);
            if (edition is null) return Results.NotFound();
            var courses = await db.Courses.AsNoTracking().Where(c => c.CatalogVersion == version).OrderBy(c => c.Code).ToArrayAsync(ct);
            return Results.Ok(new
            {
                edition.Id,
                edition.Status,
                edition.Revision,
                program = JsonSerializer.Deserialize<ProgramDefinition>(edition.RulesJson, FixtureReader.Json),
                courses = courses.Select(FixtureReader.ToDomain)
            });
        });
        group.MapPost("/catalogs", async (CatalogCloneRequest r, HttpContext http, ManagementService service, CancellationToken ct) =>
            Results.Ok(new { id = await service.CloneCatalogAsync(Actor(http), r, ct) }));
        group.MapPost("/catalogs/{version}/draft", async (string version, CatalogDraftRequest r, HttpContext http, ManagementService service, CancellationToken ct) =>
        { await service.EditCatalogAsync(Actor(http), version, r, ct); return Results.NoContent(); });
        group.MapPost("/catalogs/{version}/publish", async (string version, RevisionRequest r, HttpContext http, ManagementService service, CancellationToken ct) =>
        { await service.PublishCatalogAsync(Actor(http), version, r.Revision, ct); return Results.NoContent(); });
        group.MapGet("/policies", async (AcademicDbContext db, CancellationToken ct) =>
            Results.Ok(await db.PolicyDocuments.AsNoTracking().OrderBy(p => p.CatalogVersion).ThenBy(p => p.Version).ToArrayAsync(ct)));
        group.MapGet("/policies/{id}", async (string id, AcademicDbContext db, CancellationToken ct) =>
        {
            var doc = await db.PolicyDocuments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            return doc is null ? Results.NotFound() : Results.Ok(new
            {
                document = doc,
                sections = await db.PolicySections.AsNoTracking().Where(s => s.DocumentId == id).OrderBy(s => s.Id)
                    .Select(s => new PolicyText(s.Id, s.Title, s.Text)).ToArrayAsync(ct)
            });
        });
        group.MapPost("/policies", async (PolicyCloneRequest r, HttpContext http, ManagementService service, CancellationToken ct) =>
            Results.Ok(new { id = await service.ClonePolicyAsync(Actor(http), r.SourceDocumentId, ct) }));
        group.MapPost("/policies/{id}/draft", async (string id, PolicyDraftRequest r, HttpContext http, ManagementService service, CancellationToken ct) =>
        { await service.EditPolicyAsync(Actor(http), id, r, ct); return Results.NoContent(); });
        group.MapPost("/policies/{id}/publish", async (string id, RevisionRequest r, HttpContext http, ManagementService service, CancellationToken ct) =>
        { await service.PublishPolicyAsync(Actor(http), id, r.Revision, ct); return Results.NoContent(); });
        group.MapGet("/sections", async (AcademicDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Sections.AsNoTracking().OrderBy(s => s.TermCode).ThenBy(s => s.Id).ToArrayAsync(ct)));
        group.MapPost("/sections/seats", async (SectionSeatsRequest r, HttpContext http, ManagementService service, CancellationToken ct) =>
        { await service.SetSeatsAsync(Actor(http), r, ct); return Results.NoContent(); });
        group.MapGet("/audit", async (AcademicDbContext db, CancellationToken ct) =>
            Results.Ok(await db.MutationAudits.AsNoTracking().OrderByDescending(a => a.OccurredAt).Take(100).ToArrayAsync(ct)));
        group.MapGet("/operations", (OperationalMetrics metrics) => Results.Ok(metrics.Snapshot()));
    }
}
