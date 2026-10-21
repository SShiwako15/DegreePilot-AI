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
    public string Status { get; set; } = "published";
    public int Revision { get; set; } = 1;
    public string ApprovalHash { get; set; } = "";
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

public sealed class AcademicTerm
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public string Timezone { get; set; } = "America/New_York";
    public bool IsPublished { get; set; }
}

public sealed class CourseSection
{
    public string Id { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public string TermCode { get; set; } = "";
    public string CourseCode { get; set; } = "";
    public string SectionCode { get; set; } = "";
    public string DaysJson { get; set; } = "[]";
    public string StartLocal { get; set; } = "";
    public string EndLocal { get; set; } = "";
    public string Timezone { get; set; } = "America/New_York";
    public int Capacity { get; set; }
    public int AvailableSeats { get; set; }
    public string Delivery { get; set; } = "";
}

public sealed class SchedulePlan
{
    public Guid Id { get; set; }
    public string StudentId { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public string TermCode { get; set; } = "";
    public string ConstraintsJson { get; set; } = "";
    public string SelectionIdsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PolicyDocument
{
    public string Id { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public string Title { get; set; } = "";
    public int Version { get; set; }
    public string Status { get; set; } = "draft";
    public string SourceHash { get; set; } = "";
    public string? ReplacesDocumentId { get; set; }
    public string ApprovalHash { get; set; } = "";
    public int Revision { get; set; } = 1;
}

public sealed class PolicySection
{
    public string DocumentId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class AdvisorAssignment
{
    public string AdvisorId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public bool Active { get; set; }
    public int Revision { get; set; } = 1;
}

// An advisor proposal is separate from a student's saved plan and awards no credit.
public sealed class AdvisorSuggestion
{
    public Guid Id { get; set; }
    public string AdvisorId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public string TermCode { get; set; } = "";
    public string ConstraintsJson { get; set; } = "";
    public string SelectionIdsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
}

// Deliberately excludes request bodies, policy text, transcripts and model messages.
public sealed class MutationAudit
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public int Revision { get; set; }
    public string ContentHash { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
