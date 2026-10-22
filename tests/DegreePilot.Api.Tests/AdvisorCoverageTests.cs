using System.Net.Http.Json;
using DegreePilot.Api.Application;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DegreePilot.Api.Tests;

public class AdvisorCoverageTests
{
    private static async Task SignIn(HttpClient client, string actor = "student-ava")
    {
        (await AuthorizationTests.Login(client, actor)).EnsureSuccessStatusCode();
        await AuthorizationTests.SetCsrf(client);
    }

    private static async Task<ChatResult> Ask(HttpClient client, string question, string[]? previous = null)
    {
        var response = await client.PostAsJsonAsync("/api/advisor/chat", new ChatRequest(question,
            TermCode: "2027-SPRING", PreviousQuestions: previous));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChatResult>())!;
    }

    [Fact]
    public async Task EveryCatalogCourseCanBeExplainedByCodeAndFullTitle()
    {
        var provider = new StubAdvisor();
        await using var factory = new AcademicApiFactory(advisor: provider);
        using var client = factory.CreateClient();
        await SignIn(client);
        var courses = FixtureReader.Read<CourseDefinition[]>("courses.json");
        foreach (var course in courses)
        {
            foreach (var name in new[] { course.Code, course.Title })
            {
                var reply = await Ask(client, $"What must I complete before {name}?");
                Assert.True(reply.Status == "ANSWERED", $"{name}: {reply.Status}");
                Assert.Contains(course.Code, reply.Answer);
                Assert.NotEmpty(reply.Citations);
                foreach (var group in course.PrerequisiteGroups)
                    Assert.Contains("(" + string.Join(" OR ", group) + ")", reply.Answer);
                if (course.PrerequisiteGroups.Length == 0) Assert.Contains("no course prerequisites", reply.Answer);
            }
        }
        Assert.Equal(courses.Length * 2, provider.Requests.Count);
        Assert.All(provider.Requests, envelope =>
        {
            Assert.InRange(envelope.Facts.Length, 1, 12);
            Assert.All(envelope.Facts, fact => Assert.InRange(fact.Length, 1, 1000));
        });
    }

    [Theory]
    [InlineData("What do I have left to finish?", "85 more unique earned credits")]
    [InlineData("How is my degree progress?", "85 more unique earned credits")]
    [InlineData("Which courses do I still need to graduate?", "Required core courses still to complete")]
    [InlineData("What are the prerequisites for CS 350?", "CS350 is eligible")]
    [InlineData("What classes should I take before Data Structures?", "CS102")]
    [InlineData("What are prerequisites?", "Every group must be satisfied")]
    [InlineData("How do I plan my next semester?", "12")]
    [InlineData("Can classes clash at the same time?", "Adjacent times do not count")]
    [InlineData("How many credits should I take?", "12")]
    [InlineData("What is my GPA?", "3.37")]
    public async Task NaturalQuestionsHaveGroundedAnswers(string question, string expected)
    {
        await using var factory = new AcademicApiFactory(advisor: new StubAdvisor());
        using var client = factory.CreateClient();
        await SignIn(client);
        var reply = await Ask(client, question);
        Assert.Equal("ANSWERED", reply.Status);
        Assert.Contains(expected, reply.Answer);
        Assert.NotEmpty(reply.Citations);
    }

    [Fact]
    public async Task MultipleCoursesKeepAndOrGroupsAndFollowupsFindTheirCourse()
    {
        await using var factory = new AcademicApiFactory(advisor: new StubAdvisor());
        using var client = factory.CreateClient();
        await SignIn(client);
        var reply = await Ask(client, "What are the prerequisites for CS301 and CS401?");
        Assert.Equal("ANSWERED", reply.Status);
        Assert.Contains("(CS201) AND (MATH250 OR STAT201)", reply.Answer);
        Assert.Contains("(CS301) AND (CS310)", reply.Answer);
        var followup = await Ask(client, "Why?", ["What do I need before CS350?", "Why?"]);
        Assert.Equal("ANSWERED", followup.Status);
        Assert.Contains("CS350", followup.Answer);
    }

    [Fact]
    public async Task CourseSuggestionsContainOnlyEligibleOfferingsAndRemainTentativeWhenUnpublished()
    {
        await using var factory = new AcademicApiFactory(advisor: new StubAdvisor());
        using var client = factory.CreateClient();
        await SignIn(client);
        var reply = await Ask(client, "What classes can I take next semester?");
        Assert.Equal("ANSWERED", reply.Status);
        var choices = string.Join(" ", reply.Facts.Where(f => f.StartsWith("Eligible courses:", StringComparison.Ordinal)));
        Assert.Contains("CS301", choices);
        Assert.DoesNotContain("CS401", choices);
        Assert.DoesNotContain("CS101", choices);
        Assert.Contains("combined timetable must still be checked", reply.Answer);
        var future = await (await client.PostAsJsonAsync("/api/advisor/chat",
            new ChatRequest("Which courses can I take?", TermCode: "2028-SPRING"))).Content.ReadFromJsonAsync<ChatResult>();
        Assert.Equal("ANSWERED", future!.Status);
        Assert.Contains("tentative", future.Answer);
    }

    [Fact]
    public async Task OptionsChangedDuringResponseAreRejected()
    {
        var provider = new StubAdvisor();
        await using var factory = new AcademicApiFactory(advisor: provider);
        using var client = factory.CreateClient();
        await SignIn(client);
        provider.DuringAnswer = async () =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            var section = await db.Sections.SingleAsync(s => s.Id == "2027-SPRING-CS350-1");
            section.AvailableSeats = 0;
            await db.SaveChangesAsync();
        };
        var reply = await Ask(client, "Which classes can I take?");
        Assert.Equal("ABSTAIN", reply.Status);
        Assert.Contains("changed", reply.Answer);
    }

    [Fact]
    public async Task RemainingProgressUsesOnlyTheSignedInStudent()
    {
        await using var factory = new AcademicApiFactory(advisor: new StubAdvisor());
        using var client = factory.CreateClient();
        await SignIn(client, "student-noah");
        var reply = await Ask(client, "How many credits remain for my degree?");
        Assert.Equal("ANSWERED", reply.Status);
        Assert.Contains("113 more unique earned credits", reply.Answer);
        Assert.DoesNotContain("85 more", reply.Answer);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeedUpgradeRetiresOnlyCopiesWhoseAcademicPassagesArePreserved(bool differentText)
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var source = await db.PolicyDocuments.SingleAsync(d => d.Id.Contains("demo_catalog") && d.Status == "approved");
        var passages = await db.PolicySections.Where(s => s.DocumentId == source.Id && s.Id != "CAT-PREREQUISITES").ToArrayAsync();
        var prior = new PolicyDocument
        {
            Id = "prior-admin-copy",
            CatalogVersion = source.CatalogVersion,
            Title = "Previous university name",
            Version = 2,
            Status = "approved",
            SourceHash = "reviewed-copy"
        };
        var copies = passages.Select(p => new PolicySection
        {
            DocumentId = prior.Id,
            Id = p.Id,
            Title = p.Title,
            Text = differentText && p.Id == "CAT-PREREQ-CS401" ? "An independently edited capstone policy." : p.Text
        }).ToArray();
        prior.ApprovalHash = PolicySources.DocumentFingerprint(prior, copies);
        db.PolicyDocuments.Add(prior);
        db.PolicySections.AddRange(copies);
        await db.SaveChangesAsync();

        await new PolicySources().SeedAsync(db);

        Assert.Equal(differentText ? "approved" : "retired", prior.Status);
        Assert.Equal(copies.Length, await db.PolicySections.CountAsync(p => p.DocumentId == prior.Id));
        Assert.Equal("approved", source.Status);
        var evidence = await scope.ServiceProvider.GetRequiredService<PolicyRetrieval>().ReadAsync(source.CatalogVersion);
        if (differentText) Assert.Empty(evidence);
        else Assert.Equal(11, evidence.Length);
    }

}
