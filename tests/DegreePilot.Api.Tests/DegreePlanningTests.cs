using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DegreePilot.Api.Application;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DegreePilot.Api.Tests;

public class DegreePlanningTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Dataset(string name)
    {
        var alternate = Environment.GetEnvironmentVariable("DEGREE_BENCHMARK_DIR");
        if (!string.IsNullOrWhiteSpace(alternate)) return File.ReadAllText(Path.Combine(alternate, name));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DegreePilot.slnx"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "benchmarks", "degree_planning", name));
    }
    public sealed record Case(string Id, string Family, DegreeSolverRequest Request);
    private static Case[] Cases() => JsonSerializer.Deserialize<Case[]>(Dataset("cases.json"), Json)!;
    private static DegreeSolverReply AvaReply()
    {
        using var document = JsonDocument.Parse(Dataset("traces.json"));
        return document.RootElement.EnumerateArray().First(t => t.GetProperty("case_id").GetString() == "student-ava-2027-SPRING-8-18" &&
            t.GetProperty("algorithm").GetString() == "cp_sat").GetProperty("result").Deserialize<DegreeSolverReply>(Json)!;
    }
    private static DegreeContext Context(DegreeSolverRequest p)
    {
        var courses = p.Courses.Select(c => new CourseDefinition
        {
            Code = c.Code,
            Title = c.Code,
            Credits = c.Credits,
            Category = c.Category,
            CatalogVersion = p.CatalogVersion,
            PrerequisiteGroups = c.PrerequisiteGroups,
            OfferedTerms = p.Terms.Where(t => t.Options.Any(o => o.CourseCode == c.Code))
                .Select(t => t.Code.EndsWith("FALL", StringComparison.Ordinal) ? "Fall" : "Spring").Distinct().ToArray()
        }).ToArray();
        var program = new ProgramDefinition
        {
            Institution = "Synthetic benchmark",
            CatalogVersion = p.CatalogVersion,
            ProgramId = "TEST",
            ProgramName = "Synthetic degree",
            EffectiveYear = 2026,
            MinTotalCredits = p.MinTotalCredits,
            CoreCourseCodes = p.RequiredCore,
            MinCsElectiveCredits = p.CategoryMinimums["cs_elective"],
            MinGenEdCredits = p.CategoryMinimums["gen_ed"],
            MinFreeElectiveCredits = p.CategoryMinimums["free_elective"],
            MinPassingGrade = "C",
            GradePointScale = CatalogValidator.GradePoints.ToDictionary(),
            GradePolicyDocument = "SYNTHETIC",
            PassCreditCategories = ["cs_elective", "gen_ed", "free_elective"]
        };
        var attempts = p.Earned.Select(c => new Attempt(c, "2026-FALL", p.Passed.Contains(c) ? "C" : "P")).ToArray();
        var contexts = p.Terms.Select(t =>
        {
            var year = int.Parse(t.Code[..4], System.Globalization.CultureInfo.InvariantCulture);
            var fall = t.Code.EndsWith("FALL", StringComparison.Ordinal);
            var term = new TermDefinition(t.Code, t.Code, fall ? "Fall" : "Spring", new(year, fall ? 8 : 1, 15), new(year, fall ? 12 : 5, 15), "America/New_York", t.Published);
            string Time(int? minute) => $"{minute / 60:00}:{minute % 60:00}";
            return new PlanningContext(p.CatalogVersion, program, courses, attempts, term, t.Published ? t.Options.Select(o => new SectionDefinition(
                o.Id, p.CatalogVersion, t.Code, o.CourseCode, o.Id, o.Days, Time(o.StartMinute), Time(o.EndMinute), "America/New_York", 1, 1, "synthetic")).ToArray() : []);
        }).ToArray();
        return new(p.CatalogVersion, program, courses, attempts, contexts);
    }

    [Fact]
    public void EveryFeasibleBenchmarkTracePassesIndependentAspNetAcademicRules()
    {
        var clock = new DemoTestClock();
        var audit = new AuditEngine(clock);
        var validator = new DegreePlanValidator(audit, new(new(audit), clock));
        var cases = Cases().ToDictionary(c => c.Id);
        using var traces = JsonDocument.Parse(Dataset("traces.json"));
        using var manifest = JsonDocument.Parse(Dataset("manifest.json"));
        Assert.Equal(cases.Count * manifest.RootElement.GetProperty("repetitions").GetInt32() * 2, traces.RootElement.GetArrayLength());
        var checkedCount = 0;
        foreach (var trace in traces.RootElement.EnumerateArray())
        {
            var reply = trace.GetProperty("result").Deserialize<DegreeSolverReply>(Json)!;
            if (reply.Status != "FEASIBLE") { Assert.Empty(reply.Selections); continue; }
            var p = cases[trace.GetProperty("case_id").GetString()!].Request;
            var result = validator.Validate(Context(p), new(p.Terms[0].Code, p.Terms.Length, p.MinCredits, p.MaxCredits), reply, "benchmark");
            Assert.True(result.Status == "FEASIBLE", string.Join(';', result.Issues.Select(i => i.Code + ": " + i.Message)));
            checkedCount++;
        }
        var summary = manifest.RootElement.GetProperty("summary");
        Assert.Equal(summary.GetProperty("cp_sat").GetProperty("feasible").GetInt32() +
            summary.GetProperty("greedy").GetProperty("feasible").GetInt32(), checkedCount);
    }

    private sealed class StubDegree(DegreeSolverReply reply) : IDegreePlannerClient
    {
        public DegreeSolverRequest? Request { get; private set; }
        public Func<Task>? DuringSolve { get; set; }
        public async Task<DegreeSolverReply> SolveAsync(DegreeSolverRequest request, CancellationToken ct)
        {
            Request = request;
            if (DuringSolve is not null) await DuringSolve();
            return reply;
        }
    }
    private static async Task SignIn(HttpClient client, string account)
    {
        (await AuthorizationTests.Login(client, account)).EnsureSuccessStatusCode();
        await AuthorizationTests.SetCsrf(client);
    }
    [Fact]
    public async Task RoadmapIsOwnedMinimizedAuditedAndDoesNotAwardProjectedCredits()
    {
        var stub = new StubDegree(AvaReply());
        await using var factory = new AcademicApiFactory(degreePlanner: stub);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/degree-plans/generate", new DegreePlanRequest("2027-SPRING"))).StatusCode);
        await SignIn(client, "student-ava");
        var result = await (await client.PostAsJsonAsync("/api/degree-plans/generate", new DegreePlanRequest("2027-SPRING"))).Content.ReadFromJsonAsync<DegreePlanResult>();
        Assert.Equal("FEASIBLE", result!.Status);
        Assert.True(result.ConditionalDegreeComplete);
        Assert.Contains(result.Terms, t => t.Tentative);
        Assert.Equal(64, result.InputFingerprint.Length);
        var serialized = JsonSerializer.Serialize(stub.Request);
        foreach (var forbidden in new[] { "student-ava", "Ava", "@", "Transcript", "Grade" }) Assert.DoesNotContain(forbidden, serialized);
        Assert.Equal(35, (await client.GetFromJsonAsync<DegreeAudit>("/api/students/me/degree-audit"))!.EarnedCredits);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Equal("degree-plan.generate", Assert.Single(await db.MutationAudits.ToArrayAsync()).Action);
        Assert.Equal(13, await db.Attempts.CountAsync());
        using var noah = factory.CreateClient();
        await SignIn(noah, "student-noah");
        var invalid = await (await noah.PostAsJsonAsync("/api/degree-plans/generate?studentId=student-ava", new DegreePlanRequest("2027-SPRING"))).Content.ReadFromJsonAsync<DegreePlanResult>();
        Assert.Equal("ERROR", invalid!.Status); // Ava's actual sections cannot satisfy Noah's prerequisites.
        Assert.DoesNotContain("CS102", stub.Request!.Passed);
    }

    [Theory]
    [InlineData("INFEASIBLE")]
    [InlineData("TIMEOUT")]
    [InlineData("UNAVAILABLE")]
    public async Task FailureStatesArePreservedWithoutSelections(string status)
    {
        await using var factory = new AcademicApiFactory(degreePlanner: new StubDegree(new(status, [])));
        using var client = factory.CreateClient();
        await SignIn(client, "student-ava");
        var reply = await (await client.PostAsJsonAsync("/api/degree-plans/generate", new DegreePlanRequest("2027-SPRING"))).Content.ReadFromJsonAsync<DegreePlanResult>();
        Assert.Equal(status, reply!.Status);
        Assert.Empty(reply.Terms);
    }

    [Fact]
    public async Task RoleCsrfUnknownFieldsLimitsAndStaleOfferingsFailClosed()
    {
        var stub = new StubDegree(AvaReply());
        await using var factory = new AcademicApiFactory(degreePlanner: stub);
        using var client = factory.CreateClient();
        foreach (var role in new[] { "advisor-morgan", "admin-robin" })
        {
            await SignIn(client, role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/degree-plans/generate", new DegreePlanRequest("2027-SPRING"))).StatusCode);
        }
        await SignIn(client, "student-ava");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/degree-plans/generate", new { startTermCode = "2027-SPRING", studentId = "student-noah" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/degree-plans/generate", new DegreePlanRequest("2027-SPRING", 13))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/degree-plans/generate", new DegreePlanRequest("2027-SPRING"))).StatusCode);
        await AuthorizationTests.SetCsrf(client);
        stub.DuringSolve = async () =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            (await db.Sections.FirstAsync()).AvailableSeats = 0;
            await db.SaveChangesAsync();
        };
        var changed = await (await client.PostAsJsonAsync("/api/degree-plans/generate", new DegreePlanRequest("2027-SPRING"))).Content.ReadFromJsonAsync<DegreePlanResult>();
        Assert.Contains(changed!.Issues, i => i.Code == "PLANNING_DATA_CHANGED");
        Assert.Empty(changed.Terms);
    }
}
