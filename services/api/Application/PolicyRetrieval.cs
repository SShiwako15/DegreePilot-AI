using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DegreePilot.Api.Application;

public sealed class PolicyRetrieval(AcademicDbContext db, PolicySources approvals)
{
    // Approval permits library publication. AI explanations additionally require a rule-reviewed
    // passage. Prose revisions cannot silently redefine the student's immutable catalog rules.
    public bool ExplanationsAreReviewed(PolicyCitation[] evidence) => evidence.All(e =>
        approvals.Sections.Any(s => s.CatalogVersion == e.CatalogVersion && s.Id == e.PolicySectionId && s.Text == e.Quote));
    public async Task<bool> RulesAreApprovedAsync(string catalogVersion, CancellationToken ct)
    {
        var document = await db.Catalogs.AsNoTracking().SingleOrDefaultAsync(c => c.Id == catalogVersion, ct);
        var courses = await db.Courses.AsNoTracking().Where(c => c.CatalogVersion == catalogVersion).ToArrayAsync(ct);
        try
        {
            var program = document is null ? null : JsonSerializer.Deserialize<ProgramDefinition>(document.RulesJson, FixtureReader.Json);
            var definitions = courses.Select(FixtureReader.ToDomain).ToArray();
            return program is not null && document!.Status == "published" &&
                CatalogValidator.Validate(program, definitions, catalogVersion).Count == 0 &&
                (approvals.RulesMatch(program, definitions) ||
                    (document.ApprovalHash.Length > 0 && document.ApprovalHash == PolicySources.Fingerprint(program, definitions)));
        }
        catch (JsonException) { return false; }
    }

    public async Task<PolicyCitation[]> ReadAsync(string catalogVersion, string[]? ids = null, CancellationToken ct = default)
    {
        // Validate the complete immutable document, even when retrieving a single citation.
        var rows = await (from section in db.PolicySections.AsNoTracking()
                          join document in db.PolicyDocuments.AsNoTracking() on section.DocumentId equals document.Id
                          where document.CatalogVersion == catalogVersion && document.Status == "approved"
                          select new { section, document }).ToArrayAsync(ct);
        if (rows.GroupBy(r => r.document.Id).Any(g => !g.All(r => approvals.Matches(r.document, r.section)) &&
                (g.First().document.ApprovalHash.Length == 0 || g.First().document.ApprovalHash !=
                    PolicySources.DocumentFingerprint(g.First().document, g.Select(r => r.section)))) ||
            rows.Select(r => r.section.Id).Distinct().Count() != rows.Length) return [];
        var selected = rows.Where(r => ids is null || ids.Contains(r.section.Id)).ToArray();
        if (ids is not null && selected.Length != ids.Distinct().Count()) return [];
        return selected.OrderBy(r => r.section.Id).Select(r => new PolicyCitation(r.section.Id, catalogVersion,
            r.document.Title, r.document.Version, r.section.Title, r.section.Text)).ToArray();
    }
}
