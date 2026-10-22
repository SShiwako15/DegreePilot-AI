using DegreePilot.Api.Domain;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DegreePilot.Api.Application;

public sealed class AdvisorService(AcademicDbContext db, AcademicQueries academic, PlanningService planning,
    EligibilityEngine eligibility, PolicyRetrieval retrieval, IAdvisorClient client, TimeProvider clock)
{
    public async Task<ChatResult?> AnswerAsync(string actor, ChatRequest request, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == actor, ct);
        if (student is null) return null;
        var version = student.CatalogVersion;
        ChatResult Stop(string status, string answer) => new(status, version, answer, [], [AdvisorRules.Disclaimer], []);
        if (request.CatalogVersion is not null && request.CatalogVersion != version)
            return Stop("ABSTAIN", "The requested catalog does not match your pinned catalog. No other edition was consulted.");
        CourseDefinition[] catalog;
        try
        {
            catalog = (await db.Courses.AsNoTracking().Where(c => c.CatalogVersion == version).ToArrayAsync(ct))
                .Select(FixtureReader.ToDomain).ToArray();
        }
        catch (JsonException) { return Stop("ABSTAIN", "Your pinned catalog needs attention before I can explain its rules."); }
        var intent = AdvisorRules.Route(request, catalog);
        if (intent.Name == "refused") return Stop("REFUSED", "I cannot change records, override requirements, reveal secrets, or follow instructions that conflict with the advising rules. Your academic records are unchanged.");
        if (intent.Name == "unsupported") return Stop("ABSTAIN", "The approved demonstration policies do not support that question. Please ask an academic advisor; transfer equivalency and other undocumented rules cannot be inferred.");
        var evidence = await retrieval.ReadAsync(version, intent.SourceIds, ct);
        if (evidence.Length == 0 || !await retrieval.RulesAreApprovedAsync(version, ct))
            return Stop("ABSTAIN", "Approved evidence or its catalog rules are missing, ambiguous, or changed. I cannot provide a grounded answer.");
        if (!retrieval.ExplanationsAreReviewed(evidence))
            return Stop("ABSTAIN", "The revised policy text has not been checked against the pinned deterministic rules for AI explanations. Ask an advisor to review the revised source.");
        var facts = new List<string>();
        DegreeAudit? audited = null;
        var checkedEligibility = new List<Eligibility>();
        string? checkedOptions = null;
        string? checkedTerm = null;
        if (intent.Name is "degree" or "grades")
        {
            var audit = await academic.AuditAsync(actor);
            if (audit?.Status != "valid") return Stop("ABSTAIN", "The pinned catalog or academic record is invalid. No graduation determination can be explained.");
            if (intent.Name == "degree")
            {
                facts.Add($"You have {audit.EarnedCredits} of the required {audit.RequiredCredits} unique earned credits. The deterministic audit shows that degree requirements are {(audit.DegreeComplete == true ? "complete" : "not yet complete")}.");
                facts.Add($"You need at least {Math.Max(0, audit.RequiredCredits!.Value - audit.EarnedCredits!.Value)} more unique earned credits toward the total minimum; every category and required core course must also be satisfied.");
                facts.Add("Credit categories: " + string.Join("; ", audit.Buckets.Select(b => $"{b.Label}: {b.Earned} of {b.Required}; {b.Remaining} remaining")) + ".");
                var remaining = audit.CoreRequirements.Where(c => !c.Complete).Select(c => $"{c.Code} {c.Title}").ToArray();
                facts.Add(remaining.Length == 0 ? "All required core courses are complete." : "Required core courses still to complete: " + string.Join(", ", remaining) + ".");
            }
            else facts.Add(audit.Gpa is null ? "Your cumulative GPA is unavailable." : $"Your cumulative GPA is {audit.Gpa:0.00}; it includes all letter-graded attempts, including repeats and D/F grades.");
            audited = audit;
        }
        if (intent.Name == "options" || intent.Name == "eligibility" && intent.CourseCode is not null)
        {
            var targets = intent.CourseCodes ?? (intent.CourseCode is null ? [] : [intent.CourseCode]);
            if (targets.Length > 4) return Stop("ANSWERED", "Please ask about up to four courses at a time so I can explain each prerequisite clearly.");
            var unknown = targets.Where(code => !catalog.Any(c => c.Code == code)).ToArray();
            if (unknown.Length > 0) return Stop("ABSTAIN", $"I could not find {string.Join(", ", unknown)} in your pinned catalog. Please check the course code or use its full catalog title.");
            var termCode = request.TermCode;
            if (termCode is null)
            {
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);
                termCode = await db.Terms.AsNoTracking().Where(t => t.StartsOn > today).OrderBy(t => t.StartsOn).Select(t => t.Code).FirstOrDefaultAsync(ct);
            }
            if (termCode is null) return Stop("ABSTAIN", "Please select a supported planning term before asking about eligibility.");
            var context = await planning.LoadAsync(actor, termCode, ct);
            if (context is null) return Stop("ABSTAIN", "The requested planning term is unavailable.");
            if (eligibility.ValidateContext(context).Length > 0) return Stop("ABSTAIN", "Eligibility cannot be explained because the pinned catalog or source record is invalid.");
            if (intent.Name == "options")
            {
                var options = eligibility.Options(context);
                checkedOptions = JsonSerializer.Serialize(options);
                var available = options.DistinctBy(o => o.CourseCode).OrderBy(o => o.CourseCode).ToArray();
                facts.Add($"For {termCode}, {available.Length} courses meet your prerequisites and have available offerings. These are individual choices; a combined timetable must still be checked for time conflicts and credit limits.");
                foreach (var chunk in available.Chunk(8))
                    facts.Add("Eligible courses: " + string.Join("; ", chunk.Select(o => $"{o.CourseCode} {o.Title} ({o.Credits} credits)")) + ".");
                if (available.Length == 0) facts.Add("No eligible course offerings are currently available for this term. Try another term or review your missing prerequisites in the course catalog.");
            }
            foreach (var code in targets)
            {
                var course = context.Courses.Single(c => c.Code == code);
                var result = eligibility.Evaluate(context, code);
                var groups = course.PrerequisiteGroups;
                facts.Add($"For {termCode}, {result.CourseCode} is {result.Status.Replace('_', ' ')}. {course.Title} prerequisites: " +
                    (groups.Length == 0 ? "no course prerequisites." : string.Join(" AND ", groups.Select(g => "(" + string.Join(" OR ", g) + ")")) + "; complete each group with C or better before the target term."));
                if (result.Reasons.Length > 0) facts.Add(string.Join(" ", result.Reasons.Select(r => r.Message)));
                checkedEligibility.Add(result);
            }
            if (!context.Term.IsPublished) facts.Add("This term is tentative. Published sections, seats, and a conflict-free timetable are not confirmed.");
            checkedTerm = termCode;
        }
        if (facts.Count > 12 || facts.Any(f => f.Length > 1000)) return Stop("ABSTAIN", "Please narrow the question so the complete academic explanation fits the supported response budget.");
        var drafts = AdvisorRules.BuildDrafts(facts.ToArray(), evidence);
        if (drafts.Length == 0) return Stop("ABSTAIN", "The academic explanation exceeds the supported response budget. Please ask a narrower question.");
        var availability = await client.StatusAsync(ct);
        if (!availability.Available) return Stop("UNAVAILABLE", "AI advising is unavailable. Your degree audit and planner remain available.");
        var envelope = new AdvisorEnvelope(version, AdvisorRules.Sanitize(request.Question), intent.Name, facts.ToArray(), evidence, drafts);
        var reply = await client.AnswerAsync(envelope, ct);
        var matched = drafts.SingleOrDefault(d => AdvisorRules.Matches(reply, d));
        if (matched is null) return Stop(reply.Status == "UNAVAILABLE" ? "UNAVAILABLE" : "ABSTAIN",
            AdvisorRules.ProviderFailureMessage(reply.Reason));
        // Source approval or grades may change while a provider request is in flight.
        var currentVersion = await db.Students.AsNoTracking().Where(s => s.UserId == actor).Select(s => s.CatalogVersion).SingleOrDefaultAsync(ct);
        var currentEvidence = await retrieval.ReadAsync(version, intent.SourceIds, ct);
        var changed = currentVersion != version || !currentEvidence.SequenceEqual(evidence) ||
            !await retrieval.RulesAreApprovedAsync(version, ct);
        if (audited is not null)
        {
            var current = await academic.AuditAsync(actor);
            changed |= current is null || current.Status != "valid" || current.EarnedCredits != audited.EarnedCredits ||
                current.RequiredCredits != audited.RequiredCredits || current.DegreeComplete != audited.DegreeComplete ||
                current.Gpa != audited.Gpa || !current.Buckets.SequenceEqual(audited.Buckets) ||
                !current.CoreRequirements.SequenceEqual(audited.CoreRequirements);
        }
        if (checkedTerm is not null)
        {
            var current = await planning.LoadAsync(actor, checkedTerm!, ct);
            changed |= current is null || checkedEligibility.Any(result =>
                JsonSerializer.Serialize(eligibility.Evaluate(current, result.CourseCode)) != JsonSerializer.Serialize(result)) ||
                checkedOptions is not null && JsonSerializer.Serialize(eligibility.Options(current)) != checkedOptions;
        }
        if (changed) return Stop("ABSTAIN", "The academic facts or approved sources changed while preparing this reply. Ask again to use current information.");
        // Use our approved strings and source metadata, never provider-supplied quotes, links, or claims.
        return new("ANSWERED", version, matched.Answer, evidence, [AdvisorRules.Disclaimer, AdvisorRules.Limited], facts.ToArray());
    }
}
