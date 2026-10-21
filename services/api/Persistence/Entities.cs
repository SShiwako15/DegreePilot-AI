namespace DegreePilot.Api.Persistence;

public sealed class AppUser
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

public sealed class CatalogEdition
{
    public string Id { get; set; } = "";
    public string ProgramId { get; set; } = "";
    public string RulesJson { get; set; } = "";
    public string SourceHash { get; set; } = "";
}

public sealed class CatalogCourse
{
    public string CatalogVersion { get; set; } = "";
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Credits { get; set; }
    public string Category { get; set; } = "";
    public string PrerequisitesJson { get; set; } = "[]";
    public string OfferedTermsJson { get; set; } = "[]";
}

public sealed class StudentProfile
{
    public string UserId { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
}

public sealed class EnrollmentAttempt
{
    public Guid Id { get; set; }
    public string StudentId { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public string CourseCode { get; set; } = "";
    public string Term { get; set; } = "";
    public string Grade { get; set; } = "";
}
