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
        var intent = AdvisorRules.Route(request);
        if (intent.Name == "refused") return Stop("REFUSED", "I cannot change records, override requirements, reveal secrets, or follow instructions that conflict with the advising rules. Your academic records are unchanged.");
        if (intent.Name == "unsupported") return Stop("ABSTAIN", "The approved demonstration policies do not support that question. Please ask an academic advisor; transfer equivalency and other undocumented rules cannot be inferred.");
        var evidence = await retrieval.ReadAsync(version, intent.SourceIds, ct);
        if (evidence.Length == 0 || !await retrieval.RulesAreApprovedAsync(version, ct))
            return Stop("ABSTAIN", "Approved evidence or its catalog rules are missing, ambiguous, or changed. I cannot provide a grounded answer.");
        if (!retrieval.ExplanationsAreReviewed(evidence))
            return Stop("ABSTAIN", "The revised policy text has not been checked against the pinned deterministic rules for AI explanations. It remains readable in the policy library; ask an advisor to review it.");
        var facts = new List<string>();
        DegreeAudit? audited = null;
        Eligibility? checkedEligibility = null;
        string? checkedTerm = null;
        if (intent.Name == "degree")
        {
            var audit = await academic.AuditAsync(actor);
            if (audit?.Status != "valid") return Stop("ABSTAIN", "The pinned catalog or academic record is invalid. No graduation determination can be explained.");
            facts.Add($"You have {audit.EarnedCredits} of the required {audit.RequiredCredits} unique earned credits. The deterministic audit shows that degree requirements are {(audit.DegreeComplete == true ? "complete" : "not yet complete")}.");
            facts.Add("Credit categories: " + string.Join("; ", audit.Buckets.Select(b => $"{b.Label}: {b.Earned} of {b.Required}")) + ".");
            audited = audit;
        }
        if (intent.Name == "eligibility")
        {
            var termCode = request.TermCode;
            if (termCode is null)
            {
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);
                termCode = await db.Terms.AsNoTracking().Where(t => t.StartsOn > today).OrderBy(t => t.StartsOn).Select(t => t.Code).FirstOrDefaultAsync(ct);
            }
            if (termCode is null) return Stop("ABSTAIN", "Please select a supported planning term before asking about eligibility.");
            var context = await planning.LoadAsync(actor, termCode, ct);
            if (context is null) return Stop("ABSTAIN", "The requested planning term is unavailable.");
            var result = eligibility.Evaluate(context, intent.CourseCode!);
            if (result.Status == "invalid") return Stop("ABSTAIN", "Eligibility cannot be explained because the pinned catalog or source record is invalid.");
            facts.Add($"For {termCode}, {result.CourseCode} is {result.Status.Replace('_', ' ')}.");
            facts.AddRange(result.Reasons.Select(r => r.Message));
            if (result.Tentative) facts.Add("This term is tentative. Published sections, seats, and a conflict-free timetable are not confirmed.");
            checkedEligibility = result;
            checkedTerm = termCode;
        }
        var drafts = AdvisorRules.BuildDrafts(facts.ToArray(), evidence, intent.Name == "degree");
        if (drafts.Length == 0) return Stop("ABSTAIN", "The academic explanation exceeds the supported response budget. Browse the source library or ask a narrower question.");
        var availability = await client.StatusAsync(ct);
        if (!availability.Available) return Stop("UNAVAILABLE", "AI advising is unavailable. You can still browse approved policies, use your degree audit, and validate plans.");
        var envelope = new AdvisorEnvelope(version, AdvisorRules.Sanitize(request.Question), intent.Name, facts.ToArray(), evidence, drafts);
        var reply = await client.AnswerAsync(envelope, ct);
        var matched = drafts.SingleOrDefault(d => AdvisorRules.Matches(reply, d));
        if (matched is null) return Stop(reply.Status == "UNAVAILABLE" ? "UNAVAILABLE" : "ABSTAIN",
            "No trusted answer was returned. The response was unavailable, refused, incomplete, or failed grounding/citation validation.");
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
                !current.Buckets.SequenceEqual(audited.Buckets);
        }
        if (checkedEligibility is not null)
        {
            var current = await planning.LoadAsync(actor, checkedTerm!, ct);
            changed |= current is null || JsonSerializer.Serialize(eligibility.Evaluate(current, intent.CourseCode!)) != JsonSerializer.Serialize(checkedEligibility);
        }
        if (changed) return Stop("ABSTAIN", "The academic facts or approved sources changed while preparing this reply. Ask again to use current information.");
        // Use our approved strings and source metadata, never provider-supplied quotes, links, or claims.
        return new("ANSWERED", version, matched.Answer, evidence, [AdvisorRules.Disclaimer, AdvisorRules.Limited], facts.ToArray());
    }
}
