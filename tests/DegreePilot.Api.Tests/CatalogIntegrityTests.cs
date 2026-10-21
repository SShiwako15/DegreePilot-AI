using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DegreePilot.Api.Tests;

public class CatalogIntegrityTests
{
    [Theory]
    [InlineData("missing-minimum")]
    [InlineData("unknown-rule")]
    [InlineData("null-prerequisites")]
    public async Task MalformedStoredRulesReturnInvalidAudit(string mutation)
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        (await AuthorizationTests.Login(client, "student-ava")).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var edition = await db.Catalogs.SingleAsync();
        var rules = JsonNode.Parse(edition.RulesJson)!.AsObject();
        if (mutation == "missing-minimum") rules.Remove("min_gen_ed_credits");
        if (mutation == "unknown-rule") rules["min_science_credits"] = 12;
        if (mutation == "null-prerequisites")
            (await db.Courses.SingleAsync(c => c.Code == "CS101")).PrerequisitesJson = "null";
        edition.RulesJson = rules.ToJsonString();
        await db.SaveChangesAsync();

        var audit = await client.GetFromJsonAsync<DegreeAudit>("/api/students/me/degree-audit");
        Assert.Equal("invalid", audit!.Status);
        Assert.Null(audit.EarnedCredits);
        Assert.Null(audit.DegreeComplete);
        Assert.Contains(audit.Issues, i => i.Code == "CATALOG_INVALID");
    }

    [Fact]
    public async Task ANewCatalogDoesNotChangeAnExistingStudentsAudit()
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        (await AuthorizationTests.Login(client, "student-ava")).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var rules = JsonNode.Parse((await db.Catalogs.SingleAsync()).RulesJson)!;
        rules["catalog_version"] = "NSU-BSCS-2027";
        rules["min_total_credits"] = 130;
        db.Catalogs.Add(new() { Id = "NSU-BSCS-2027", ProgramId = "BSCS", RulesJson = rules.ToJsonString(), SourceHash = "new-edition" });
        await db.SaveChangesAsync();

        var audit = await client.GetFromJsonAsync<DegreeAudit>("/api/students/me/degree-audit");
        Assert.Equal("NSU-BSCS-2026", audit!.CatalogVersion);
        Assert.Equal(120, audit.RequiredCredits);
        Assert.Equal(35, audit.EarnedCredits);
    }

    [Fact]
    public async Task ReadinessFailsWhenApplicationSchemaIsMissing()
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await db.Database.EnsureDeletedAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task DemoAuthenticationCannotStartInProduction()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Demo:Enabled"] = "true" }));
        });
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Demo accounts must never be enabled", error.Message);
    }
}
