using DegreePilot.Api.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DegreePilot.Api.Tests;

public sealed class AcademicApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "Test-Synthetic-Only!2026";
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string? postgresConnection;
    public AcademicApiFactory(string? postgresConnection = null)
    {
        this.postgresConnection = postgresConnection;
        if (postgresConnection is null) connection.Open();
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:Enabled"] = "true",
            ["Demo:Password"] = Password,
            ["Logging:LogLevel:Default"] = "None",
            ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "None"
        }));
        builder.ConfigureServices(services =>
        {
            // Each test host owns isolated, disposable cookie keys; never write to a user's key store.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<DbContextOptions<AcademicDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AcademicDbContext>>();
            services.AddDbContext<AcademicDbContext>(options =>
            {
                if (postgresConnection is null) options.UseSqlite(connection);
                else options.UseNpgsql(postgresConnection);
            });
        });
    }
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await connection.DisposeAsync();
    }
}
