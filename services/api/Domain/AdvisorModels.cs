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
public sealed record ProviderReply(string Status, string? Answer, string[]? CitedSectionIds, string[]? Limitations,
    string? Reason = null);
public sealed record ChatResult(string Status, string CatalogVersion, string Answer, PolicyCitation[] Citations,
    string[] Limitations, string[] Facts);
public sealed record QuestionIntent(string Name, string[] SourceIds, string? CourseCode = null, string[]? CourseCodes = null);

public static partial class AdvisorRules
{
    public const string Disclaimer = "Fictional demonstration policies; informational guidance only. Verify real requirements with an academic advisor.";
    public const string Limited = "Replies use only the approved evidence and current deterministic facts supplied for this question.";
    public static string ProviderFailureMessage(string? reason) => reason switch
    {
        "PROVIDER_AUTH" => "The AI provider rejected its credentials or model permissions. An administrator must check the private provider configuration. Deterministic features remain available.",
        "PROVIDER_RATE_LIMIT" => "The AI provider's quota or rate limit was reached. Check API billing/limits or try again later. Deterministic features remain available.",
        "PROVIDER_TIMEOUT" or "PROVIDER_CONNECTION" => "The AI provider could not be reached within the response deadline. Try again later; deterministic features remain available.",
        "MODEL_INCOMPLETE" => "The AI provider returned an incomplete response. No partial academic advice was accepted. Browse the approved sources or try a narrower question.",
        "MODEL_REFUSED" => "The AI provider did not select an approved explanation for this question. Browse the approved sources or ask an academic advisor.",
        "MODEL_SELECTION_INVALID" or "MODEL_OUTPUT_INVALID" or "GROUNDING_REJECTED" => "The AI response failed validation. No unverified answer or citations were accepted. Browse the approved sources or ask an academic advisor.",
        _ => "No trusted answer was returned. The response was unavailable, refused, incomplete, or failed grounding/citation validation."
    };
    public static GroundedDraft[] BuildDrafts(string[] facts, PolicyCitation[] evidence)
    {
        var citations = evidence.Select(e => e.PolicySectionId).ToArray();
        string Compose(IEnumerable<string> selectedFacts)
        {
            var answer = string.Join("\n\n", selectedFacts);
            // Preserve every deterministic fact. Full source excerpts remain in citations even
            // when they cannot all fit in the shared 6000-character reply budget.
            foreach (var source in evidence)
            {
                var candidate = string.Join("\n\n", new[] { answer, source.Quote }.Where(s => s.Length > 0));
                if (candidate.Length <= 6000) answer = candidate;
            }
            return answer;
        }
        return new[] { Compose(facts) }
            .Where(s => s.Length is > 0 and <= 6000).Distinct().Select(s => new GroundedDraft(s, citations)).ToArray();
    }
    [GeneratedRegex(@"\b[A-Z]{2,5}[\s-]?\d{3}\b", RegexOptions.IgnoreCase)]
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

    public static QuestionIntent Route(ChatRequest request, CourseDefinition[]? catalog = null)
    {
        var current = request.Question.ToLowerInvariant();
        if (UnsafeActions().IsMatch(current)) return new("refused", []);
        QuestionIntent Classify(string q)
        {
            bool Has(params string[] words) => words.Any(q.Contains);
            if (Has("transfer", "tuition", "scholarship", "financial aid", "admission", "medical"))
                return new("unsupported", []);
            if (Has("grade", "repeat", "passing", "gpa")) return new("grades", ["CAT-CORE-GRADES"]);
            var codes = CourseCodes().Matches(q).Select(m => Regex.Replace(m.Value, @"[\s-]", "").ToUpperInvariant()).ToList();
            foreach (var course in catalog ?? [])
            {
                var title = course.Title.ToLowerInvariant();
                if (Regex.IsMatch(q, @"\b" + Regex.Escape(title) + @"\b") ||
                    title.EndsWith('s') && Regex.IsMatch(q, @"\b" + Regex.Escape(title[..^1]) + @"\b"))
                    codes.Add(course.Code);
            }
            codes = codes.Distinct().ToList();
            if (codes.Count > 0)
            {
                var ids = codes.Count == 1 && (codes[0] is "CS301" or "CS350" or "CS401")
                    ? new[] { $"CAT-PREREQ-{codes[0]}" } : ["CAT-PREREQUISITES"];
                return new("eligibility", ids, codes[0], codes.ToArray());
            }
            if (Has("prereq", "pre-req", "pre req", "eligible", "eligibility", "before taking", "before i take"))
                return new("eligibility", ["CAT-PREREQUISITES"]);
            if (Has("overlap", "conflict", "adjacent", "timezone", "same time", "clash"))
                return new("conflicts", ["PLN-SECTION-CONFLICT"]);
            if (Has("graduat", "degree", "earned", "progress", "remaining", "still need", "left to", "on track", "how far", "what is left", "what's left", "am i done"))
                return new("degree", ["CAT-BSCS-TOTAL", "CAT-GRADUATION"]);
            if (Has("what classes", "which classes", "what courses", "which courses", "classes can i", "courses can i", "recommend courses", "recommend classes", "suggest classes", "study next", "take next", "choose courses"))
                return new("options", ["CAT-PREREQUISITES", "PLN-FUTURE-OFFERINGS"]);
            if (Has("tentative", "future", "unpublished", "projected", "offering"))
                return new("future", ["PLN-FUTURE-OFFERINGS"]);
            if (Has("credit load", "credit range", "how many credits should", "how many credits can", "12", "18"))
                return new("load", ["PLN-CREDIT-LOAD"]);
            if (Has("graduat", "degree", "earned", "credit", "progress", "remaining", "still need", "left to", "on track", "how far", "what is left", "what's left", "am i done", "core courses", "requirements"))
                return new("degree", ["CAT-BSCS-TOTAL", "CAT-GRADUATION"]);
            if (Has("semester", "plan", "schedule", "timetable", "registration"))
                return new("load", ["PLN-CREDIT-LOAD", "PLN-SECTION-CONFLICT", "PLN-FUTURE-OFFERINGS"]);
            if (Has("advisor", "enrollment", "artificial intelligence") || Regex.IsMatch(q, @"\bai\b"))
                return new("boundary", ["PLN-AI-ROLE"]);
            return new("unsupported", []);
        }
        var intent = Classify(current);
        if ((intent.Name == "unsupported" || intent.Name == "eligibility" && intent.CourseCode is null) &&
            Regex.IsMatch(current, @"^(why|what about|can you explain|tell me more|and |how so|am i eligible)"))
        {
            foreach (var prior in (request.PreviousQuestions ?? []).Reverse())
            {
                var contextual = Classify(prior.ToLowerInvariant() + " " + current);
                if (contextual.Name != "unsupported") return contextual;
            }
        }
        return intent;
    }

    public static bool Matches(ProviderReply reply, GroundedDraft draft) => reply.Status == "ANSWERED" &&
        reply.Answer == draft.Answer && reply.CitedSectionIds is { Length: > 0 } &&
        reply.CitedSectionIds.Distinct().Count() == reply.CitedSectionIds.Length &&
        reply.CitedSectionIds.Order().SequenceEqual(draft.CitedSectionIds.Order()) &&
        reply.Limitations is { Length: <= 2 } && reply.Limitations.All(l => l is Disclaimer or Limited);
}
