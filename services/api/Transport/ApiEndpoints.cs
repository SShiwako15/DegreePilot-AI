using System.Security.Claims;
using DegreePilot.Api.Application;
using DegreePilot.Api.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Transport;

public sealed record LoginRequest(string? Username, string? Password);

public static class ApiEndpoints
{
    private static string Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public static void MapAcademicEndpoints(this WebApplication app, bool demoEnabled)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", service = "degreepilot-api" }));
        app.MapGet("/health/ready", async (AcademicDbContext db) =>
        {
            try
            {
                // Query an application table, so an unmigrated database is not ready.
                await db.Catalogs.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync();
                await db.Terms.AsNoTracking().Select(x => x.Code).Take(1).ToArrayAsync();
                await db.Plans.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync();
                await db.PolicyDocuments.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync();
                await db.PolicySections.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync();
                await db.AdvisorAssignments.AsNoTracking().Take(1).ToArrayAsync();
                await db.MutationAudits.AsNoTracking().Take(1).ToArrayAsync();
                return Results.Ok(new { status = "ready" });
            }
            catch (Exception) { return Results.Json(new { status = "unavailable" }, statusCode: 503); }
        });
        app.MapGet("/api/auth/csrf", (IAntiforgery csrf, HttpContext context) =>
            Results.Ok(new { token = csrf.GetAndStoreTokens(context).RequestToken }));
        app.MapPost("/api/auth/login", async (LoginRequest request, AcademicDbContext db,
            IPasswordHasher<AppUser> hasher, HttpContext context) =>
        {
            if (!demoEnabled) return Results.Problem(statusCode: 503, title: "Local demo sign-in is disabled.");
            if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 100 ||
                string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
                return Results.Problem(statusCode: 400, title: "Enter a valid demo username and password.");
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == request.Username);
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
                return Results.Problem(statusCode: 401, title: "The username or password is incorrect.");
            Claim[] claims = [new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Role, user.Role)];
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                new AuthenticationProperties { IsPersistent = false });
            return Results.NoContent();
        }).RequireRateLimiting("LoginBudget");
        app.MapPost("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapGet("/api/me", async (HttpContext context, AcademicDbContext db, IAdvisorClient advisor, CancellationToken ct) =>
        {
            var id = Actor(context);
            var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == id);
            var student = await db.Students.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == id);
            return Results.Ok(new
            {
                user.Id,
                user.DisplayName,
                user.Role,
                student?.CatalogVersion,
                ai = user.Role == "student" ? await advisor.StatusAsync(ct)
                    : new DegreePilot.Api.Domain.AdvisorStatus(false, "AI advising is restricted to the student's own workspace.")
            });
        }).RequireAuthorization();
        app.MapGet("/api/catalog/versions", async (AcademicDbContext db) =>
            Results.Ok(await db.Catalogs.AsNoTracking().Where(x => x.Status == "published").Select(x => new { catalogVersion = x.Id, x.ProgramId }).ToArrayAsync())).RequireAuthorization();
        app.MapGet("/api/catalog/{version}/courses", async (string version, AcademicDbContext db) =>
        {
            if (!await db.Catalogs.AnyAsync(x => x.Id == version && x.Status == "published")) return Results.NotFound();
            var courses = await db.Courses.AsNoTracking().Where(x => x.CatalogVersion == version).OrderBy(x => x.Code).ToArrayAsync();
            return Results.Ok(courses.Select(FixtureReader.ToDomain));
        }).RequireAuthorization();
        app.MapGet("/api/students/me/transcript", async (HttpContext context, AcademicQueries query) =>
        {
            var data = await query.TranscriptAsync(Actor(context));
            return data is null ? Results.NotFound() : Results.Ok(data);
        }).RequireAuthorization("Student");
        app.MapGet("/api/students/me/degree-audit", async (HttpContext context, AcademicQueries query) =>
        {
            var data = await query.AuditAsync(Actor(context));
            return data is null ? Results.NotFound() : Results.Ok(data);
        }).RequireAuthorization("Student");
        app.MapGet("/api/students/{studentId}/transcript", async (string studentId, HttpContext context, WorkspaceAccess access, AcademicQueries query, CancellationToken ct) =>
        {
            if (!await access.CanReadAsync(context.User, studentId, ct)) return Results.NotFound();
            var data = await query.TranscriptAsync(studentId);
            return data is null ? Results.NotFound() : Results.Ok(data);
        }).RequireAuthorization("AcademicReader");
        app.MapGet("/api/students/{studentId}/degree-audit", async (string studentId, HttpContext context, WorkspaceAccess access, AcademicQueries query, CancellationToken ct) =>
        {
            if (!await access.CanReadAsync(context.User, studentId, ct)) return Results.NotFound();
            var data = await query.AuditAsync(studentId);
            return data is null ? Results.NotFound() : Results.Ok(data);
        }).RequireAuthorization("AcademicReader");
    }
}
