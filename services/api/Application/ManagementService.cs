using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Application;

// Admin commands never change a student's pinned catalog, attempts, grades or saved plans.
public sealed partial class ManagementService(AcademicDbContext db, TimeProvider clock)
{
    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{2,79}$")]
    private static partial Regex VersionId();
    private static void Require(bool condition, int status, string message)
    {
        if (!condition) throw new WorkflowException(status, message);
    }
    private static void Editable(string status, int actual, int expected) =>
        Require(status == "draft" && actual == expected, 409, "This draft changed or is already published. Refresh before retrying.");
    public async Task AssignAsync(string actor, AssignmentRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        Require(await db.Users.AnyAsync(u => u.Id == request.AdvisorId && u.Role == "advisor", ct) &&
            await db.Students.AnyAsync(s => s.UserId == request.StudentId, ct), 400, "Choose an existing advisor and student.");
        var assignment = await db.AdvisorAssignments.SingleOrDefaultAsync(a => a.AdvisorId == request.AdvisorId && a.StudentId == request.StudentId, ct);
        Require((assignment?.Revision ?? 0) == request.Revision, 409, "Assignment changed. Refresh before retrying.");
        if (assignment is null)
        {
            assignment = new() { AdvisorId = request.AdvisorId, StudentId = request.StudentId };
            db.AdvisorAssignments.Add(assignment);
        }
        else assignment.Revision++;
        assignment.Active = request.Active;
        AuditTrail.Add(db, clock, actor, request.Active ? "assignment.grant" : "assignment.revoke",
            $"{assignment.AdvisorId}/{assignment.StudentId}", assignment.Revision);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<string> CloneCatalogAsync(string actor, CatalogCloneRequest request, CancellationToken ct)
    {
        Require(request.CatalogVersion is not null && VersionId().IsMatch(request.CatalogVersion), 400, "Use a unique catalog ID of 3–80 uppercase letters, digits and hyphens.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        Require(!await db.Catalogs.AnyAsync(c => c.Id == request.CatalogVersion, ct), 409, "That catalog version already exists.");
        var source = await db.Catalogs.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.SourceVersion && c.Status == "published", ct);
        Require(source is not null, 404, "Published source catalog not found.");
        var program = JsonSerializer.Deserialize<ProgramDefinition>(source!.RulesJson, FixtureReader.Json)! with { CatalogVersion = request.CatalogVersion! };
        var courses = (await db.Courses.AsNoTracking().Where(c => c.CatalogVersion == source.Id).ToArrayAsync(ct))
            .Select(FixtureReader.ToDomain).Select(c => c with { CatalogVersion = request.CatalogVersion! }).ToArray();
        Require(CatalogValidator.Validate(program, courses, program.CatalogVersion).Count == 0, 422, "Source catalog is invalid.");
        var edition = new CatalogEdition
        {
            Id = program.CatalogVersion,
            ProgramId = program.ProgramId,
            RulesJson = JsonSerializer.Serialize(program, FixtureReader.Json),
            Status = "draft"
        };
        db.Catalogs.Add(edition);
        db.Courses.AddRange(courses.Select(ToEntity));
        AuditTrail.Add(db, clock, actor, "catalog.draft", edition.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return edition.Id;
    }

    public async Task EditCatalogAsync(string actor, string version, CatalogDraftRequest request, CancellationToken ct)
    {
        Require(request.Program is not null && request.Courses is { Length: > 0 and <= 200 } &&
            request.Courses.All(c => c is not null && c.Title?.Length <= 200 && c.PrerequisiteGroups?.Length <= 30 &&
                c.PrerequisiteGroups.All(g => g is { Length: > 0 and <= 30 })) &&
            CatalogValidator.Validate(request.Program, request.Courses, version).Count == 0,
            422, "Catalog has invalid requirements, courses or prerequisite cycles.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var edition = await db.Catalogs.SingleOrDefaultAsync(c => c.Id == version, ct);
        Require(edition is not null, 404, "Catalog not found.");
        Editable(edition!.Status, edition.Revision, request.Revision);
        db.Courses.RemoveRange(await db.Courses.Where(c => c.CatalogVersion == version).ToArrayAsync(ct));
        // Save removal inside the transaction so EF can track replacement composite keys.
        await db.SaveChangesAsync(ct);
        edition.RulesJson = JsonSerializer.Serialize(request.Program, FixtureReader.Json);
        edition.ProgramId = request.Program!.ProgramId;
        edition.Revision++;
        db.Courses.AddRange(request.Courses!.Select(ToEntity));
        AuditTrail.Add(db, clock, actor, "catalog.edit", version, edition.Revision);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task PublishCatalogAsync(string actor, string version, int revision, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var edition = await db.Catalogs.SingleOrDefaultAsync(c => c.Id == version, ct);
        Require(edition is not null, 404, "Catalog not found.");
        Editable(edition!.Status, edition.Revision, revision);
        var program = JsonSerializer.Deserialize<ProgramDefinition>(edition.RulesJson, FixtureReader.Json)!;
        var courses = (await db.Courses.AsNoTracking().Where(c => c.CatalogVersion == version).ToArrayAsync(ct)).Select(FixtureReader.ToDomain).ToArray();
        Require(CatalogValidator.Validate(program, courses, version).Count == 0, 422, "Catalog validation failed.");
        edition.ApprovalHash = PolicySources.Fingerprint(program, courses);
        edition.SourceHash = edition.ApprovalHash;
        edition.Status = "published";
        edition.Revision++;
        AuditTrail.Add(db, clock, actor, "catalog.publish", version, edition.Revision, edition.ApprovalHash);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<string> ClonePolicyAsync(string actor, string id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var source = await db.PolicyDocuments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Status == "approved", ct);
        Require(source is not null, 404, "Approved source document not found.");
        Require(!await db.PolicyDocuments.AnyAsync(p => p.ReplacesDocumentId == id && p.Status == "draft", ct), 409, "A revision draft already exists for this document.");
        var draft = new PolicyDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            CatalogVersion = source!.CatalogVersion,
            Title = source.Title,
            Version = source.Version + 1,
            Status = "draft",
            ReplacesDocumentId = id
        };
        db.PolicyDocuments.Add(draft);
        db.PolicySections.AddRange((await db.PolicySections.AsNoTracking().Where(s => s.DocumentId == id).ToArrayAsync(ct))
            .Select(s => new PolicySection { DocumentId = draft.Id, Id = s.Id, Title = s.Title, Text = s.Text }));
        AuditTrail.Add(db, clock, actor, "policy.draft", draft.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return draft.Id;
    }

    public async Task EditPolicyAsync(string actor, string id, PolicyDraftRequest request, CancellationToken ct)
    {
        Require(!string.IsNullOrWhiteSpace(request.Title) && request.Title.Length <= 200 && request.Sections is { Length: > 0 and <= 30 } &&
            request.Sections.All(s => s is not null && s.Id is not null && VersionId().IsMatch(s.Id) &&
                !string.IsNullOrWhiteSpace(s.Title) && s.Title.Length <= 200 && !string.IsNullOrWhiteSpace(s.Text) && s.Text.Length <= 3000) &&
            request.Sections.Select(s => s.Id).Distinct().Count() == request.Sections.Length,
            422, "Supply unique source IDs, titles and policy passages of 1–3000 characters.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var doc = await db.PolicyDocuments.SingleOrDefaultAsync(p => p.Id == id, ct);
        Require(doc is not null, 404, "Policy document not found.");
        Editable(doc!.Status, doc.Revision, request.Revision);
        db.PolicySections.RemoveRange(await db.PolicySections.Where(s => s.DocumentId == id).ToArrayAsync(ct));
        await db.SaveChangesAsync(ct);
        doc.Title = request.Title;
        doc.Revision++;
        db.PolicySections.AddRange(request.Sections!.Select(s => new PolicySection { DocumentId = id, Id = s.Id, Title = s.Title, Text = s.Text }));
        AuditTrail.Add(db, clock, actor, "policy.edit", id, doc.Revision);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task PublishPolicyAsync(string actor, string id, int revision, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var doc = await db.PolicyDocuments.SingleOrDefaultAsync(p => p.Id == id, ct);
        Require(doc is not null, 404, "Policy document not found.");
        Editable(doc!.Status, doc.Revision, revision);
        var source = await db.PolicyDocuments.SingleOrDefaultAsync(p => p.Id == doc.ReplacesDocumentId, ct);
        Require(source?.Status == "approved", 409, "The source was already retired. Refresh and create a new revision.");
        Require(await db.Catalogs.AnyAsync(c => c.Id == doc.CatalogVersion && c.Status == "published", ct), 422, "Publish the catalog first.");
        var sections = await db.PolicySections.AsNoTracking().Where(s => s.DocumentId == id).ToArrayAsync(ct);
        var otherIds = await (from s in db.PolicySections
                              join p in db.PolicyDocuments on s.DocumentId equals p.Id
                              where p.CatalogVersion == doc.CatalogVersion && p.Status == "approved" && p.Id != source!.Id
                              select s.Id).ToArrayAsync(ct);
        Require(sections.Length > 0 && !sections.Any(s => otherIds.Contains(s.Id)), 422, "Approved source IDs must be unambiguous in this catalog.");
        doc.ApprovalHash = PolicySources.DocumentFingerprint(doc, sections);
        doc.SourceHash = doc.ApprovalHash;
        doc.Status = "approved";
        doc.Revision++;
        source!.Status = "retired";
        source.Revision++;
        AuditTrail.Add(db, clock, actor, "policy.publish", id, doc.Revision, doc.ApprovalHash);
        AuditTrail.Add(db, clock, actor, "policy.retire", source.Id, source.Revision, source.SourceHash);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task SetSeatsAsync(string actor, SectionSeatsRequest request, CancellationToken ct)
    {
        Require(request.Capacity is > 0 and <= 1000 && request.AvailableSeats >= 0 && request.AvailableSeats <= request.Capacity,
            422, "Seat counts must be within capacity (1–1000).");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var section = await db.Sections.SingleOrDefaultAsync(s => s.CatalogVersion == request.CatalogVersion && s.Id == request.SectionId, ct);
        Require(section is not null, 404, "Section not found.");
        Require(section!.Capacity == request.ExpectedCapacity && section.AvailableSeats == request.ExpectedAvailableSeats,
            409, "Section changed. Refresh before retrying.");
        section.Capacity = request.Capacity;
        section.AvailableSeats = request.AvailableSeats;
        AuditTrail.Add(db, clock, actor, "section.seats", $"{section.CatalogVersion}/{section.Id}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static CatalogCourse ToEntity(CourseDefinition c) => new()
    {
        CatalogVersion = c.CatalogVersion,
        Code = c.Code,
        Title = c.Title,
        Credits = c.Credits,
        Category = c.Category,
        PrerequisitesJson = JsonSerializer.Serialize(c.PrerequisiteGroups),
        OfferedTermsJson = JsonSerializer.Serialize(c.OfferedTerms)
    };
}
