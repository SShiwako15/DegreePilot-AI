using System.Text.Json.Serialization;

namespace DegreePilot.Api.Domain;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgramDefinition
{
    public required string Institution { get; init; }
    public required string CatalogVersion { get; init; }
    public required string ProgramId { get; init; }
    public required string ProgramName { get; init; }
    public required int EffectiveYear { get; init; }
    public required int MinTotalCredits { get; init; }
    public required string[] CoreCourseCodes { get; init; }
    public required int MinCsElectiveCredits { get; init; }
    public required int MinGenEdCredits { get; init; }
    public required int MinFreeElectiveCredits { get; init; }
    public required string MinPassingGrade { get; init; }
    public required Dictionary<string, int> GradePointScale { get; init; }
    public required string GradePolicyDocument { get; init; }
    public string? Notes { get; init; }
    // The supplied policy approves no P-grade categories. Approval must be explicit.
    public string[] PassCreditCategories { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CourseDefinition
{
    public required string Code { get; init; }
    public required string Title { get; init; }
    public required int Credits { get; init; }
    public required string Category { get; init; }
    public required string[][] PrerequisiteGroups { get; init; }
    public required string[] OfferedTerms { get; init; }
    public required string CatalogVersion { get; init; }
}

public sealed record Attempt(string CourseCode, string Term, string Grade);
public sealed record RuleIssue(string Code, string Message);
public sealed record CreditBucket(string Id, string Label, int Earned, int Required)
{
    public int Remaining => Math.Max(0, Required - Earned);
    public bool Complete => Earned >= Required;
}
public sealed record CoreRequirement(string Code, string Title, int Credits, bool Complete, string? Grade, string? Term);
public sealed record EarnedCourse(string Code, int Credits, string Category, string Grade, string Term);
public sealed record DegreeAudit(
    string Status, string CatalogVersion, string RuleEngineVersion, DateTimeOffset GeneratedAt,
    int? EarnedCredits, int? RequiredCredits, decimal? Gpa, bool? DegreeComplete,
    IReadOnlyList<CreditBucket> Buckets, IReadOnlyList<CoreRequirement> CoreRequirements,
    IReadOnlyList<EarnedCourse> EarnedCourses, IReadOnlyList<RuleIssue> Issues);
