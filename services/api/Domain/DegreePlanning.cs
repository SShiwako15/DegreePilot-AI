using System.Text.Json.Serialization;

namespace DegreePilot.Api.Domain;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DegreePlanRequest(string StartTermCode, int HorizonTerms = 8, int MinCredits = 12, int MaxCredits = 18);
public sealed record DegreeContext(string CatalogVersion, ProgramDefinition? Program, CourseDefinition[] Courses,
    Attempt[] Attempts, PlanningContext[] Terms);
public sealed record DegreeSolverCourse(string Code, int Credits, string Category, string[][] PrerequisiteGroups);
public sealed record DegreeSolverTerm(string Code, bool Published, SolverOption[] Options);
public sealed record DegreeSolverRequest(string CatalogVersion, DegreeSolverCourse[] Courses, string[] Earned,
    string[] Passed, string[] RequiredCore, Dictionary<string, int> CategoryMinimums, int MinTotalCredits,
    DegreeSolverTerm[] Terms, int MinCredits, int MaxCredits, double MaxTimeSeconds = 5);
public sealed record DegreeSelection(string TermCode, string[] SelectionIds);
public sealed record DegreeSolverReply(string Status, DegreeSelection[] Selections, bool Optimal = false,
    int? CompletionTerms = null, int? CompletionLowerBound = null);
public sealed record DegreePlanResult(string Status, string CatalogVersion, string InputFingerprint,
    PlanResult[] Terms, RuleIssue[] Issues, bool Optimal = false, int? CompletionTerms = null,
    int? CompletionLowerBound = null, bool ConditionalDegreeComplete = false, Guid? Id = null);

public sealed class DegreePlanValidator(AuditEngine audit, PlanValidator plans)
{
    public DegreePlanResult Validate(DegreeContext context, DegreePlanRequest request, DegreeSolverReply proposal, string fingerprint)
    {
        DegreePlanResult Invalid(params RuleIssue[] issues) => new("ERROR", context.CatalogVersion, fingerprint, [], issues);
        if (proposal.Status != "FEASIBLE") return Invalid(new RuleIssue("DEGREE_RESULT_INVALID", "Only feasible selections can be validated."));
        if (proposal.Selections is null || proposal.Selections.Length > context.Terms.Length ||
            proposal.Selections.Any(s => s is null || s.SelectionIds is null || s.SelectionIds.Length > 30) ||
            proposal.Selections.Select(s => s.TermCode).Distinct().Count() != proposal.Selections.Length ||
            proposal.Selections.Any(s => !context.Terms.Any(t => t.Term.Code == s.TermCode)))
            return Invalid(new RuleIssue("DEGREE_RESULT_INVALID", "The proposal contains invalid or duplicate term selections."));
        var projected = context.Attempts.ToList();
        var scheduled = new HashSet<string>();
        var results = new List<PlanResult>();
        var finish = 0;
        for (var index = 0; index < context.Terms.Length; index++)
        {
            var term = context.Terms[index];
            var selected = proposal.Selections.SingleOrDefault(s => s.TermCode == term.Term.Code)?.SelectionIds ?? [];
            if (selected.Length == 0) continue; // Explicit idle semesters are allowed while awaiting an offering.
            var plan = plans.Validate(term with { Attempts = projected.ToArray() },
                new(term.Term.Code, request.MinCredits, request.MaxCredits), selected);
            if (plan.Status != "FEASIBLE") return Invalid([new("DEGREE_RESULT_INVALID", "A semester failed independent validation."), .. plan.Issues]);
            foreach (var course in plan.Selections)
            {
                if (!scheduled.Add(course.CourseCode)) return Invalid(new RuleIssue("DUPLICATE_COURSE", "A course appears in multiple semesters."));
                projected.Add(new(course.CourseCode, term.Term.Code, "C"));
            }
            results.Add(plan);
            finish = index + 1;
        }
        var final = audit.Calculate(context.Program, context.Courses, projected, context.CatalogVersion);
        if (final.Status != "valid" || final.DegreeComplete != true || proposal.CompletionTerms != finish ||
            proposal.CompletionLowerBound is < 0 || proposal.CompletionLowerBound > finish ||
            (proposal.Optimal && proposal.CompletionLowerBound != finish))
            return Invalid(new RuleIssue("DEGREE_INCOMPLETE", "The conditional audit, completion term, or reported bound is invalid."));
        return new("FEASIBLE", context.CatalogVersion, fingerprint, results.ToArray(), [], proposal.Optimal,
            finish, proposal.CompletionLowerBound, true);
    }
}
