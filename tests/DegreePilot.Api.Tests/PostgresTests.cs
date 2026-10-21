using System.Net;
using System.Net.Http.Json;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DegreePilot.Api.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DEGREEPILOT_TEST_DATABASE")))
            Skip = "Set DEGREEPILOT_TEST_DATABASE to a local PostgreSQL connection with CREATEDB permission.";
    }
}

public class PostgresTests
{
    [PostgresFact]
    public async Task RealPostgresMigrationSeedRestartAndAuthorization()
    {
        var connectionString = Environment.GetEnvironmentVariable("DEGREEPILOT_TEST_DATABASE")!;
        var databaseName = "degreepilot_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        // Identifier is generated internally from a fixed prefix and a GUID.
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName };
            var policySectionCount = 9;
            for (var startup = 0; startup < 2; startup++)
            {
                await using var factory = new AcademicApiFactory(settings.ConnectionString, new StubPlanner());
                using var client = factory.CreateClient();
                (await AuthorizationTests.Login(client, "student-ava")).EnsureSuccessStatusCode();
                Assert.Equal(35, (await client.GetFromJsonAsync<DegreeAudit>("/api/students/me/degree-audit"))!.EarnedCredits);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/students/student-noah/transcript")).StatusCode);
                Assert.True((await client.GetFromJsonAsync<Eligibility>("/api/courses/CS301/eligibility?term=2027-SPRING"))!.Eligible);
                if (startup == 0)
                {
                    await AuthorizationTests.SetCsrf(client);
                    var request = new PlanRequest("2027-SPRING", 12, 12);
                    var generated = await (await client.PostAsJsonAsync("/api/plans/generate", request)).Content.ReadFromJsonAsync<PlanResult>();
                    Assert.Equal("FEASIBLE", generated!.Status);
                    (await client.PostAsJsonAsync("/api/plans/save", new SelectionRequest(request, generated.Selections.Select(s => s.Id).ToArray()))).EnsureSuccessStatusCode();
                    using var adminClient = factory.CreateClient();
                    (await AuthorizationTests.Login(adminClient, "admin-robin")).EnsureSuccessStatusCode();
                    await AuthorizationTests.SetCsrf(adminClient);
                    (await adminClient.PostAsJsonAsync("/api/admin/assignments", new AssignmentRequest("advisor-morgan", "student-ava", false, 1))).EnsureSuccessStatusCode();
                    var documents = (await adminClient.GetFromJsonAsync<PolicyDocument[]>("/api/admin/policies"))!;
                    var source = documents.First(d => d.Title.Contains("Catalog", StringComparison.OrdinalIgnoreCase));
                    var created = await (await adminClient.PostAsJsonAsync("/api/admin/policies", new PolicyCloneRequest(source.Id)))
                        .Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    (await adminClient.PostAsJsonAsync($"/api/admin/policies/{created.GetProperty("id").GetString()}/publish", new RevisionRequest(1))).EnsureSuccessStatusCode();
                    using var countScope = factory.Services.CreateScope();
                    policySectionCount = await countScope.ServiceProvider.GetRequiredService<AcademicDbContext>().PolicySections.CountAsync();
                }
                Assert.Single((await client.GetFromJsonAsync<PlanResult[]>("/api/students/me/plans"))!);
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
                Assert.Equal(4, (await db.Database.GetAppliedMigrationsAsync()).Count());
                Assert.False(db.Database.HasPendingModelChanges());
                Assert.Equal(13, await db.Attempts.CountAsync());
                Assert.Equal(36, await db.Courses.CountAsync());
                Assert.Equal(19, await db.Sections.CountAsync());
                Assert.Equal(3, await db.Terms.CountAsync());
                Assert.Equal(1, await db.Plans.CountAsync());
                Assert.Equal(3, await db.PolicyDocuments.CountAsync());
                Assert.Equal(policySectionCount, await db.PolicySections.CountAsync());
                Assert.False(Assert.Single(await db.AdvisorAssignments.ToArrayAsync()).Active);
                Assert.Single(await db.MutationAudits.Where(a => a.Action == "plan.save").ToArrayAsync());
                Assert.Equal("CAT-PREREQ-CS401", (await client.GetFromJsonAsync<PolicyCitation>("/api/policies/sections/CAT-PREREQ-CS401"))!.PolicySectionId);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
