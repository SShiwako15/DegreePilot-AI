using System.Text.Json;
using DegreePilot.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Persistence;

public sealed record SectionFixture(string Id, string TermCode, string CourseCode, string SectionCode,
    string[] Days, string StartLocal, string EndLocal, string Timezone, int Capacity, int AvailableSeats, string Delivery);

public static class PlanningFixtures
{
    public static TermDefinition ToDomain(AcademicTerm t) => new(t.Code, t.Name, t.Kind, t.StartsOn, t.EndsOn, t.Timezone, t.IsPublished);
    public static SectionDefinition ToDomain(CourseSection s) => new(s.Id, s.CatalogVersion, s.TermCode, s.CourseCode,
        s.SectionCode, JsonSerializer.Deserialize<string[]>(s.DaysJson) ?? throw new JsonException("Missing weekdays."),
        s.StartLocal, s.EndLocal, s.Timezone, s.Capacity, s.AvailableSeats, s.Delivery);

    public static async Task SeedAsync(AcademicDbContext db, ProgramDefinition program, CourseDefinition[] courses)
    {
        var terms = FixtureReader.Read<TermDefinition[]>("terms.json");
        var sections = FixtureReader.Read<SectionFixture[]>("sections.json");
        if (terms.Select(t => t.Code).Distinct().Count() != terms.Length || terms.Any(t => !SchedulingRules.ValidTerm(t)))
            throw new InvalidOperationException("CATALOG_INVALID: term fixtures are invalid.");
        foreach (var t in terms)
            if (!await db.Terms.AnyAsync(x => x.Code == t.Code)) db.Terms.Add(new()
            { Code = t.Code, Name = t.Name, Kind = t.Kind, StartsOn = t.StartsOn, EndsOn = t.EndsOn, Timezone = t.Timezone, IsPublished = t.IsPublished });
        await db.SaveChangesAsync();
        foreach (var s in sections)
        {
            var term = terms.SingleOrDefault(t => t.Code == s.TermCode);
            var section = new SectionDefinition(s.Id, program.CatalogVersion, s.TermCode, s.CourseCode,
                s.SectionCode, s.Days, s.StartLocal, s.EndLocal, s.Timezone, s.Capacity, s.AvailableSeats, s.Delivery);
            if (term is null || !term.IsPublished || !courses.Any(c => c.Code == s.CourseCode) || !SchedulingRules.ValidSection(section, term))
                throw new InvalidOperationException("CATALOG_INVALID: section fixtures are invalid.");
            if (!await db.Sections.AnyAsync(x => x.CatalogVersion == program.CatalogVersion && x.Id == s.Id)) db.Sections.Add(new()
            {
                Id = s.Id,
                CatalogVersion = program.CatalogVersion,
                TermCode = s.TermCode,
                CourseCode = s.CourseCode,
                SectionCode = s.SectionCode,
                DaysJson = JsonSerializer.Serialize(s.Days),
                StartLocal = s.StartLocal,
                EndLocal = s.EndLocal,
                Timezone = s.Timezone,
                Capacity = s.Capacity,
                AvailableSeats = s.AvailableSeats,
                Delivery = s.Delivery
            });
        }
        await db.SaveChangesAsync();
    }
}
