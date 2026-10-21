using System.Text.Json;
using DegreePilot.Api.Domain;

namespace DegreePilot.Api.Persistence;

public sealed record UserFixture
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public string? CatalogVersion { get; init; }
    public string? ProgramId { get; init; }
    public Attempt[] Transcript { get; init; } = [];
    public string[] AssignedStudentIds { get; init; } = [];
}

public static class FixtureReader
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public static T Read<T>(string name) => JsonSerializer.Deserialize<T>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), Json)
        ?? throw new InvalidOperationException("Fixture is empty.");

    public static CourseDefinition ToDomain(CatalogCourse c) => new()
    {
        Code = c.Code,
        Title = c.Title,
        Credits = c.Credits,
        Category = c.Category,
        CatalogVersion = c.CatalogVersion,
        PrerequisiteGroups = JsonSerializer.Deserialize<string[][]>(c.PrerequisitesJson)
            ?? throw new JsonException("Prerequisite data is missing."),
        OfferedTerms = JsonSerializer.Deserialize<string[]>(c.OfferedTermsJson)
            ?? throw new JsonException("Offering data is missing.")
    };
}
