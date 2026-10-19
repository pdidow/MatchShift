using System.Globalization;
using System.Text.RegularExpressions;
using MatchShift.Agents.Workflow;

namespace MatchShift.Evaluation;

/// <summary>One deliberately bad variant of a real output, with the defect it carries.</summary>
public sealed record Mutation(string Id, string Description, string Text);

/// <summary>
/// Builds the adversarial set from REAL agent output: each mutation injects exactly one known defect into an otherwise
/// genuine text, so a quality layer that misses it cannot be excused by an unrealistic sentence.
/// </summary>
public static partial class Adversarial
{
    public static IReadOnlyList<Mutation> For(InsightContext c, string audienceText, string accessibleText, string? otherLanguageText, string? otherModeText)
    {
        var wrongScore = $"{c.Score.Home + 2}-{c.Score.Away}";
        var outsider = c.AllMatchPlayers.First(p => c.Players.All(q => q.Id != p.Id) && c.Players.All(q => Last(q.Name) != Last(p.Name)));
        var future = c.KnownEventSeconds.Where(kv => kv.Value > c.MatchSecond).OrderBy(kv => kv.Value).First().Key;
        var knownMinutes = c.DeterministicMetrics.SelectMany(m => new[] { m.BaselineWindow, m.CurrentWindow }).OfType<WindowFact>()
            .SelectMany(w => new[] { w.FromMinute, w.ToMinute }).Concat(c.SourceEvents.SelectMany(e => new[] { e.Second / 60, e.Second / 60 + 1 }))
            .Append(c.DisplayMinute).ToHashSet();
        var inventedMinute = Enumerable.Range(3, c.DisplayMinute - 3).First(m => !knownMinutes.Contains(m) && !knownMinutes.Contains(m - 1) && !knownMinutes.Contains(m + 1));
        var primary = c.DeterministicMetrics.First(m => m.IsPrimary);

        return
        [
            new("WRONG_SCORE", "States a score that is not the score at the insight time.", $"{audienceText} The score is now {wrongScore}."),
            new("UNSUPPORTED_PERCENTAGE", "Adds a percentage that no metric supports.", $"{audienceText} Possession rose by 17% in this spell."),
            new("INVENTED_PLAYER", "Names a squad player who is not involved in the evidence.", $"{audienceText} {outsider.Name} drove the change."),
            new("INVENTED_EVENT_ID", "Cites an event ID that does not exist.", $"{audienceText} See event {c.MatchId}-E99999."),
            new("FUTURE_EVENT", "Cites a real event that happens after the insight.", $"{audienceText} It continued with event {future}."),
            new("INVENTED_MINUTE", "States a match minute that is not grounded.", $"{audienceText} The change began at {inventedMinute} minutes."),
            new("PLAYER_COUNT", "Claims a player count that MatchShift never computed.", $"{audienceText} Eight key players were involved."),
            new("CAUSAL_CERTAINTY", "States an unsupported cause with certainty.", $"{audienceText} This definitely proves the coach's change caused it."),
            new("ACCESSIBILITY_CONTRADICTS", "Accessible text changes the primary metric value.", ChangePrimary(accessibleText, primary)),
            new("LANGUAGE_VALUE_CHANGE", "A translated variant changes the primary metric value.", ChangePrimary(otherLanguageText ?? audienceText, primary)),
            new("AUDIENCE_FACT_CHANGE", "A different audience mode changes the primary metric value.", ChangePrimary(otherModeText ?? audienceText, primary)),
        ];
    }

    /// <summary>Replaces the primary metric's displayed number with a wrong one, or appends a wrong value if it is not mentioned.</summary>
    public static string ChangePrimary(string text, MetricFact primary)
    {
        var shown = NumberPattern().Match(primary.DisplayCurrent);
        if (!shown.Success) return $"{text} {primary.Label} was 123.";
        var value = double.Parse(shown.Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var wrong = (value >= 30 ? value - 23 : value + 23).ToString("0.#", CultureInfo.InvariantCulture);
        var at = Regex.Match(text, $@"(?<![\d.,]){Regex.Escape(shown.Value)}(?![\d])");
        return at.Success ? text[..at.Index] + wrong + text[(at.Index + at.Length)..] : $"{text} {primary.Label}: {wrong}%.";
    }

    private static string Last(string name) => name.Split(' ').Last();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex NumberPattern();
}
