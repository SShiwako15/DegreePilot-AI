using System.Security.Claims;
using DegreePilot.Api.Application;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using DegreePilot.Api.Transport;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var localEnvironment = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");

builder.Services.AddDbContext<AcademicDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Database") ?? "Host=localhost;Database=degreepilot"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AuditEngine>();
builder.Services.AddScoped<AcademicQueries>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddProblemDetails();
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
builder.Services.AddAuthorization(options => options.AddPolicy("Student", policy => policy.RequireRole("student")));
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
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception)
    {
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.Headers.CacheControl = "no-store";
        await Results.Problem(statusCode: 500, title: "The request could not be completed.",
            extensions: new Dictionary<string, object?> { ["code"] = "SERVICE_ERROR" }).ExecuteAsync(context);
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) ||
        HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
    {
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
