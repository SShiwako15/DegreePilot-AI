using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;

namespace DegreePilot.Api.Tests;

public class AuditTests
{
    private readonly ProgramDefinition program = FixtureReader.Read<ProgramDefinition>("program.json");
    private readonly CourseDefinition[] courses = FixtureReader.Read<CourseDefinition[]>("courses.json");
    private readonly UserFixture[] users = FixtureReader.Read<UserFixture[]>("students.json");
    private readonly AuditEngine engine = new(TimeProvider.System);

    private DegreeAudit Audit(params Attempt[] attempts) => engine.Calculate(program, courses, attempts, program.CatalogVersion);

    [Fact]
    public void AvaHas35CreditsAndSevenMissingCoreCourses()
    {
        var result = Audit(users.Single(x => x.Id == "student-ava").Transcript);
        Assert.Equal("valid", result.Status);
        Assert.Equal(35, result.EarnedCredits);
        Assert.Equal(120, result.RequiredCredits);
        Assert.Equal(3.37m, result.Gpa);
        Assert.False(result.DegreeComplete);
        Assert.Equal(["CS210", "CS220", "CS301", "CS310", "CS330", "CS401", "STAT201"],
            result.CoreRequirements.Where(x => !x.Complete).Select(x => x.Code));
        Assert.Equal([23, 0, 12, 0], result.Buckets.Select(x => x.Earned));
        Assert.Equal([44, 12, 36, 28], result.Buckets.Select(x => x.Required));
    }

    [Fact]
    public void NoahEarnsSevenAndDDoesNotSatisfyCore()
    {
        var result = Audit(users.Single(x => x.Id == "student-noah").Transcript);
        Assert.Equal(7, result.EarnedCredits);
        Assert.Equal(2.64m, result.Gpa);
        Assert.False(result.CoreRequirements.Single(x => x.Code == "CS102").Complete);
    }

    [Fact]
    public void RepeatsUseLatestPassingTermRegardlessOfInputOrderOrLaterFailure()
    {
        var result = Audit(new("CS101", "2026-FALL", "F"), new("CS101", "2025-FALL", "D"),
            new("CS101", "2026-SPRING", "C"), new("CS101", "2025-SPRING", "A"));
        Assert.Equal(4, result.EarnedCredits);
        Assert.Equal("C", Assert.Single(result.EarnedCourses).Grade);
        Assert.Equal("2026-SPRING", result.CoreRequirements.Single(x => x.Code == "CS101").Term);
        Assert.Equal(1.75m, result.Gpa);
    }

    [Theory]
    [InlineData("D")]
    [InlineData("F")]
    [InlineData("W")]
    [InlineData("I")]
    [InlineData("P")]
    public void NonPassingGradesEarnNoCredit(string grade)
    {
        var result = Audit(new Attempt("CS101", "2026-FALL", grade));
        Assert.Equal(0, result.EarnedCredits);
        Assert.False(result.CoreRequirements.Single(x => x.Code == "CS101").Complete);
        if (grade is "W" or "I" or "P") Assert.Null(result.Gpa);
    }

    [Fact]
    public void PassGradeRequiresExplicitElectiveApproval()
    {
        Attempt[] attempts = [new("CS350", "2026-FALL", "P")];
        Assert.Equal(0, Audit(attempts).EarnedCredits);
        var result = engine.Calculate(program with { PassCreditCategories = ["cs_elective"] }, courses, attempts, program.CatalogVersion);
        Assert.Equal(3, result.EarnedCredits);
        Assert.Null(result.Gpa);
    }

    [Fact]
    public void AllRequirementsAreNeededEvenWhenTotalIsMet()
    {
        var all = courses.Select(c => new Attempt(c.Code, "2026-FALL", "A")).ToArray();
        var complete = Audit(all);
        Assert.Equal(120, complete.EarnedCredits);
        Assert.True(complete.DegreeComplete);
        Assert.Equal(complete.EarnedCredits, complete.Buckets.Sum(x => x.Earned));
        var extraCreditCourse = courses.Select(c => c.Code == "ELEC001" ? c with { Credits = 7 } : c).ToArray();
        var missingCore = engine.Calculate(program, extraCreditCourse, all.Where(a => a.CourseCode != "CS401").ToArray(), program.CatalogVersion);
        Assert.Equal(120, missingCore.EarnedCredits);
        Assert.False(missingCore.DegreeComplete);
    }

    [Theory]
    [InlineData("UNKNOWN", "2026-FALL", "A")]
    [InlineData("CS101", "Fall 2026", "A")]
    [InlineData("CS101", "2026-FALL", "A+")]
    [InlineData("CS101", "2026-FALL\n", "A")]
    [InlineData("CS101", "٢٠٢٦-FALL", "A")]
    public void InvalidTranscriptNeverProducesGuessedCredits(string code, string term, string grade)
    {
        var result = Audit(new Attempt(code, term, grade));
        Assert.Equal("invalid", result.Status);
        Assert.Null(result.EarnedCredits);
        Assert.Null(result.DegreeComplete);
        Assert.Contains(result.Issues, x => x.Code == "TRANSCRIPT_INVALID");
    }

    [Fact]
    public void DuplicateAttemptIsAnError()
    {
        var attempt = new Attempt("CS101", "2026-FALL", "A");
        Assert.Equal("invalid", Audit(attempt, attempt).Status);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("category")]
    [InlineData("cycle")]
    [InlineData("reference")]
    [InlineData("version")]
    [InlineData("duplicate")]
    public void CatalogValidationFailsClosed(string mutation)
    {
        var invalid = mutation switch
        {
            "missing" => courses.Where(x => x.Code != "CS401").ToArray(),
            "category" => courses.Select(x => x.Code == "CS101" ? x with { Category = "cs_elective" } : x).ToArray(),
            "cycle" => courses.Select(x => x.Code == "CS101" ? x with { PrerequisiteGroups = [["CS102"]] } : x).ToArray(),
            "reference" => courses.Select(x => x.Code == "CS101" ? x with { PrerequisiteGroups = [["MISSING"]] } : x).ToArray(),
            "version" => courses.Select(x => x with { CatalogVersion = "newer" }).ToArray(),
            _ => [.. courses, courses[0]]
        };
        var result = engine.Calculate(program, invalid, [], program.CatalogVersion);
        Assert.Equal("invalid", result.Status);
        Assert.Null(result.EarnedCredits);
        Assert.Contains(result.Issues, x => x.Code == "CATALOG_INVALID");
    }

    [Fact]
    public void MissingCatalogAndUnsupportedPolicyAreErrors()
    {
        Assert.Equal("invalid", engine.Calculate(null, courses, [], program.CatalogVersion).Status);
        Assert.Equal("invalid", engine.Calculate(program, courses, [], "NEW-CATALOG").Status);
        Assert.Equal("invalid", engine.Calculate(program with { MinPassingGrade = "D" }, courses, [], program.CatalogVersion).Status);
    }

    [Fact]
    public void NullRequirementCodesAreReportedWithoutCrashing()
    {
        var nullCore = program with { CoreCourseCodes = [null!] };
        Assert.Equal("CATALOG_INVALID", Assert.Single(engine.Calculate(nullCore, courses, [], program.CatalogVersion).Issues).Code);
        var nullPrerequisite = courses.Select(c => c.Code == "CS101" ? c with { PrerequisiteGroups = [[null!]] } : c).ToArray();
        Assert.Equal("CATALOG_INVALID", Assert.Single(engine.Calculate(program, nullPrerequisite, [], program.CatalogVersion).Issues).Code);
    }
}
