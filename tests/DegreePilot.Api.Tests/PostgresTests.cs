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
            for (var startup = 0; startup < 2; startup++)
            {
                await using var factory = new AcademicApiFactory(settings.ConnectionString);
                using var client = factory.CreateClient();
                (await AuthorizationTests.Login(client, "student-ava")).EnsureSuccessStatusCode();
                Assert.Equal(35, (await client.GetFromJsonAsync<DegreeAudit>("/api/students/me/degree-audit"))!.EarnedCredits);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/students/student-noah/transcript")).StatusCode);
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
                Assert.Single(await db.Database.GetAppliedMigrationsAsync());
                Assert.False(db.Database.HasPendingModelChanges());
                Assert.Equal(13, await db.Attempts.CountAsync());
                Assert.Equal(36, await db.Courses.CountAsync());
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
