using System.Text.Json.Serialization;

namespace DegreePilot.Api.Domain;

public sealed record TermDefinition(string Code, string Name, string Kind, DateOnly StartsOn,
    DateOnly EndsOn, string Timezone, bool IsPublished);
public sealed record SectionDefinition(string Id, string CatalogVersion, string TermCode, string CourseCode,
    string SectionCode, string[] Days, string StartLocal, string EndLocal, string Timezone,
    int Capacity, int AvailableSeats, string Delivery);
public sealed record PlanningContext(string CatalogVersion, ProgramDefinition? Program,
    CourseDefinition[] Courses, Attempt[] Attempts, TermDefinition Term, SectionDefinition[] Sections);
public sealed record Eligibility(string CourseCode, string Status, bool Eligible, bool Tentative,
    string[][] MissingPrerequisiteGroups, RuleIssue[] Reasons);
public sealed record CourseEligibility(CourseDefinition Course, Eligibility Eligibility);
public sealed record PlanOption(string Id, string CourseCode, string Title, int Credits, string Category,
    string? SectionCode, string[] Days, string? StartLocal, string? EndLocal,
    string Timezone, int? AvailableSeats, bool Tentative);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlanRequest(string TermCode, int MinCredits = 12, int MaxCredits = 18,
    string[]? PreferredDays = null, bool FewerCampusDays = true);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SelectionRequest(PlanRequest Constraints, string[] SelectionIds);
public sealed record PlanResult(string Status, string CatalogVersion, string TermCode, bool Tentative,
    int TotalCredits, PlanOption[] Selections, RuleIssue[] Issues, Guid? Id = null);
public sealed record SolverOption(string Id, string CourseCode, int Credits, bool Core,
    string[] Days, int? StartMinute, int? EndMinute);
public sealed record SolverRequest(SolverOption[] Options, int MinCredits, int MaxCredits,
    string[] PreferredDays, bool FewerCampusDays, double MaxTimeSeconds = 5);
public sealed record SolverResult(string Status, string[] SelectionIds);
