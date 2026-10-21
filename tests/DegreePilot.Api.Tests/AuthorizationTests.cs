using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DegreePilot.Api.Tests;

public class AuthorizationTests
{
    internal static async Task SetCsrf(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
    }
    internal static async Task<HttpResponseMessage> Login(HttpClient client, string username)
    {
        await SetCsrf(client);
        return await client.PostAsJsonAsync("/api/auth/login", new { username, password = AcademicApiFactory.Password });
    }

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/students/me/transcript")]
    [InlineData("/api/students/me/degree-audit")]
    [InlineData("/api/students/student-ava/transcript")]
    public async Task AnonymousCannotReadPrivateData(string path)
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task StudentsAreIsolatedByPathAndQueryAndLogoutClearsSession()
    {
        await using var factory = new AcademicApiFactory();
        using var ava = factory.CreateClient();
        using var noah = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await Login(ava, "student-ava")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Login(noah, "student-noah")).StatusCode);
        var avaAudit = await ava.GetFromJsonAsync<DegreeAudit>("/api/students/me/degree-audit");
        Assert.Equal(35, avaAudit!.EarnedCredits);
        var noahAudit = await noah.GetFromJsonAsync<DegreeAudit>("/api/students/me/degree-audit?studentId=student-ava&catalogVersion=other");
        Assert.Equal(7, noahAudit!.EarnedCredits);
        foreach (var resource in new[] { "transcript", "degree-audit" })
        {
            var denied = await noah.GetAsync($"/api/students/student-ava/{resource}");
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.DoesNotContain("CS201", await denied.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await ava.GetAsync($"/api/students/student-noah/{resource}")).StatusCode);
        }
        await SetCsrf(noah);
        Assert.Equal(HttpStatusCode.NoContent, (await noah.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await noah.GetAsync("/api/me")).StatusCode);
    }

    [Theory]
    [InlineData("advisor-morgan")]
    [InlineData("admin-robin")]
    public async Task OtherRolesCannotUseStudentEndpoints(string account)
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        (await Login(client, account)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/students/me/degree-audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/students/student-ava/transcript")).StatusCode);
    }

    [Fact]
    public async Task CookiesAndCsrfAreEnforcedWithoutCrossOriginSharing()
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { username = "student-ava", password = AcademicApiFactory.Password })).StatusCode);
        var response = await Login(client, "student-ava");
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), x => x.StartsWith("degreepilot.session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.CacheControl?.NoStore);
        // The anonymous login token is no longer valid for the signed-in actor.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.invalid");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.False((await client.GetAsync("/api/me")).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task BadCredentialsAndInvalidInputsDoNotAuthenticate()
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        await SetCsrf(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { username = "student-ava", password = "wrong-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { username = "", password = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task SeedingIsIdempotentAndRelationalConstraintsRejectDuplicatesAndCatalogMismatch()
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await DemoSeeder.SeedAsync(db, AcademicApiFactory.Password, scope.ServiceProvider.GetRequiredService<AuditEngine>());
        Assert.Equal(4, await db.Users.CountAsync());
        Assert.Equal(36, await db.Courses.CountAsync());
        Assert.Equal(13, await db.Attempts.CountAsync());
        db.Attempts.Add(new() { Id = Guid.NewGuid(), StudentId = "student-ava", CatalogVersion = "NSU-BSCS-2026", CourseCode = "CS101", Term = "2025-FALL", Grade = "A" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Attempts.Add(new() { Id = Guid.NewGuid(), StudentId = "student-ava", CatalogVersion = "NEW-CATALOG", CourseCode = "CS101", Term = "2028-FALL", Grade = "A" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var catalog = await db.Catalogs.SingleAsync();
        catalog.SourceHash = "changed";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DemoSeeder.SeedAsync(db, AcademicApiFactory.Password, scope.ServiceProvider.GetRequiredService<AuditEngine>()));
    }

    [Fact]
    public async Task UnknownCatalogReturns404AndHealthChecksWork()
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        (await client.GetAsync("/api/health")).EnsureSuccessStatusCode();
        (await client.GetAsync("/health/ready")).EnsureSuccessStatusCode();
        (await Login(client, "student-ava")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/catalog/unknown/courses")).StatusCode);
    }
}
