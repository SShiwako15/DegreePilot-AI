using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DegreePilot.Api.Application;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DegreePilot.Api.Tests;

public class HardeningTests
{
    private sealed class ChangingPlanner : IPlannerClient
    {
        public Func<Task>? DuringSolve { get; set; }
        public async Task<SolverResult> SolveAsync(SolverRequest request, CancellationToken ct)
        {
            if (DuringSolve is not null) await DuringSolve();
            return new("FEASIBLE", ["2027-SPRING-CS301-2"]);
        }
    }
    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static async Task SignIn(HttpClient client, string user = "student-ava")
    {
        (await AuthorizationTests.Login(client, user)).EnsureSuccessStatusCode();
        await AuthorizationTests.SetCsrf(client);
    }

    [Fact]
    public async Task SolverProposalUsesFreshDataAfterSectionClosesDuringSolve()
    {
        var planner = new ChangingPlanner();
        await using var factory = new AcademicApiFactory(planner: planner);
        planner.DuringSolve = async () =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            (await db.Sections.SingleAsync(s => s.Id == "2027-SPRING-CS301-2")).AvailableSeats = 0;
            await db.SaveChangesAsync();
        };
        using var client = factory.CreateClient();
        await SignIn(client);
        var result = await (await client.PostAsJsonAsync("/api/plans/generate", new PlanRequest("2027-SPRING", 3, 3))).Content.ReadFromJsonAsync<PlanResult>();
        Assert.Equal("ERROR", result!.Status);
        Assert.Empty(result.Selections);
        Assert.Contains(result.Issues, i => i.Code == "SECTION_FULL");
    }

    [Fact]
    public async Task HistoricalSavedPlansRetainChoicesWhileNewSavesStillRequireFutureTerms()
    {
        var clock = new MutableClock();
        await using var factory = new AcademicApiFactory(clock: clock);
        using var client = factory.CreateClient();
        await SignIn(client);
        var selection = new SelectionRequest(new("2027-SPRING", 3, 3), ["2027-SPRING-CS301-2"]);
        var saved = await (await client.PostAsJsonAsync("/api/plans/save", selection)).Content.ReadFromJsonAsync<PlanResult>();
        clock.Now = new(2027, 2, 1, 12, 0, 0, TimeSpan.Zero);
        await SignIn(client); // Advancing the clock correctly expires the old cookie too.
        var historical = (await client.GetFromJsonAsync<PlanResult>($"/api/plans/{saved!.Id}"))!;
        Assert.Equal("FEASIBLE", historical.Status);
        Assert.Equal(3, historical.TotalCredits);
        Assert.Equal("2027-SPRING-CS301-2", Assert.Single(historical.Selections).Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/plans/save", selection)).StatusCode);
    }

    [Fact]
    public async Task LoginAndSolverRateLimitsAreBoundedWithRetryAfterAndDoNotDisableAudit()
    {
        await using var factory = new AcademicApiFactory(planner: new StubPlanner("UNAVAILABLE", []), loginLimit: 1, plannerLimit: 1);
        using var client = factory.CreateClient();
        await SignIn(client);
        var login = await AuthorizationTests.Login(client, "student-ava");
        Assert.Equal(HttpStatusCode.TooManyRequests, login.StatusCode);
        Assert.Equal("60", Assert.Single(login.Headers.GetValues("Retry-After")));
        (await client.PostAsJsonAsync("/api/plans/generate", new PlanRequest("2027-SPRING", 3, 3))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/plans/generate", new PlanRequest("2027-SPRING", 3, 3))).StatusCode);
        Assert.Equal(35, (await client.GetFromJsonAsync<DegreeAudit>("/api/students/me/degree-audit"))!.EarnedCredits);
    }

    [Fact]
    public async Task AdminCommandsNeedCsrfAndSameOriginAndHttpsCookiesAreSecure()
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var login = await AuthorizationTests.Login(client, "admin-robin");
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("degreepilot.session=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        var command = new AssignmentRequest("advisor-morgan", "student-ava", false, 1);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/assignments", command)).StatusCode);
        await AuthorizationTests.SetCsrf(client);
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.invalid");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/assignments", command)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        (await client.PostAsJsonAsync("/api/admin/assignments", command)).EnsureSuccessStatusCode();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/admin/assignments");
        preflight.Headers.Add("Origin", "https://untrusted.invalid");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        Assert.False((await client.SendAsync(preflight)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task RoleChangeInvalidatesAnExistingSession()
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        await SignIn(client, "admin-robin");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (await db.Users.SingleAsync(u => u.Id == "admin-robin")).Role = "advisor";
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/audit")).StatusCode);
    }

    [Fact]
    public async Task GenericFailuresAndMalformedBodiesDoNotExposePayloadOrLogSecrets()
    {
        const string sentinel = "sk-SYNTHETIC-SECRET-NOT-A-REAL-KEY-000000000";
        var planner = new ChangingPlanner { DuringSolve = () => throw new InvalidOperationException(sentinel) };
        using var capture = new LogCapture();
        await using var factory = new AcademicApiFactory(planner: planner, logs: capture);
        using var client = factory.CreateClient();
        await SignIn(client);
        var failure = await client.PostAsJsonAsync("/api/plans/generate", new PlanRequest("2027-SPRING", 3, 3));
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.DoesNotContain(sentinel, await failure.Content.ReadAsStringAsync());
        Assert.True(failure.Headers.CacheControl?.NoStore);
        Assert.True(failure.Headers.Contains("X-Request-ID"));
        var malformed = await client.PostAsync("/api/plans/save", new StringContent("{\"secret\":\"" + sentinel + "\"}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.DoesNotContain(sentinel, await malformed.Content.ReadAsStringAsync());
        Assert.DoesNotContain(sentinel, string.Join('\n', capture.Messages));
        Assert.DoesNotContain(AcademicApiFactory.Password, string.Join('\n', capture.Messages));
        Assert.NotEmpty(capture.Messages); // Active capture, rather than logging globally disabled.
        using var admin = factory.CreateClient();
        await SignIn(admin, "admin-robin");
        var health = await admin.GetFromJsonAsync<JsonElement>("/api/admin/operations");
        Assert.Equal(1, health.GetProperty("serverErrors").GetInt32());
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
        public void Dispose() { }
        private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(formatter(state, exception) + exception?.ToString());
        }
    }
}
