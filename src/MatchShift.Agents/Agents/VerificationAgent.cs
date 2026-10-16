using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MatchShift.Agents.Narrative;
using MatchShift.Core.Insights;

namespace MatchShift.Agents.Agents;

[JsonConverter(typeof(JsonStringEnumConverter<VerificationStatus>))]
public enum VerificationStatus
{
    Approved,
    Revised,
    Rejected
}

public sealed record VerificationIssue(string Sentence, string Problem);

public sealed record VerificationResult(VerificationStatus Status, IReadOnlyList<VerificationIssue> Issues, int NumbersChecked)
{
    public bool Approved => Status == VerificationStatus.Approved;
}

/// <summary>
/// VERIFICATION AGENT. Used by NarrationService for AI output and by AgentPipeline.
/// Live snapshots first pass EvidenceGate. Provider selections additionally require exact canonical rendering.
///
/// Input: generated sentences + the structured evidence they claim to describe.
/// Checks:
///   1. Every number in the text matches a value in the evidence/slots (or a definitional
///      constant such as 90 minutes or the 0-100 index scale), within rounding tolerance.
///   2. Reject club names supplied in otherClubNames (empty by default).
///   3. No claims of intent or certainty the data cannot support ("deliberately", "the manager decided"...).
/// Output: Approved, or Rejected with issues. Failure behaviour: callers fall back to the
/// deterministic template text, which is itself verified.
/// </summary>
public sealed partial class VerificationAgent
{
    /// <summary>Numbers that are definitions, not statistics: index scales, match length, halves.</summary>
    public static readonly IReadOnlySet<double> DefinitionalConstants = new HashSet<double> { 0, 1, 45, 50, 90, 100 };

    private static readonly string[] IntentClaims =
    [
        "deliberately", "on purpose", "the manager decided", "the coach decided", "instructed", "game plan was", "clearly intended",
        "délibérément", "exprès", "l'entraîneur a décidé", "a ordonné",
        "deliberadamente", "a propósito", "el entrenador decidió", "ordenó",
    ];

    private readonly IReadOnlyCollection<string> _otherClubNames;

    public VerificationAgent(IEnumerable<string>? otherClubNames = null) =>
        _otherClubNames = otherClubNames?.ToList() ?? [];

    public string Name => "Verification Agent";

    public VerificationResult Verify(IEnumerable<string> sentences, IEnumerable<double> allowedNumbers)
    {
        var allowed = allowedNumbers.Concat(DefinitionalConstants).ToList();
        var issues = new List<VerificationIssue>();
        var checkedCount = 0;

        foreach (var sentence in sentences.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            foreach (var number in ExtractNumbers(sentence))
            {
                checkedCount++;
                if (!allowed.Any(a => Matches(number, a)))
                    issues.Add(new VerificationIssue(sentence, $"Number {number.ToString(CultureInfo.InvariantCulture)} is not supported by the evidence."));
            }

            var lower = sentence.ToLowerInvariant();
            foreach (var claim in IntentClaims.Where(c => lower.Contains(c, StringComparison.Ordinal)))
                issues.Add(new VerificationIssue(sentence, $"Unsupported claim of intent ('{claim}')."));

            foreach (var club in _otherClubNames.Where(c => sentence.Contains(c, StringComparison.OrdinalIgnoreCase)))
                issues.Add(new VerificationIssue(sentence, $"Mentions '{club}', which is not in this match."));
        }

        return new VerificationResult(issues.Count == 0 ? VerificationStatus.Approved : VerificationStatus.Rejected, issues, checkedCount);
    }

    /// <summary>Verify a rendered insight against the slots it may draw on.</summary>
    public VerificationResult VerifyInsight(InsightNarrative narrative, Insight insight, NarrativeSlots slots) =>
        Verify(narrative.Sentences(), AllowedNumbers(insight, slots));

    public static IEnumerable<double> AllowedNumbers(Insight insight, NarrativeSlots slots)
    {
        foreach (var value in slots.Values.Values)
            foreach (var n in ExtractNumbers(value)) yield return n;
        foreach (var e in insight.Evidence)
        {
            yield return e.RecentValue;
            if (e.BaselineValue is { } b) yield return b;
            if (e.Difference is { } d) yield return Math.Abs(d);
            if (e.Threshold is { } t) yield return Math.Abs(t);
            yield return e.SampleSize;
            if (e.Unit == MetricUnit.Share)
            {
                yield return Math.Round(e.RecentValue * 100);
                if (e.BaselineValue is { } bs) yield return Math.Round(bs * 100);
                if (e.Difference is { } ds) yield return Math.Round(Math.Abs(ds) * 100);
                if (e.Threshold is { } ts) yield return Math.Round(Math.Abs(ts) * 100);
            }
            if (e.RecentWindow is { } rw) { yield return rw.FromMinute; yield return rw.ToMinute; yield return Math.Round((rw.ToSecond - rw.FromSecond) / 60.0); }
            if (e.BaselineWindow is { } bw) { yield return bw.FromMinute; yield return bw.ToMinute; yield return Math.Round((bw.ToSecond - bw.FromSecond) / 60.0); }
        }
        yield return insight.DisplayMinute;
    }

    public static bool Matches(double claimed, double allowed)
    {
        if (Math.Abs(claimed - allowed) < 1e-9) return true;
        // A rounded presentation of a precise value (e.g. 0.36 vs 0.357, 62 vs 61.8).
        var tolerance = Math.Max(0.5, Math.Abs(allowed) * 0.01);
        if (Math.Abs(allowed) < 10 && claimed % 1 != 0) tolerance = 0.006;
        return Math.Abs(claimed - allowed) <= tolerance;
    }

    /// <summary>Extract numbers written as 62, 0.36, 0,36, 31%, 31 %, +37 ... (culture-agnostic).</summary>
    public static IReadOnlyList<double> ExtractNumbers(string text)
    {
        var result = new List<double>();
        foreach (Match m in NumberPattern().Matches(text))
        {
            var token = m.Value.Replace(',', '.');
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) result.Add(v);
        }
        return result;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])\d+(?:[.,]\d+)?")]
    private static partial Regex NumberPattern();
}
