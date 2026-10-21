using DegreePilot.Api.Persistence;
using DegreePilot.Api.Application;
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
    private readonly IPlannerClient? planner;
    private readonly IAdvisorClient advisor;
    private readonly int advisorLimit;
    private readonly int loginLimit;
    private readonly int plannerLimit;
    private readonly TimeProvider clock;
    private readonly ILoggerProvider? logs;
    public AcademicApiFactory(string? postgresConnection = null, IPlannerClient? planner = null, IAdvisorClient? advisor = null,
        int advisorLimit = 1000, int loginLimit = 1000, int plannerLimit = 1000, TimeProvider? clock = null, ILoggerProvider? logs = null)
    {
        this.postgresConnection = postgresConnection;
        this.planner = planner;
        this.advisor = advisor ?? new StubAdvisor(false);
        this.advisorLimit = advisorLimit;
        this.loginLimit = loginLimit;
        this.plannerLimit = plannerLimit;
        this.clock = clock ?? new DemoTestClock();
        this.logs = logs;
        if (postgresConnection is null) connection.Open();
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        if (logs is not null) builder.ConfigureLogging(logging => logging.AddProvider(logs));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:Enabled"] = "true",
            ["Demo:Password"] = Password,
            ["Advisor:RequestsPerMinute"] = advisorLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Security:LoginRequestsPerMinute"] = loginLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Security:PlannerRequestsPerMinute"] = plannerLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Logging:LogLevel:Default"] = logs is null ? "None" : "Information",
            ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "None"
        }));
        builder.ConfigureServices(services =>
        {
            // Each test host owns isolated, disposable cookie keys; never write to a user's key store.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock);
            services.RemoveAll<IAdvisorClient>();
            services.AddSingleton(advisor);
            if (planner is not null)
            {
                services.RemoveAll<IPlannerClient>();
                services.AddSingleton(planner);
            }
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
