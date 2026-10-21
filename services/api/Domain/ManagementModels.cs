using System.Text.Json.Serialization;

namespace DegreePilot.Api.Domain;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssignmentRequest(string AdvisorId, string StudentId, bool Active, int Revision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CatalogCloneRequest(string SourceVersion, string CatalogVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CatalogDraftRequest(int Revision, ProgramDefinition Program, CourseDefinition[] Courses);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RevisionRequest(int Revision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PolicyCloneRequest(string SourceDocumentId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PolicyText(string Id, string Title, string Text);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PolicyDraftRequest(int Revision, string Title, PolicyText[] Sections);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SectionSeatsRequest(string CatalogVersion, string SectionId, int Capacity, int AvailableSeats,
    int ExpectedCapacity, int ExpectedAvailableSeats);
