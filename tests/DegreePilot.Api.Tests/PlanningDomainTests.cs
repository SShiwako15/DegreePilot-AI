using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;

namespace DegreePilot.Api.Tests;

public sealed class DemoTestClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
}

public class PlanningDomainTests
{
    internal static readonly TimeProvider Clock = new DemoTestClock();
    internal static readonly EligibilityEngine Engine = new(new AuditEngine(Clock));
    internal static readonly PlanValidator Validator = new(Engine, Clock);
    internal static PlanningContext Context(string student = "student-ava", string term = "2027-SPRING")
    {
        var program = FixtureReader.Read<ProgramDefinition>("program.json");
        return new(program.CatalogVersion, program, FixtureReader.Read<CourseDefinition[]>("courses.json"),
            FixtureReader.Read<UserFixture[]>("students.json").Single(u => u.Id == student).Transcript,
            FixtureReader.Read<TermDefinition[]>("terms.json").Single(t => t.Code == term),
            FixtureReader.Read<SectionFixture[]>("sections.json").Where(s => s.TermCode == term)
                .Select(s => new SectionDefinition(s.Id, program.CatalogVersion, s.TermCode, s.CourseCode,
                    s.SectionCode, s.Days, s.StartLocal, s.EndLocal, s.Timezone, s.Capacity, s.AvailableSeats, s.Delivery)).ToArray());
    }

    [Fact]
    public void AvaAlgorithmsEligibleAndCapstoneBlockedAndCompletedCoursesNotRecommended()
    {
        var context = Context();
        Assert.True(Engine.Evaluate(context, "CS301").Eligible);
        var capstone = Engine.Evaluate(context, "CS401");
        Assert.False(capstone.Eligible);
        Assert.Equal(["CS301", "CS310"], capstone.MissingPrerequisiteGroups.SelectMany(g => g));
        Assert.Equal("completed", Engine.Evaluate(context, "CS102").Status);
        Assert.Contains(Engine.Evaluate(context, "CS210").Reasons, i => i.Code == "NOT_OFFERED");
    }

    [Fact]
    public void AndOfOrGroupsRequirePriorTermCPlus()
    {
        var context = Context() with { Attempts = [new("CS201", "2026-FALL", "C"), new("STAT201", "2026-FALL", "B")] };
        Assert.True(Engine.Evaluate(context, "CS301").Eligible);
        Assert.False(Engine.Evaluate(context with { Attempts = [new("STAT201", "2026-FALL", "A")] }, "CS301").Eligible);
        Assert.False(Engine.Evaluate(context with { Attempts = [new("CS201", "2027-SPRING", "A"), new("MATH250", "2026-FALL", "A")] }, "CS301").Eligible);
        var noah = Engine.Evaluate(Context("student-noah"), "CS201");
        Assert.False(noah.Eligible);
        Assert.Equal("blocked", noah.Status);
        Assert.Contains(noah.Reasons, i => i.Code == "MISSING_PREREQUISITE" && i.Message.Contains("CS102"));
    }

    [Theory]
    [InlineData("Tue", "10:15", "11:30", false)]
    [InlineData("Tue", "10:00", "11:15", true)]
    [InlineData("Wed", "09:00", "10:15", false)]
    public void MeetingOverlapIsHalfOpenAndDaySpecific(string day, string start, string end, bool overlaps)
    {
        var first = Context().Sections[0] with { Days = ["Tue"], StartLocal = "09:00", EndLocal = "10:15" };
        Assert.Equal(overlaps, SchedulingRules.Conflicts(first, first with { Days = [day], StartLocal = start, EndLocal = end }));
    }

    [Theory]
    [InlineData("2027-SPRING-CS220-1", "2027-SPRING-CS301-1", "TIME_CONFLICT")]
    [InlineData("2027-SPRING-CS301-1", "2027-SPRING-CS301-2", "DUPLICATE_COURSE")]
    [InlineData("2027-SPRING-CS401-1", "2027-SPRING-CS310-1", "MISSING_PREREQUISITE")]
    [InlineData("unknown", "2027-SPRING-CS310-1", "SECTION_INVALID")]
    public void ValidatorRejectsInvalidSelections(string a, string b, string reason)
    {
        var result = Validator.Validate(Context(), new("2027-SPRING", 6, 6), [a, b]);
        Assert.Equal("INVALID", result.Status);
        Assert.Contains(result.Issues, i => i.Code == reason);
    }

    [Fact]
    public void ValidatorChecksSeatsCreditsAndTentativePatterns()
    {
        var context = Context();
        var first = "2027-SPRING-CS301-2";
        Assert.Equal("FEASIBLE", Validator.Validate(context, new("2027-SPRING", 3, 3), [first]).Status);
        Assert.Contains(Validator.Validate(context, new("2027-SPRING"), [first]).Issues, i => i.Code == "CREDIT_RANGE");
        var full = context with { Sections = context.Sections.Select(s => s.Id == first ? s with { AvailableSeats = 0 } : s).ToArray() };
        Assert.Contains(Validator.Validate(full, new("2027-SPRING", 3, 3), [first]).Issues, i => i.Code == "SECTION_FULL");
        var future = Context(term: "2028-SPRING");
        var projected = Validator.Validate(future, new("2028-SPRING", 3, 3), ["tentative:CS301"]);
        Assert.Equal("FEASIBLE", projected.Status);
        Assert.True(projected.Tentative);
        Assert.Null(Assert.Single(projected.Selections).StartLocal);
        Assert.Contains(Engine.Evaluate(future, "CS210").Reasons, i => i.Code == "NOT_OFFERED");
    }

    [Fact]
    public void FullPublishedSectionsBlockEligibilityWithoutClaimingTheCourseIsNotOffered()
    {
        var context = Context();
        var full = context with
        {
            Sections = context.Sections.Select(s => s.CourseCode == "CS301"
                ? s with { AvailableSeats = 0 } : s).ToArray()
        };
        var result = Engine.Evaluate(full, "CS301");
        Assert.Equal("blocked", result.Status);
        Assert.False(result.Eligible);
        Assert.Contains(result.Reasons, issue => issue.Code == "SECTION_FULL");
        Assert.DoesNotContain(result.Reasons, issue => issue.Code == "NOT_OFFERED");
        Assert.DoesNotContain(Engine.Options(full), option => option.CourseCode == "CS301");
    }

    [Fact]
    public void CorruptCatalogSectionAndTermFailClosed()
    {
        var context = Context();
        Assert.Contains(Engine.Evaluate(context with { Program = null }, "CS301").Reasons, i => i.Code == "CATALOG_INVALID");
        var bad = context with { Sections = [context.Sections[0] with { CatalogVersion = "wrong", StartLocal = "nonsense" }] };
        Assert.Equal("invalid", Engine.Evaluate(bad, "CS301").Status);
        Assert.Equal("INVALID", Validator.Validate(bad, new("2027-SPRING"), ["unknown"]).Status);
        Assert.NotEmpty(SchedulingRules.ValidateRequest(new("bad", 18, 12)));
    }
}
