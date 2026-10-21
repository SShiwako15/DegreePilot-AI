using System.Globalization;
using System.Text.RegularExpressions;

namespace DegreePilot.Api.Domain;

public static partial class SchedulingRules
{
    public static readonly string[] Weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri"];
    [GeneratedRegex(@"^[0-9]{4}-(SPRING|FALL)\z")]
    private static partial Regex TermPattern();
    public static bool ValidTermCode(string? code) => code is not null && TermPattern().IsMatch(code);
    public static int Minute(string clock) => TimeOnly.ParseExact(clock, "HH:mm", CultureInfo.InvariantCulture).Hour * 60
        + TimeOnly.ParseExact(clock, "HH:mm", CultureInfo.InvariantCulture).Minute;

    public static bool ValidTerm(TermDefinition term) => ValidTermCode(term.Code) &&
        term.Kind == (term.Code.EndsWith("SPRING", StringComparison.Ordinal) ? "Spring" : "Fall") &&
        term.StartsOn < term.EndsOn && term.StartsOn.Year.ToString(CultureInfo.InvariantCulture) == term.Code[..4] &&
        term.Timezone == "America/New_York";

    public static bool ValidSection(SectionDefinition section, TermDefinition term) =>
        section.TermCode == term.Code && section.Timezone == term.Timezone &&
        section.Days is { Length: > 0 } && section.Days.Distinct().Count() == section.Days.Length &&
        section.Days.All(Weekdays.Contains) && section.Capacity > 0 &&
        section.AvailableSeats >= 0 && section.AvailableSeats <= section.Capacity &&
        TimeOnly.TryParseExact(section.StartLocal, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) &&
        TimeOnly.TryParseExact(section.EndLocal, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) && start < end;

    public static bool Conflicts(SectionDefinition a, SectionDefinition b) => a.TermCode == b.TermCode &&
        a.Days.Intersect(b.Days).Any() && Minute(a.StartLocal) < Minute(b.EndLocal) && Minute(b.StartLocal) < Minute(a.EndLocal);

    public static RuleIssue[] ValidateRequest(PlanRequest request) =>
        !ValidTermCode(request.TermCode) || request.MinCredits < 1 || request.MaxCredits > 30 ||
        request.MinCredits > request.MaxCredits || request.PreferredDays is { Length: > 5 } || request.PreferredDays?.Any(day => !Weekdays.Contains(day)) == true
        ? [new("REQUEST_INVALID", "Choose a supported term, a credit range from 1 to 30, and valid preferred weekdays.")] : [];
}
