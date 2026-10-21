namespace DegreePilot.Api.Domain;

public static class CatalogValidator
{
    public static readonly string[] Categories = ["core", "cs_elective", "gen_ed", "free_elective"];
    public static readonly IReadOnlyDictionary<string, int> GradePoints = new Dictionary<string, int>
    { ["A"] = 4, ["B"] = 3, ["C"] = 2, ["D"] = 1, ["F"] = 0 };

    public static List<RuleIssue> Validate(ProgramDefinition? program, IReadOnlyList<CourseDefinition> courses, string pinnedVersion)
    {
        var issues = new List<RuleIssue>();
        void Invalid(string message) => issues.Add(new("CATALOG_INVALID", message));
        if (program is null || program.CatalogVersion != pinnedVersion)
        {
            Invalid("The student's pinned catalog is missing or does not match the loaded rules.");
            return issues;
        }
        if (string.IsNullOrWhiteSpace(program.ProgramId) || string.IsNullOrWhiteSpace(program.Institution) ||
            string.IsNullOrWhiteSpace(program.ProgramName) || string.IsNullOrWhiteSpace(program.GradePolicyDocument) ||
            program.EffectiveYear is < 1900 or > 9999 || program.MinTotalCredits <= 0 ||
            program.MinCsElectiveCredits < 0 || program.MinGenEdCredits < 0 || program.MinFreeElectiveCredits < 0 ||
            program.MinPassingGrade != "C" || program.GradePointScale is null ||
            program.GradePointScale.Count != GradePoints.Count ||
            GradePoints.Any(x => !program.GradePointScale.TryGetValue(x.Key, out var points) || points != x.Value))
            Invalid("Required credit or grade rules are missing or unsupported.");
        if (program.PassCreditCategories is null || program.PassCreditCategories.Any(x => x == "core" || !Categories.Contains(x)))
            Invalid("Pass-grade approval must name only supported elective categories.");
        var core = program.CoreCourseCodes;
        if (core is null || core.Length == 0 || core.Any(string.IsNullOrWhiteSpace) || core.Distinct().Count() != core.Length)
        {
            Invalid("Core requirements must be a nonempty set of unique course codes.");
            return issues;
        }
        if (courses.Count == 0 || courses.Any(x => string.IsNullOrWhiteSpace(x.Code)) ||
            courses.Select(x => x.Code).Distinct().Count() != courses.Count)
        {
            Invalid("The catalog contains missing or duplicate course codes.");
            return issues;
        }
        var byCode = courses.ToDictionary(x => x.Code);
        foreach (var code in core)
            if (!byCode.TryGetValue(code, out var course) || course.Category != "core")
                Invalid($"Required core course {code} is missing or has a conflicting category.");
        foreach (var course in courses)
        {
            if (course.CatalogVersion != pinnedVersion || course.Credits is <= 0 or > 30 ||
                string.IsNullOrWhiteSpace(course.Title) || !Categories.Contains(course.Category) ||
                (course.Category == "core" && !core.Contains(course.Code)))
                Invalid($"Course {course.Code} has invalid credits, category, or catalog version.");
            if (course.OfferedTerms is null || course.OfferedTerms.Any(x => x is not ("Fall" or "Spring")))
                Invalid($"Course {course.Code} has unsupported offering data.");
            if (course.PrerequisiteGroups is null || course.PrerequisiteGroups.Any(g =>
                    g is null || g.Length == 0 || g.Any(code => string.IsNullOrWhiteSpace(code) || !byCode.ContainsKey(code))))
                Invalid($"Course {course.Code} references missing or malformed prerequisites.");
        }
        if (issues.Count > 0) return issues;
        // Validate dependency data at import; eligibility evaluation is deferred to Phase 2.
        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();
        bool HasCycle(string code)
        {
            if (visiting.Contains(code)) return true;
            if (!visited.Add(code)) return false;
            visiting.Add(code);
            foreach (var dependency in byCode[code].PrerequisiteGroups.SelectMany(x => x))
                if (HasCycle(dependency)) return true;
            visiting.Remove(code);
            return false;
        }
        if (courses.Any(x => HasCycle(x.Code))) Invalid("The catalog contains a prerequisite cycle.");
        return issues;
    }
}
