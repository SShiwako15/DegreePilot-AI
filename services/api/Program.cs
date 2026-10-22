using System.Security.Claims;
using DegreePilot.Api.Application;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using DegreePilot.Api.Transport;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var localEnvironment = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 512 * 1024);
// EF exceptions can include data values. Operational errors use fixed response codes instead.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Update", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);

builder.Services.AddDbContext<AcademicDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Database") ?? "Host=localhost;Database=degreepilot"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AuditEngine>();
builder.Services.AddSingleton<EligibilityEngine>();
builder.Services.AddSingleton<PlanValidator>();
builder.Services.AddSingleton<DegreePlanValidator>();
builder.Services.AddScoped<DegreePlanningService>();
builder.Services.AddHttpClient<IDegreePlannerClient, DegreePlannerClient>((services, http) =>
{
    http.BaseAddress = new Uri(services.GetRequiredService<IConfiguration>()["Planner:BaseUrl"] ?? "http://127.0.0.1:8000/");
    http.Timeout = TimeSpan.FromSeconds(12);
});
builder.Services.AddScoped<PlanningService>();
builder.Services.AddSingleton<PolicySources>();
builder.Services.AddScoped<PolicyRetrieval>();
builder.Services.AddScoped<AdvisorService>();
builder.Services.AddScoped<WorkspaceAccess>();
builder.Services.AddScoped<ManagementService>();
builder.Services.AddSingleton<OperationalMetrics>();
builder.Services.AddHttpClient<IAdvisorClient, AdvisorClient>((services, http) =>
{
    http.BaseAddress = new Uri(services.GetRequiredService<IConfiguration>()["Planner:BaseUrl"] ?? "http://127.0.0.1:8000/");
    http.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<IPlannerClient, PlannerClient>((services, http) =>
{
    var config = services.GetRequiredService<IConfiguration>();
    http.BaseAddress = new Uri(config["Planner:BaseUrl"] ?? "http://127.0.0.1:8000/");
    http.Timeout = TimeSpan.FromSeconds(12);
});
builder.Services.AddScoped<AcademicQueries>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("LoginBudget", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Clamp(builder.Configuration.GetValue("Security:LoginRequestsPerMinute", 20), 1, 1000),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("PlannerBudget", http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Clamp(builder.Configuration.GetValue("Security:PlannerRequestsPerMinute", 12), 1, 1000),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("AdvisorBudget", http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Clamp(builder.Configuration.GetValue("Advisor:RequestsPerMinute", 5), 1, 1000),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Results.Problem(statusCode: 429, title: "Request limit reached. Please wait a minute before trying again.")
            .ExecuteAsync(context.HttpContext);
    };
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "degreepilot.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = localEnvironment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(2);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = async context =>
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<AcademicDbContext>();
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = context.Principal?.FindFirstValue(ClaimTypes.Role);
        if (!await db.Users.AnyAsync(x => x.Id == id && x.Role == role)) context.RejectPrincipal();
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Student", policy => policy.RequireRole("student"));
    options.AddPolicy("Advisor", policy => policy.RequireRole("advisor"));
    options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
    options.AddPolicy("AcademicReader", policy => policy.RequireRole("student", "advisor"));
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "degreepilot.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = localEnvironment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

var app = builder.Build();
// Read final configuration after host/test overrides have been applied.
var demoEnabled = app.Configuration.GetValue<bool>("Demo:Enabled");
if (demoEnabled && !(app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")))
    throw new InvalidOperationException("Demo accounts must never be enabled outside Development or Testing.");
// Return generic errors without logging exception text or SQL parameters containing records.
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
    if (context.Request.ContentLength > 512 * 1024)
    {
        await Results.Problem(statusCode: 413, title: "Request exceeds the 512 KiB limit.").ExecuteAsync(context);
        return;
    }
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (BadHttpRequestException)
    {
        await Results.Problem(statusCode: 400, title: "The request body is invalid.").ExecuteAsync(context);
    }
    catch (WorkflowException ex)
    {
        await Results.Problem(statusCode: ex.Status, title: ex.Message).ExecuteAsync(context);
    }
    catch (DbUpdateException)
    {
        await Results.Problem(statusCode: 409, title: "The data changed or conflicts with existing records. Refresh before retrying.")
            .ExecuteAsync(context);
    }
    catch (Npgsql.PostgresException ex) when (ex.SqlState is "40001" or "40P01")
    {
        await Results.Problem(statusCode: 409, title: "A concurrent change was detected. Refresh before retrying.")
            .ExecuteAsync(context);
    }
    catch (Exception)
    {
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "same-origin";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
        await Results.Problem(statusCode: 500, title: "The request could not be completed.",
            extensions: new Dictionary<string, object?> { ["code"] = "SERVICE_ERROR" }).ExecuteAsync(context);
    }
    finally { context.RequestServices.GetRequiredService<OperationalMetrics>().Observe(context.Response.StatusCode); }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) ||
        HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var source) ||
            !string.Equals(source.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase) ||
            source.Scheme != context.Request.Scheme))
        {
            await Results.Problem(statusCode: 400, title: "Cross-origin mutations are not allowed.").ExecuteAsync(context);
            return;
        }
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            await Results.Problem(statusCode: 400, title: "Refresh the page and retry the request.",
                extensions: new Dictionary<string, object?> { ["code"] = "CSRF_INVALID" }).ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
app.MapAcademicEndpoints(demoEnabled);
app.MapPlanningEndpoints();
app.MapAdvisorEndpoints();
app.MapManagementEndpoints();
app.MapAdvisorWorkspaceEndpoints();

if (demoEnabled)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
    if (app.Environment.IsEnvironment("Testing") && !db.Database.IsNpgsql()) await db.Database.EnsureCreatedAsync();
    else await db.Database.MigrateAsync();
    var password = app.Configuration["Demo:Password"] ?? throw new InvalidOperationException("Set Demo__Password for synthetic account seeding.");
    await DemoSeeder.SeedAsync(db, password, scope.ServiceProvider.GetRequiredService<AuditEngine>());
}
app.Run();

public partial class Program;
