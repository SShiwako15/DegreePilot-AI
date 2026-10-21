namespace DegreePilot.Api.Domain;

// Independently checks selected IDs against authoritative data; never trusts solver credit/time fields.
public sealed class PlanValidator(EligibilityEngine eligibility, TimeProvider clock)
{
    public RuleIssue[] CheckRequest(PlanningContext context, PlanRequest request, bool requireFuture = true)
    {
        var issues = new List<RuleIssue>(SchedulingRules.ValidateRequest(request));
        issues.AddRange(eligibility.ValidateContext(context));
        if (request.TermCode != context.Term.Code) issues.Add(new("TERM_MISMATCH", "The plan term does not match its data."));
        var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).Date;
        if (requireFuture && context.Term.StartsOn <= DateOnly.FromDateTime(today))
            issues.Add(new("TERM_NOT_FUTURE", "Choose a future term for planning."));
        return issues.ToArray();
    }

    public PlanResult Validate(PlanningContext context, PlanRequest request, string[] ids, bool requireFuture = true)
    {
        var issues = new List<RuleIssue>(CheckRequest(context, request, requireFuture));
        if (issues.Count > 0) return new("INVALID", context.CatalogVersion, request.TermCode,
            !context.Term.IsPublished, 0, [], issues.ToArray());
        if (ids.Length == 0 || ids.Length > 30 || ids.Distinct().Count() != ids.Length)
            issues.Add(new("DUPLICATE_OR_EMPTY_SELECTION", "Select unique courses or sections; a plan cannot be empty."));
        var selected = new List<PlanOption>();
        foreach (var id in ids.Take(30))
        {
            if (context.Term.IsPublished)
            {
                var section = context.Sections.SingleOrDefault(s => s.Id == id);
                if (section is null) { issues.Add(new("SECTION_INVALID", "A selected section does not belong to this term and catalog.")); continue; }
                var course = context.Courses.SingleOrDefault(c => c.Code == section.CourseCode);
                if (course is null) continue; // Invalid catalog is already reported above.
                issues.AddRange(eligibility.Evaluate(context, course.Code).Reasons);
                if (section.AvailableSeats <= 0) issues.Add(new("SECTION_FULL", $"{section.SectionCode} has no available seats."));
                selected.Add(new(section.Id, course.Code, course.Title, course.Credits, course.Category,
                    section.SectionCode, section.Days, section.StartLocal, section.EndLocal, section.Timezone, section.AvailableSeats, false));
            }
            else
            {
                var course = context.Courses.SingleOrDefault(c => $"tentative:{c.Code}" == id);
                if (course is null) { issues.Add(new("COURSE_INVALID", "An unknown tentative course was selected.")); continue; }
                issues.AddRange(eligibility.Evaluate(context, course.Code).Reasons);
                selected.Add(new(id, course.Code, course.Title, course.Credits, course.Category, null, [], null, null, context.Term.Timezone, null, true));
            }
        }
        if (selected.Select(s => s.CourseCode).Distinct().Count() != selected.Count)
            issues.Add(new("DUPLICATE_COURSE", "Select at most one section of each course."));
        if (context.Term.IsPublished && issues.All(i => i.Code != "CATALOG_INVALID"))
        {
            var sections = context.Sections.Where(s => ids.Contains(s.Id)).ToArray();
            for (var i = 0; i < sections.Length; i++)
                for (var j = i + 1; j < sections.Length; j++)
                    if (SchedulingRules.Conflicts(sections[i], sections[j])) issues.Add(new("TIME_CONFLICT", $"{sections[i].SectionCode} overlaps {sections[j].SectionCode}."));
        }
        var credits = selected.Sum(s => s.Credits);
        if (credits < request.MinCredits || credits > request.MaxCredits)
            issues.Add(new("CREDIT_RANGE", $"Selected {credits} credits; the requested range is {request.MinCredits}–{request.MaxCredits}."));
        return new(issues.Count == 0 ? "FEASIBLE" : "INVALID", context.CatalogVersion, request.TermCode,
            !context.Term.IsPublished, credits, selected.ToArray(), issues.ToArray());
    }
}
