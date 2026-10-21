using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DegreePilot.Api.Domain;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatRequest(string Question, string? CatalogVersion = null, string? TermCode = null,
    string[]? PreviousQuestions = null);
public sealed record AdvisorStatus(bool Available, string Reason);
public sealed record PolicyCitation(string PolicySectionId, string CatalogVersion, string DocumentTitle,
    int DocumentVersion, string Title, string Quote);
public sealed record GroundedDraft(string Answer, string[] CitedSectionIds);
public sealed record AdvisorEnvelope(string CatalogVersion, string Question, string Intent,
    string[] Facts, PolicyCitation[] Evidence, GroundedDraft[] Drafts);
public sealed record ProviderReply(string Status, string? Answer, string[]? CitedSectionIds, string[]? Limitations);
public sealed record ChatResult(string Status, string CatalogVersion, string Answer, PolicyCitation[] Citations,
    string[] Limitations, string[] Facts);
public sealed record QuestionIntent(string Name, string[] SourceIds, string? CourseCode = null);

public static partial class AdvisorRules
{
    public const string Disclaimer = "Fictional demonstration policies; informational guidance only. Verify real requirements with an academic advisor.";
    public const string Limited = "Replies use only the approved evidence and current deterministic facts supplied for this question.";
    public static GroundedDraft[] BuildDrafts(string[] facts, PolicyCitation[] evidence, bool degree)
    {
        var citations = evidence.Select(e => e.PolicySectionId).ToArray();
        string Compose(IEnumerable<string> selectedFacts)
        {
            var answer = string.Join("\n\n", selectedFacts);
            // Preserve every deterministic fact. Full source excerpts remain in citations even
            // when they cannot all fit in the shared Python/Responses 6000-character budget.
            foreach (var source in evidence)
            {
                var candidate = string.Join("\n\n", new[] { answer, source.Quote }.Where(s => s.Length > 0));
                if (candidate.Length <= 6000) answer = candidate;
            }
            return answer;
        }
        return new[] { Compose(facts.Take(degree ? 1 : facts.Length)), Compose(facts) }
            .Where(s => s.Length is > 0 and <= 6000).Distinct().Select(s => new GroundedDraft(s, citations)).ToArray();
    }
    [GeneratedRegex(@"\b(?:CS|MATH|STAT|GEN|ELEC)\d{3}\b", RegexOptions.IgnoreCase)]
    private static partial Regex CourseCodes();
    [GeneratedRegex(@"ignore.{0,50}(previous|instruction|polic)|system prompt|reveal.{0,40}(key|secret|token)|exfiltrat|(?:change|edit|update|override|delete|alter).{0,60}(grade|transcript|credit|record)|(?:sql|execute code)", RegexOptions.IgnoreCase)]
    private static partial Regex UnsafeActions();
    [GeneratedRegex(@"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|\bsk-[A-Za-z0-9_-]+|\bstudent-[A-Za-z0-9-]+", RegexOptions.IgnoreCase)]
    private static partial Regex PrivateText();
    public static string Sanitize(string text) => PrivateText().Replace(text.Trim(), "[redacted]");
    public static bool Valid(ChatRequest request) => !string.IsNullOrWhiteSpace(request.Question) &&
        request.Question.Length <= 1000 && request.PreviousQuestions is not { Length: > 4 } &&
        request.PreviousQuestions?.Any(q => string.IsNullOrWhiteSpace(q) || q.Length > 1000) != true &&
        request.CatalogVersion is not { Length: > 100 } &&
        (request.TermCode is null || SchedulingRules.ValidTermCode(request.TermCode));

    public static QuestionIntent Route(ChatRequest request)
    {
        var current = request.Question.ToLowerInvariant();
        if (UnsafeActions().IsMatch(current)) return new("refused", []);
        QuestionIntent Classify(string q)
        {
            if (new[] { "transfer", "tuition", "scholarship", "financial aid", "admission", "medical" }.Any(q.Contains))
                return new("unsupported", []);
            var codes = CourseCodes().Matches(q).Select(m => m.Value.ToUpperInvariant()).Distinct().ToArray();
            if (codes.Length == 1 && codes[0] is "CS301" or "CS401")
                return new("eligibility", [$"CAT-PREREQ-{codes[0]}"], codes[0]);
            if (codes.Length > 0 && !new[] { "grade", "repeat", "passing" }.Any(q.Contains)) return new("unsupported", []);
            if (new[] { "grade", "repeat", "passing", "gpa" }.Any(q.Contains)) return new("grades", ["CAT-CORE-GRADES"]);
            if (new[] { "graduat", "degree", "earned", "total credit" }.Any(q.Contains))
                return new("degree", ["CAT-BSCS-TOTAL", "CAT-GRADUATION"]);
            if (new[] { "overlap", "conflict", "adjacent", "tuesday", "timezone" }.Any(q.Contains)) return new("conflicts", ["PLN-SECTION-CONFLICT"]);
            if (new[] { "tentative", "future", "unpublished", "projected" }.Any(q.Contains)) return new("future", ["PLN-FUTURE-OFFERINGS"]);
            if (new[] { "semester", "credit load", "credit range", "12", "18" }.Any(q.Contains)) return new("load", ["PLN-CREDIT-LOAD"]);
            if (new[] { "ai", "advisor", "enrollment" }.Any(q.Contains)) return new("boundary", ["PLN-AI-ROLE"]);
            return new("unsupported", []);
        }
        var intent = Classify(current);
        if (intent.Name == "unsupported" && Regex.IsMatch(current, @"^(why|what about|can you explain|tell me more|and |how so)"))
            intent = Classify(string.Join(' ', (request.PreviousQuestions ?? []).TakeLast(1)).ToLowerInvariant() + " " + current);
        return intent;
    }

    public static bool Matches(ProviderReply reply, GroundedDraft draft) => reply.Status == "ANSWERED" &&
        reply.Answer == draft.Answer && reply.CitedSectionIds is { Length: > 0 } &&
        reply.CitedSectionIds.Distinct().Count() == reply.CitedSectionIds.Length &&
        reply.CitedSectionIds.Order().SequenceEqual(draft.CitedSectionIds.Order()) &&
        reply.Limitations is { Length: <= 2 } && reply.Limitations.All(l => l is Disclaimer or Limited);
}
