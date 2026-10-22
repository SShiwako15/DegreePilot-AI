using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using DegreePilot.Api.Domain;

namespace DegreePilot.Api.Persistence;

public sealed record PolicyApproval(string File, string CatalogVersion, int Version, string Status, string Sha256);
public sealed record ApprovedSection(string DocumentId, string CatalogVersion, string DocumentTitle,
    int Version, string SourceHash, string Id, string Title, string Text);

// Only explicitly approved, fingerprinted local sources may be seeded or retrieved.
public sealed partial class PolicySources
{
    public ApprovedSection[] Sections { get; }
    private readonly string rulesFingerprint;
    private readonly string catalogVersion;

    public static string Fingerprint(ProgramDefinition program, CourseDefinition[] courses) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(program, FixtureReader.Json) +
            JsonSerializer.Serialize(courses.OrderBy(c => c.Code, StringComparer.Ordinal), FixtureReader.Json))));
    public bool RulesMatch(ProgramDefinition program, CourseDefinition[] courses) =>
        program.CatalogVersion == catalogVersion && Fingerprint(program, courses) == rulesFingerprint;
    public static string DocumentFingerprint(PolicyDocument document, IEnumerable<PolicySection> sections) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            document.Id,
            document.CatalogVersion,
            document.Title,
            document.Version,
            sections = sections.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => new { s.Id, s.Title, s.Text })
        }))));
    [GeneratedRegex(@"^## ([A-Z][A-Z0-9-]+) — (.+)$", RegexOptions.Multiline)]
    private static partial Regex Headings();

    public PolicySources()
    {
        var program = FixtureReader.Read<ProgramDefinition>("program.json");
        catalogVersion = program.CatalogVersion;
        rulesFingerprint = Fingerprint(program, FixtureReader.Read<CourseDefinition[]>("courses.json"));
        var directory = Path.Combine(AppContext.BaseDirectory, "Policies");
        var approvals = JsonSerializer.Deserialize<PolicyApproval[]>(File.ReadAllText(Path.Combine(directory, "manifest.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Missing policy approvals.");
        var sections = new List<ApprovedSection>();
        foreach (var approval in approvals.Where(a => a.Status == "approved"))
        {
            if (Path.GetFileName(approval.File) != approval.File || approval.Version < 1)
                throw new InvalidOperationException("Invalid policy approval.");
            var text = File.ReadAllText(Path.Combine(directory, approval.File)).Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(approval.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Policy content changed. Review and version its approval before import.");
            var title = text.Split('\n')[0].TrimStart('#', ' ', '\uFEFF');
            var headings = Headings().Matches(text).ToArray();
            if (headings.Length == 0) throw new InvalidOperationException("Approved policy has no identifiable sections.");
            for (var i = 0; i < headings.Length; i++)
            {
                var start = headings[i].Index + headings[i].Length;
                var end = i + 1 < headings.Length ? headings[i + 1].Index : text.Length;
                var body = text[start..end].Trim();
                if (body.Length is 0 or > 3000) throw new InvalidOperationException("Invalid policy section size.");
                sections.Add(new($"{approval.CatalogVersion}:{Path.GetFileNameWithoutExtension(approval.File)}:{approval.Version}",
                    approval.CatalogVersion, title, approval.Version, approval.Sha256, headings[i].Groups[1].Value,
                    headings[i].Groups[2].Value.Trim(), body));
            }
        }
        if (sections.GroupBy(s => (s.CatalogVersion, s.Id)).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Ambiguous approved citation IDs.");
        Sections = sections.ToArray();
    }

    public bool Matches(PolicyDocument document, PolicySection section) => Sections.Any(s =>
        s.CatalogVersion == document.CatalogVersion && s.DocumentId == document.Id && s.Version == document.Version &&
        s.SourceHash == document.SourceHash && s.DocumentTitle == document.Title && s.Id == section.Id &&
        s.Title == section.Title && s.Text == section.Text && document.Status is "approved" or "retired");

    public async Task SeedAsync(AcademicDbContext db)
    {
        foreach (var group in Sections.GroupBy(s => s.DocumentId))
        {
            var first = group.First();
            var document = await db.PolicyDocuments.SingleOrDefaultAsync(d => d.Id == first.DocumentId);
            // A new manifest can supersede older copies whose academic passages are all
            // preserved verbatim. Keep their immutable history. Never replace a differing
            // administrator revision, or restore a seed explicitly retired by publication.
            if (document is null || document.Status == "approved")
            {
                var candidates = await db.PolicyDocuments.Where(d => d.CatalogVersion == first.CatalogVersion &&
                    d.Status == "approved" && d.Id != first.DocumentId).ToArrayAsync();
                foreach (var prior in candidates)
                {
                    var passages = await db.PolicySections.Where(s => s.DocumentId == prior.Id).ToArrayAsync();
                    if (passages.Length == 0 || !passages.All(p => group.Any(s =>
                        s.Id == p.Id && s.Title == p.Title && s.Text == p.Text))) continue;
                    if (prior.ApprovalHash.Length > 0 && prior.ApprovalHash != DocumentFingerprint(prior, passages)) continue;
                    prior.Status = "retired";
                    prior.Revision++;
                }
            }
            if (document is null)
            {
                document = new()
                {
                    Id = first.DocumentId,
                    CatalogVersion = first.CatalogVersion,
                    Title = first.DocumentTitle,
                    Version = first.Version,
                    Status = "approved",
                    SourceHash = first.SourceHash
                };
                db.PolicyDocuments.Add(document);
                db.PolicySections.AddRange(group.Select(s => new PolicySection
                { DocumentId = s.DocumentId, Id = s.Id, Title = s.Title, Text = s.Text }));
            }
            else
            {
                var stored = await db.PolicySections.Where(s => s.DocumentId == document.Id).ToArrayAsync();
                if (stored.Length != group.Count() || stored.Any(s => !Matches(document, s)))
                    throw new InvalidOperationException("Published policy changed. Use an explicitly approved new version.");
            }
        }
        await db.SaveChangesAsync();
    }
}
