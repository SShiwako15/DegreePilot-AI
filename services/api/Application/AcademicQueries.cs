using System.Text.Json;
using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Application;

public sealed record TranscriptRow(string CourseCode, string Title, int Credits, string Term, string Grade);
public sealed record StudentTranscript(string CatalogVersion, IReadOnlyList<TranscriptRow> Attempts);

public sealed class AcademicQueries(AcademicDbContext db, AuditEngine engine)
{
    public async Task<StudentTranscript?> TranscriptAsync(string studentId)
    {
        var profile = await db.Students.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == studentId);
        if (profile is null) return null;
        var rows = await (from a in db.Attempts.AsNoTracking()
                          join c in db.Courses.AsNoTracking() on new { a.CatalogVersion, Code = a.CourseCode }
                              equals new { c.CatalogVersion, c.Code }
                          where a.StudentId == studentId
                          select new TranscriptRow(a.CourseCode, c.Title, c.Credits, a.Term, a.Grade)).ToArrayAsync();
        return new(profile.CatalogVersion, rows.OrderByDescending(x => AuditEngine.TermOrder(x.Term)).ThenBy(x => x.CourseCode).ToArray());
    }

    public async Task<DegreeAudit?> AuditAsync(string studentId)
    {
        var profile = await db.Students.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == studentId);
        if (profile is null) return null;
        var edition = await db.Catalogs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == profile.CatalogVersion);
        var courses = await db.Courses.AsNoTracking().Where(x => x.CatalogVersion == profile.CatalogVersion).ToArrayAsync();
        var attempts = await db.Attempts.AsNoTracking().Where(x => x.StudentId == studentId).ToArrayAsync();
        try
        {
            return engine.Calculate(edition is null ? null : JsonSerializer.Deserialize<ProgramDefinition>(edition.RulesJson, FixtureReader.Json),
                courses.Select(FixtureReader.ToDomain).ToArray(),
                attempts.Select(x => new Attempt(x.CourseCode, x.Term, x.Grade)).ToArray(), profile.CatalogVersion);
        }
        catch (JsonException)
        {
            return engine.Calculate(null, [], [], profile.CatalogVersion);
        }
    }
}
