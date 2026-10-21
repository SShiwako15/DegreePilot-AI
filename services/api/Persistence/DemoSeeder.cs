using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DegreePilot.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Persistence;

public static class DemoSeeder
{
    public static async Task SeedAsync(AcademicDbContext db, string password, AuditEngine engine)
    {
        if (password.Length < 12) throw new InvalidOperationException("Demo password must contain at least 12 characters.");
        var program = FixtureReader.Read<ProgramDefinition>("program.json");
        var courses = FixtureReader.Read<CourseDefinition[]>("courses.json");
        var users = FixtureReader.Read<UserFixture[]>("students.json");
        if (CatalogValidator.Validate(program, courses, program.CatalogVersion).Count > 0)
            throw new InvalidOperationException("CATALOG_INVALID: synthetic catalog validation failed.");
        foreach (var user in users.Where(x => x.Role == "student"))
        {
            if (user.CatalogVersion != program.CatalogVersion || user.ProgramId != program.ProgramId ||
                engine.Calculate(program, courses, user.Transcript, user.CatalogVersion).Status != "valid")
                throw new InvalidOperationException("TRANSCRIPT_INVALID: synthetic student validation failed.");
        }
        var rules = JsonSerializer.Serialize(program, FixtureReader.Json);
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rules + JsonSerializer.Serialize(courses, FixtureReader.Json))));
        await using var transaction = await db.Database.BeginTransactionAsync();
        var existing = await db.Catalogs.SingleOrDefaultAsync(x => x.Id == program.CatalogVersion);
        if (existing is not null && existing.SourceHash != sourceHash)
            throw new InvalidOperationException("Pinned catalog fixtures changed. Publish a new catalog version instead of overwriting rules.");
        if (existing is null)
        {
            db.Catalogs.Add(new() { Id = program.CatalogVersion, ProgramId = program.ProgramId, RulesJson = rules, SourceHash = sourceHash });
            db.Courses.AddRange(courses.Select(c => new CatalogCourse
            {
                CatalogVersion = c.CatalogVersion,
                Code = c.Code,
                Title = c.Title,
                Credits = c.Credits,
                Category = c.Category,
                PrerequisitesJson = JsonSerializer.Serialize(c.PrerequisiteGroups),
                OfferedTermsJson = JsonSerializer.Serialize(c.OfferedTerms)
            }));
            await db.SaveChangesAsync();
        }
        var hasher = new PasswordHasher<AppUser>();
        foreach (var fixture in users)
        {
            if (await db.Users.AnyAsync(x => x.Id == fixture.Id)) continue;
            var user = new AppUser { Id = fixture.Id, DisplayName = fixture.DisplayName, Email = fixture.Email, Role = fixture.Role };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
            if (fixture.Role == "student")
            {
                db.Students.Add(new() { UserId = user.Id, CatalogVersion = fixture.CatalogVersion! });
                db.Attempts.AddRange(fixture.Transcript.Select(a => new EnrollmentAttempt
                {
                    Id = Guid.NewGuid(),
                    StudentId = user.Id,
                    CatalogVersion = fixture.CatalogVersion!,
                    CourseCode = a.CourseCode,
                    Term = a.Term,
                    Grade = a.Grade
                }));
            }
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
