using System.Text.RegularExpressions;

namespace DegreePilot.Api.Domain;

public sealed partial class AuditEngine(TimeProvider timeProvider)
{
    public const string Version = "1.0.0";
    [GeneratedRegex(@"^[0-9]{4}-(SPRING|FALL)\z", RegexOptions.CultureInvariant)]
    private static partial Regex TermPattern();
    public static int TermOrder(string term) => int.Parse(term[..4], System.Globalization.CultureInfo.InvariantCulture) * 2 + (term.EndsWith("FALL", StringComparison.Ordinal) ? 1 : 0);

    public DegreeAudit Calculate(ProgramDefinition? program, IReadOnlyList<CourseDefinition> courses,
        IReadOnlyList<Attempt> attempts, string pinnedVersion)
    {
        var issues = CatalogValidator.Validate(program, courses, pinnedVersion);
        DegreeAudit Invalid() => new("invalid", pinnedVersion, Version, timeProvider.GetUtcNow(),
            null, null, null, null, [], [], [], issues);
        if (issues.Count > 0 || program is null) return Invalid();
        var catalog = courses.ToDictionary(x => x.Code);
        if (attempts.GroupBy(x => (x.CourseCode, x.Term)).Any(x => x.Count() > 1))
            issues.Add(new("TRANSCRIPT_INVALID", "Duplicate course attempts in the same term are unsupported."));
        foreach (var attempt in attempts)
        {
            if (attempt.CourseCode is null || !catalog.ContainsKey(attempt.CourseCode))
                issues.Add(new("TRANSCRIPT_INVALID", "A transcript course is not in the pinned catalog; transfer equivalency is unsupported."));
            if (attempt.Term is null || !TermPattern().IsMatch(attempt.Term))
                issues.Add(new("TRANSCRIPT_INVALID", "An attempt has an invalid term; expected YYYY-SPRING or YYYY-FALL."));
            if (attempt.Grade is not ("A" or "B" or "C" or "D" or "F" or "W" or "I" or "P"))
                issues.Add(new("TRANSCRIPT_INVALID", "An attempt has an unsupported grade."));
        }
        if (issues.Count > 0) return Invalid();
        bool EarnsCredit(Attempt a) => a.Grade is "A" or "B" or "C" ||
            (a.Grade == "P" && program.PassCreditCategories.Contains(catalog[a.CourseCode].Category));
        var earned = attempts.Where(EarnsCredit).GroupBy(x => x.CourseCode)
            .Select(g => g.OrderByDescending(x => TermOrder(x.Term)).First())
            .OrderBy(x => x.CourseCode, StringComparer.Ordinal)
            .Select(x => new EarnedCourse(x.CourseCode, catalog[x.CourseCode].Credits, catalog[x.CourseCode].Category, x.Grade, x.Term)).ToArray();
        var core = program.CoreCourseCodes.Select(code =>
        {
            var pass = earned.SingleOrDefault(x => x.Code == code);
            return new CoreRequirement(code, catalog[code].Title, catalog[code].Credits, pass is not null, pass?.Grade, pass?.Term);
        }).ToArray();
        int Credits(string category) => earned.Where(x => x.Category == category).Sum(x => x.Credits);
        CreditBucket[] buckets = [
            new("core", "Required core", Credits("core"), core.Sum(x => x.Credits)),
            new("cs_elective", "CS electives", Credits("cs_elective"), program.MinCsElectiveCredits),
            new("gen_ed", "General education", Credits("gen_ed"), program.MinGenEdCredits),
            new("free_elective", "Free electives", Credits("free_elective"), program.MinFreeElectiveCredits)
        ];
        var graded = attempts.Where(x => CatalogValidator.GradePoints.ContainsKey(x.Grade)).ToArray();
        var gpaCredits = graded.Sum(x => catalog[x.CourseCode].Credits);
        decimal? gpa = gpaCredits == 0 ? null : Math.Round(graded.Sum(x =>
            (decimal)CatalogValidator.GradePoints[x.Grade] * catalog[x.CourseCode].Credits) / gpaCredits, 2, MidpointRounding.AwayFromZero);
        var total = earned.Sum(x => x.Credits);
        return new("valid", pinnedVersion, Version, timeProvider.GetUtcNow(), total, program.MinTotalCredits, gpa,
            total >= program.MinTotalCredits && buckets.All(x => x.Complete) && core.All(x => x.Complete), buckets, core, earned, []);
    }
}
