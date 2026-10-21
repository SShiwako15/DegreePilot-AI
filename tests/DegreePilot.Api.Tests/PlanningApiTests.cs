using System.Net;
using System.Net.Http.Json;
using DegreePilot.Api.Application;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DegreePilot.Api.Tests;

public sealed class StubPlanner(string status = "FEASIBLE", string[]? ids = null) : IPlannerClient
{
    public SolverRequest? LastRequest { get; private set; }
    public Task<SolverResult> SolveAsync(SolverRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new SolverResult(status, ids ?? ["2027-SPRING-CS301-2", "2027-SPRING-CS310-1", "2027-SPRING-CS350-1", "2027-SPRING-STAT201-1"]));
    }
}

public class PlanningApiTests
{
    private static readonly PlanRequest Spring = new("2027-SPRING", 12, 12);
    private static async Task SignIn(HttpClient client, string user = "student-ava")
    {
        (await AuthorizationTests.Login(client, user)).EnsureSuccessStatusCode();
        await AuthorizationTests.SetCsrf(client);
    }

    [Fact]
    public async Task EligibilitySectionsAndSearchUseActorAndPinnedVersion()
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/terms")).StatusCode);
        await SignIn(client);
        Assert.True((await client.GetFromJsonAsync<Eligibility>("/api/courses/CS301/eligibility?term=2027-SPRING"))!.Eligible);
        Assert.Equal("blocked", (await client.GetFromJsonAsync<Eligibility>("/api/courses/CS401/eligibility?term=2027-SPRING"))!.Status);
        var search = await client.GetFromJsonAsync<CourseEligibility[]>("/api/courses?term=2027-SPRING&search=algorithms&status=eligible");
        Assert.Equal("CS301", Assert.Single(search!).Course.Code);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/terms/2027-SPRING/sections")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/terms/unknown/sections")).StatusCode);
        using var noah = factory.CreateClient();
        await SignIn(noah, "student-noah");
        Assert.False((await noah.GetFromJsonAsync<Eligibility>("/api/courses/CS201/eligibility?term=2027-SPRING&studentId=student-ava"))!.Eligible);
    }

    [Fact]
    public async Task GeneratedPlanIsValidatedSavedRevalidatedAndIsolated()
    {
        var solver = new StubPlanner();
        await using var factory = new AcademicApiFactory(planner: solver);
        using var client = factory.CreateClient();
        await SignIn(client);
        var generated = await (await client.PostAsJsonAsync("/api/plans/generate", Spring)).Content.ReadFromJsonAsync<PlanResult>();
        Assert.Equal("FEASIBLE", generated!.Status);
        Assert.Equal(12, generated.TotalCredits);
        Assert.DoesNotContain(solver.LastRequest!.Options, o => o.CourseCode is "CS401" or "CS101");
        Assert.Equal(5, solver.LastRequest.MaxTimeSeconds);
        var selection = new SelectionRequest(Spring, generated.Selections.Select(s => s.Id).ToArray());
        var savedResponse = await client.PostAsJsonAsync("/api/plans/save", selection);
        savedResponse.EnsureSuccessStatusCode();
        var saved = (await savedResponse.Content.ReadFromJsonAsync<PlanResult>())!;
        Assert.NotNull(saved.Id);
        Assert.Single((await client.GetFromJsonAsync<PlanResult[]>("/api/students/me/plans"))!);
        using var noah = factory.CreateClient();
        await SignIn(noah, "student-noah");
        Assert.Empty((await noah.GetFromJsonAsync<PlanResult[]>("/api/students/me/plans?studentId=student-ava"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await noah.GetAsync($"/api/plans/{saved.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await noah.PostAsJsonAsync("/api/plans/save", selection)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (await db.Sections.SingleAsync(s => s.Id == "2027-SPRING-CS301-2")).AvailableSeats = 0;
        await db.SaveChangesAsync();
        Assert.Equal("INVALID", (await client.GetFromJsonAsync<PlanResult>($"/api/plans/{saved.Id}"))!.Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/plans/save", selection)).StatusCode);
    }

    [Theory]
    [InlineData("TIMEOUT")]
    [InlineData("INFEASIBLE")]
    [InlineData("UNAVAILABLE")]
    [InlineData("ERROR")]
    public async Task SolverFailuresNeverBecomeFeasiblePlans(string status)
    {
        await using var factory = new AcademicApiFactory(planner: new StubPlanner(status, []));
        using var client = factory.CreateClient();
        await SignIn(client);
        var result = await (await client.PostAsJsonAsync("/api/plans/generate", Spring)).Content.ReadFromJsonAsync<PlanResult>();
        Assert.Equal(status, result!.Status);
        Assert.Empty(result.Selections);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public async Task MaliciousSolverProposalAndTamperedSaveAreRejected()
    {
        await using var factory = new AcademicApiFactory(planner: new StubPlanner("FEASIBLE", ["2027-SPRING-CS301-1", "2027-SPRING-CS220-1"]));
        using var client = factory.CreateClient();
        await SignIn(client);
        var result = await (await client.PostAsJsonAsync("/api/plans/generate", Spring)).Content.ReadFromJsonAsync<PlanResult>();
        Assert.Equal("ERROR", result!.Status);
        Assert.Empty(result.Selections);
        Assert.Contains(result.Issues, i => i.Code == "SOLVER_RESULT_INVALID");
        var tampered = new SelectionRequest(new("2027-SPRING", 6, 6), ["2027-SPRING-CS301-1", "2027-SPRING-CS220-1"]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/plans/save", tampered)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/plans/save", new { constraints = Spring, selectionIds = new[] { "unknown" }, studentId = "student-noah" })).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<PlanResult[]>("/api/students/me/plans"))!);
    }

    [Fact]
    public async Task ManualTentativeSaveNeedsNoSolverAndIsNotATimetable()
    {
        await using var factory = new AcademicApiFactory(planner: new StubPlanner("UNAVAILABLE", []));
        using var client = factory.CreateClient();
        await SignIn(client);
        var selection = new SelectionRequest(new("2028-SPRING", 3, 3), ["tentative:CS301"]);
        var result = await (await client.PostAsJsonAsync("/api/plans/save", selection)).Content.ReadFromJsonAsync<PlanResult>();
        Assert.Equal("FEASIBLE", result!.Status);
        Assert.True(result.Tentative);
        Assert.Empty(Assert.Single(result.Selections).Days);
        Assert.Null(result.Selections[0].SectionCode);
        var repeated = selection with { SelectionIds = ["tentative:CS301", "tentative:CS301"] };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/plans/save", repeated)).StatusCode);
    }

    [Theory]
    [InlineData("advisor-morgan")]
    [InlineData("admin-robin")]
    public async Task OtherRolesCannotReadOrGenerateStudentPlans(string role)
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        await SignIn(client, role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/students/me/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/plans/generate", Spring)).StatusCode);
    }
}
