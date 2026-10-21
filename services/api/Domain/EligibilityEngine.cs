namespace DegreePilot.Api.Domain;

public sealed class EligibilityEngine(AuditEngine audit)
{
    public RuleIssue[] ValidateContext(PlanningContext context)
    {
        var result = audit.Calculate(context.Program, context.Courses, context.Attempts, context.CatalogVersion);
        if (result.Status != "valid") return result.Issues.ToArray();
        if (!SchedulingRules.ValidTerm(context.Term) ||
            context.Sections.Select(s => s.Id).Distinct().Count() != context.Sections.Length ||
            context.Sections.Any(s => s.CatalogVersion != context.CatalogVersion ||
                !context.Courses.Any(c => c.Code == s.CourseCode) || !SchedulingRules.ValidSection(s, context.Term)))
            return [new("CATALOG_INVALID", "Term or section data is invalid for this catalog.")];
        return [];
    }

    public Eligibility Evaluate(PlanningContext context, string courseCode)
    {
        var invalid = ValidateContext(context);
        if (invalid.Length > 0) return new(courseCode, "invalid", false, !context.Term.IsPublished, [], invalid);
        var course = context.Courses.SingleOrDefault(c => c.Code == courseCode);
        if (course is null) return new(courseCode, "invalid", false, !context.Term.IsPublished, [],
            [new("CATALOG_INVALID", "The course is not in the student's pinned catalog.")]);
        // Ordinary prerequisites require letter grades, even where a P might earn elective credit.
        var completed = context.Attempts.Where(a => a.Grade is "A" or "B" or "C" &&
            AuditEngine.TermOrder(a.Term) < AuditEngine.TermOrder(context.Term.Code)).Select(a => a.CourseCode).ToHashSet();
        if (completed.Contains(courseCode)) return new(courseCode, "completed", false, !context.Term.IsPublished, [],
            [new("ALREADY_COMPLETED", "Completed with C or better before this term.")]);
        var missing = course.PrerequisiteGroups.Where(group => !group.Any(completed.Contains)).ToArray();
        var publishedSections = context.Sections.Where(s => s.CourseCode == courseCode).ToArray();
        var offered = context.Term.IsPublished ? publishedSections.Length > 0
            : course.OfferedTerms.Contains(context.Term.Kind);
        var full = context.Term.IsPublished && publishedSections.Length > 0 &&
            publishedSections.All(s => s.AvailableSeats == 0);
        var reasons = new List<RuleIssue>();
        foreach (var group in missing) reasons.Add(new("MISSING_PREREQUISITE", $"Complete {string.Join(" or ", group)} with C or better before {context.Term.Code}."));
        if (!offered) reasons.Add(new("NOT_OFFERED", context.Term.IsPublished
            ? "No published section exists for this term." : "The catalog does not advertise this term pattern."));
        if (full) reasons.Add(new("SECTION_FULL", "All published sections have no available seats."));
        return new(courseCode, missing.Length > 0 || full ? "blocked" : !offered ? "not_offered" : "eligible",
            offered && !full && missing.Length == 0, !context.Term.IsPublished, missing, reasons.ToArray());
    }

    public PlanOption[] Options(PlanningContext context)
    {
        if (ValidateContext(context).Length > 0) return [];
        var courses = context.Courses.Where(c => Evaluate(context, c.Code).Eligible).ToDictionary(c => c.Code);
        if (!context.Term.IsPublished) return courses.Values.OrderBy(c => c.Code).Select(c => new PlanOption(
            $"tentative:{c.Code}", c.Code, c.Title, c.Credits, c.Category, null, [], null, null,
            context.Term.Timezone, null, true)).ToArray();
        return context.Sections.Where(s => courses.ContainsKey(s.CourseCode) && s.AvailableSeats > 0)
            .OrderBy(s => s.Id).Select(s => new PlanOption(s.Id, s.CourseCode, courses[s.CourseCode].Title,
                courses[s.CourseCode].Credits, courses[s.CourseCode].Category, s.SectionCode, s.Days,
                s.StartLocal, s.EndLocal, s.Timezone, s.AvailableSeats, false)).ToArray();
    }
}
