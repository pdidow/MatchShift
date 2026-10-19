using System.Globalization;
using System.Text.RegularExpressions;
using MatchShift.Agents.Agents;

namespace MatchShift.Agents.Workflow;

/// <summary>A deterministic finding about generated text. Codes are stable so tests and the UI can rely on them.</summary>
public sealed record GroundingIssue(string Agent, string Field, string Code, string Message);

public static class IssueCodes
{
    public const string UnknownEvidenceId = "UNKNOWN_EVIDENCE_ID";
    public const string FutureEvidence = "FUTURE_EVIDENCE";
    public const string NotSupportingEvidence = "NOT_SUPPORTING_EVIDENCE";
    public const string UnsupportedNumber = "UNSUPPORTED_NUMBER";
    public const string WrongScore = "WRONG_SCORE";
    public const string FutureTime = "FUTURE_TIME";
    public const string UnknownPlayer = "UNKNOWN_PLAYER";
    public const string ForeignTeam = "FOREIGN_TEAM";
    public const string IntentClaim = "INTENT_CLAIM";
    public const string CertaintyClaim = "CERTAINTY_CLAIM";
    public const string CausalFact = "CAUSAL_FACT";
    public const string ConfidenceMismatch = "CONFIDENCE_MISMATCH";
    public const string SampleOverclaim = "SAMPLE_OVERCLAIM";
    public const string EmptyField = "EMPTY_FIELD";
    public const string TooLong = "TOO_LONG";
    public const string MissingCitation = "MISSING_CITATION";
}

/// <summary>Whether text states observations (strictest) or interpretation (hedged reasoning allowed).</summary>
public enum TextKind { Fact, Interpretation, Narrative }

/// <summary>
/// Deterministic grounding checks around the AI stages. Code checks what code can check exactly:
/// evidence IDs exist and precede the cut-off, every number is an authoritative value, the score and
/// times are right, named players are involved, and no intent/certainty/causal over-claiming. Passing
/// these checks does not prove a sentence is true; the Verification Agent reviews meaning on top.
/// </summary>
public static partial class GroundingValidator
{
    public const int MaxFieldLength = 700;

    private static readonly string[] CertaintyClaims =
    [
        "definitely", "certainly", "undoubtedly", "without doubt", "without a doubt", "proves", "proved that", "guaranteed",
        "certainement", "sans aucun doute", "prouve", "sin duda", "demuestra", "garantiza",
    ];

    private static readonly string[] CausalWords =
    [
        "because", "caused", "due to", "led to", "resulted in", "as a result", "parce que", "à cause", "porque", "debido a",
    ];

    public static IReadOnlyList<double> AllowedNumbers(InsightContext c)
    {
        var n = new List<double>(VerificationAgent.DefinitionalConstants)
        {
            c.DisplayMinute, c.MatchSecond / 60, c.Score.Home, c.Score.Away, c.SupportingEventIds.Length, c.SourceEvents.Length,
            c.ConfidenceInputs.Score, Math.Round(c.ConfidenceInputs.Score * 100),
        };
        foreach (var m in c.DeterministicMetrics)
        {
            AddValue(n, m.Current, m.Unit);
            if (m.Baseline is { } b) AddValue(n, b, m.Unit);
            if (m.Change is { } d) AddValue(n, Math.Abs(d), m.Unit);
            if (m.Threshold is { } t) AddValue(n, Math.Abs(t), m.Unit);
            n.Add(m.SampleSize);
            if (m.MinimumSample is { } min) n.Add(min);
            foreach (var w in new[] { m.BaselineWindow, m.CurrentWindow })
                if (w is not null) { n.Add(w.FromMinute); n.Add(w.ToMinute); n.Add(w.ToMinute - w.FromMinute); }
            foreach (var text in new[] { m.DisplayBaseline, m.DisplayCurrent, m.DisplayChange, m.DisplayThreshold, m.Comparison })
                if (text is not null) n.AddRange(VerificationAgent.ExtractNumbers(text));
        }
        foreach (var e in c.SourceEvents) { n.Add(e.Second / 60); n.Add(e.Second % 60); n.Add(e.Second / 60 + 1); }
        var w0 = c.DeterministicNarrative;
        foreach (var text in new[] { w0.Headline, w0.Fact, w0.Inference, w0.PossibleExplanation, w0.Ticker, w0.StudioLine, w0.Teaching })
            if (text is not null) n.AddRange(VerificationAgent.ExtractNumbers(text));
        return n;

        static void AddValue(List<double> list, double value, string unit)
        {
            list.Add(value);
            if (unit == "Share") { list.Add(Math.Round(value * 100)); list.Add(Math.Round(value * 100, 1)); }
        }
    }

    /// <summary>All deterministic checks for one field of generated text.</summary>
    public static IReadOnlyList<GroundingIssue> CheckText(InsightContext c, string agent, string field, string? text, TextKind kind,
        bool required = false, IReadOnlyList<double>? allowed = null)
    {
        var issues = new List<GroundingIssue>();
        void Add(string code, string message) => issues.Add(new(agent, field, code, message));

        if (string.IsNullOrWhiteSpace(text))
        {
            if (required) Add(IssueCodes.EmptyField, "Required text is empty.");
            return issues;
        }
        if (text.Length > MaxFieldLength) Add(IssueCodes.TooLong, $"Text is {text.Length} characters (limit {MaxFieldLength}).");

        allowed ??= AllowedNumbers(c);
        var lower = text.ToLowerInvariant();

        foreach (Match m in EventIdPattern().Matches(text))
            issues.AddRange(CheckEvidenceIds(c, agent, field, [m.Value]));

        // Mask event IDs and clock times before number checks so their digits are judged by the dedicated checks.
        var masked = EventIdPattern().Replace(text, " ");
        foreach (Match m in ClockPattern().Matches(masked))
        {
            var seconds = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (seconds > c.MatchSecond) Add(IssueCodes.FutureTime, $"Time {m.Value} is after the insight ({c.MatchTime}).");
        }
        masked = ClockPattern().Replace(masked, " ");

        foreach (Match m in MinutePattern().Matches(masked))
        {
            var value = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            if (value > c.DisplayMinute) Add(IssueCodes.FutureTime, $"Minute {value} is after the insight (minute {c.DisplayMinute}).");
        }

        foreach (Match m in ScorePattern().Matches(masked))
        {
            int a = m.Groups[1].Value[0] - '0', b = m.Groups[2].Value[0] - '0';
            var isScore = (a == c.Score.Home && b == c.Score.Away) || (a == c.Score.Away && b == c.Score.Home);
            var isWindow = c.DeterministicMetrics.Any(x => x.CurrentWindow is { } w && w.FromMinute == a && w.ToMinute == b);
            if (!isScore && !isWindow) Add(IssueCodes.WrongScore, $"'{m.Value}' does not match the score at {c.MatchTime} ({c.Score}).");
        }

        foreach (var number in VerificationAgent.ExtractNumbers(masked))
            if (!allowed.Any(a => VerificationAgent.Matches(number, a)))
                Add(IssueCodes.UnsupportedNumber, $"Number {number.ToString(CultureInfo.InvariantCulture)} is not an authoritative value.");

        var allowedIds = c.Players.Select(p => p.Id).ToHashSet();
        var allowedSurnames = c.Players.Select(p => Surname(p.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in c.AllMatchPlayers.Where(p => !allowedIds.Contains(p.Id)))
        {
            var surname = Surname(p.Name);
            var named = Contains(text, p.Name) || (surname.Length >= 4 && !allowedSurnames.Contains(surname) && Contains(text, surname));
            if (named) Add(IssueCodes.UnknownPlayer, $"'{p.Name}' is not involved in this insight's evidence.");
        }
        foreach (var club in c.ForeignTeamNames.Where(club => Contains(text, club)))
            Add(IssueCodes.ForeignTeam, $"'{club}' is not in this match.");

        foreach (var claim in VerificationAgent.IntentClaims.Where(x => lower.Contains(x, StringComparison.Ordinal)))
            Add(IssueCodes.IntentClaim, $"Unsupported claim of intent ('{claim}').");
        foreach (var claim in CertaintyClaims.Where(x => ContainsAffirmed(lower, x)))
            Add(IssueCodes.CertaintyClaim, $"Over-certain wording ('{claim}').");
        if (kind == TextKind.Fact)
            foreach (var word in CausalWords.Where(x => ContainsWord(lower, x)))
                Add(IssueCodes.CausalFact, $"A fact may not assert a cause ('{word}').");

        var level = c.ConfidenceInputs.Level;
        if (level != "High" && (lower.Contains("high confidence") || lower.Contains("confiance élevée") || lower.Contains("confianza alta")))
            Add(IssueCodes.ConfidenceMismatch, $"Claims high confidence; deterministic confidence is {level}.");
        if (level == "High" && lower.Contains("low confidence"))
            Add(IssueCodes.ConfidenceMismatch, "Claims low confidence; deterministic confidence is High.");
        if (!c.ConfidenceInputs.AllSampleRequirementsMet && (lower.Contains("statistically significant") || lower.Contains("large sample")))
            Add(IssueCodes.SampleOverclaim, "Over-states the sample: at least one metric did not meet its minimum sample.");

        return issues;
    }

    /// <summary>Every cited ID must be a supporting event of this insight at or before the cut-off.</summary>
    public static IReadOnlyList<GroundingIssue> CheckEvidenceIds(InsightContext c, string agent, string field, IEnumerable<string> ids)
    {
        var issues = new List<GroundingIssue>();
        var supporting = c.SupportingEventIds.ToHashSet();
        foreach (var id in ids.Distinct())
        {
            if (!c.KnownEventSeconds.TryGetValue(id, out var second))
                issues.Add(new(agent, field, IssueCodes.UnknownEvidenceId, $"Evidence '{id}' does not exist."));
            else if (second > c.MatchSecond)
                issues.Add(new(agent, field, IssueCodes.FutureEvidence, $"Evidence '{id}' happens after the insight ({c.MatchTime})."));
            else if (!supporting.Contains(id))
                issues.Add(new(agent, field, IssueCodes.NotSupportingEvidence, $"Evidence '{id}' is not a supporting event of this insight."));
        }
        return issues;
    }

    public static IReadOnlyList<GroundingIssue> CheckTactical(InsightContext c, TacticalAnalystResult t)
    {
        const string a = "Tactical Analyst";
        var allowed = AllowedNumbers(c);
        var issues = new List<GroundingIssue>();
        for (var i = 0; i < t.FactsUsed.Count; i++) issues.AddRange(CheckText(c, a, $"factsUsed[{i}]", t.FactsUsed[i], TextKind.Fact, allowed: allowed));
        issues.AddRange(CheckText(c, a, "inference", t.Inference, TextKind.Interpretation, required: true, allowed: allowed));
        issues.AddRange(CheckText(c, a, "possibleExplanation", t.PossibleExplanation, TextKind.Interpretation, allowed: allowed));
        issues.AddRange(CheckText(c, a, "tacticalSignificance", t.TacticalSignificance, TextKind.Interpretation, allowed: allowed));
        issues.AddRange(CheckText(c, a, "confidenceQualification", t.ConfidenceQualification, TextKind.Interpretation, allowed: allowed));
        for (var i = 0; i < t.Limitations.Count; i++) issues.AddRange(CheckText(c, a, $"limitations[{i}]", t.Limitations[i], TextKind.Interpretation, allowed: allowed));
        issues.AddRange(CheckEvidenceIds(c, a, "evidenceIds", t.EvidenceIds));
        if (t.EvidenceIds.Count == 0 && c.SourceEvents.Length > 0)
            issues.Add(new(a, "evidenceIds", IssueCodes.MissingCitation, "The interpretation cites no supporting event."));
        return issues;
    }

    public static IReadOnlyList<GroundingIssue> CheckAudience(InsightContext c, AudienceNarrativeResult r)
    {
        const string a = "Audience Narrative";
        var allowed = AllowedNumbers(c);
        return
        [
            .. CheckText(c, a, "headline", r.Headline, TextKind.Narrative, required: true, allowed: allowed),
            .. CheckText(c, a, "summary", r.Summary, TextKind.Narrative, required: true, allowed: allowed),
            .. CheckText(c, a, "detail", r.Detail, TextKind.Narrative, allowed: allowed),
            .. CheckText(c, a, "keyTakeaway", r.KeyTakeaway, TextKind.Narrative, allowed: allowed),
        ];
    }

    public static IReadOnlyList<GroundingIssue> CheckAccessibility(InsightContext c, AccessibilityNarrativeResult r)
    {
        const string a = "Accessibility Narrator";
        var allowed = AllowedNumbers(c);
        return
        [
            .. CheckText(c, a, "spokenSummary", r.SpokenSummary, TextKind.Narrative, required: true, allowed: allowed),
            .. CheckText(c, a, "visualDescription", r.VisualDescription, TextKind.Narrative, allowed: allowed),
            .. CheckText(c, a, "simplifiedExplanation", r.SimplifiedExplanation, TextKind.Narrative, required: true, allowed: allowed),
            .. CheckText(c, a, "replayAudioDescription", r.ReplayAudioDescription, TextKind.Narrative, allowed: allowed),
        ];
    }

    public static IReadOnlyList<GroundingIssue> CheckCorrection(InsightContext c, CorrectedNarrative r)
    {
        const string a = "Verification Agent";
        var allowed = AllowedNumbers(c);
        return
        [
            .. CheckText(c, a, "corrected.headline", r.Headline, TextKind.Narrative, required: true, allowed: allowed),
            .. CheckText(c, a, "corrected.summary", r.Summary, TextKind.Narrative, required: true, allowed: allowed),
            .. CheckText(c, a, "corrected.inference", r.Inference, TextKind.Interpretation, allowed: allowed),
            .. CheckText(c, a, "corrected.spokenSummary", r.SpokenSummary, TextKind.Narrative, allowed: allowed),
        ];
    }

    private static string Surname(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? name;

    private static bool Contains(string text, string phrase) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}]){Regex.Escape(phrase)}(?![\p{{L}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ContainsWord(string lower, string phrase) => Contains(lower, phrase);

    /// <summary>True when the phrase occurs without a negation just before it ("does not prove", "no garantiza", "ne prouve pas").</summary>
    private static bool ContainsAffirmed(string lower, string phrase) =>
        Regex.Matches(lower, $@"(?<![\p{{L}}]){Regex.Escape(phrase)}(?![\p{{L}}])").Any(m =>
            !NegationPattern().IsMatch(lower[Math.Max(0, m.Index - 16)..m.Index]));

    [GeneratedRegex(@"\b(?:not|no|never|ne|pas|nunca|jamais)\b|n't\b", RegexOptions.IgnoreCase)]
    private static partial Regex NegationPattern();

    [GeneratedRegex(@"\b[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*-E\d{5}\b")]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"(?<![\d:])(\d{1,2}):([0-5]\d)(?![\d:])")]
    private static partial Regex ClockPattern();

    [GeneratedRegex(@"(?<![\d.,])(?<n>\d{1,3})\s*(?:'|’|′)|\b(?:minute|minuto)\s+(?<n>\d{1,3})\b|\b(?<n>\d{1,3})(?:st|nd|rd|th|e|º|ª)?\s*(?:minute|min)\b(?!s)", RegexOptions.IgnoreCase)]
    private static partial Regex MinutePattern();

    // A single-digit pair such as "2-1"; a trailing full stop or comma ends a sentence, but "3-0.5" is not a score.
    [GeneratedRegex(@"(?<![\d'’′.,:-])(\d)\s*[-–]\s*(\d)(?![\d'’′%:–-]|[.,]\d)")]
    private static partial Regex ScorePattern();
}

/// <summary>The combined outputs of the agent stages, as one candidate for verification.</summary>
public sealed record NarrativeCandidate(TacticalAnalystResult Tactical, AudienceNarrativeResult Audience, AccessibilityNarrativeResult? Accessibility);

/// <summary>Outcome of deterministic validation: the pruned candidate plus every finding and removal.</summary>
public sealed record ValidationReport(
    NarrativeCandidate Candidate,
    IReadOnlyList<GroundingIssue> Issues,
    IReadOnlyList<string> Removed,
    bool AudienceValid,
    bool AccessibilityDropped)
{
    public bool Clean => Issues.Count == 0;
}

/// <summary>
/// Removes optional claims that failed deterministic checks (removal never adds an unsupported claim),
/// substitutes the deterministic inference when the model's inference fails, and reports whether the
/// required audience text still needs a repair.
/// </summary>
public static class CandidateSanitizer
{
    public static ValidationReport Validate(InsightContext c, NarrativeCandidate candidate)
    {
        var allIssues = new List<GroundingIssue>();
        var removed = new List<string>();
        var t = candidate.Tactical;

        var tacticalIssues = GroundingValidator.CheckTactical(c, t);
        allIssues.AddRange(tacticalIssues);
        bool Bad(string field) => tacticalIssues.Any(i => i.Field == field);
        var facts = t.FactsUsed.Where((_, i) => !Bad($"factsUsed[{i}]")).ToList();
        var limitations = t.Limitations.Where((_, i) => !Bad($"limitations[{i}]")).ToList();
        var supporting = c.SupportingEventIds.ToHashSet();
        var ids = t.EvidenceIds.Where(supporting.Contains).Distinct().ToList();
        if (facts.Count < t.FactsUsed.Count) removed.Add($"{t.FactsUsed.Count - facts.Count} tactical fact(s)");
        if (limitations.Count < t.Limitations.Count) removed.Add($"{t.Limitations.Count - limitations.Count} limitation(s)");
        if (ids.Count < t.EvidenceIds.Distinct().Count()) removed.Add($"{t.EvidenceIds.Distinct().Count() - ids.Count} invalid evidence reference(s)");
        string? Keep(string field, string? value) { if (value is null || !Bad(field)) return value; removed.Add($"tactical {field}"); return null; }
        var inference = t.Inference;
        if (Bad("inference")) { inference = c.DeterministicNarrative.Inference; removed.Add("tactical inference (replaced by deterministic wording)"); }
        var tactical = t with
        {
            FactsUsed = facts.Count > 0 ? facts : [c.DeterministicNarrative.Fact],
            Inference = inference,
            PossibleExplanation = Keep("possibleExplanation", t.PossibleExplanation),
            TacticalSignificance = Keep("tacticalSignificance", t.TacticalSignificance),
            ConfidenceQualification = Keep("confidenceQualification", t.ConfidenceQualification) ?? "",
            EvidenceIds = ids.Count > 0 ? ids : [.. c.SourceEvents.Take(3).Select(e => e.Id)],
            Limitations = limitations,
        };

        var audienceIssues = GroundingValidator.CheckAudience(c, candidate.Audience);
        allIssues.AddRange(audienceIssues);
        var audienceValid = !audienceIssues.Any(i => i.Field is "headline" or "summary");
        var audience = candidate.Audience with
        {
            Detail = audienceIssues.Any(i => i.Field == "detail") ? null : candidate.Audience.Detail,
            KeyTakeaway = audienceIssues.Any(i => i.Field == "keyTakeaway") ? "" : candidate.Audience.KeyTakeaway,
        };
        if (audienceIssues.Any(i => i.Field is "detail" or "keyTakeaway")) removed.Add("audience optional text");

        var accessibility = candidate.Accessibility;
        var dropped = false;
        if (accessibility is not null)
        {
            var aIssues = GroundingValidator.CheckAccessibility(c, accessibility);
            allIssues.AddRange(aIssues);
            if (aIssues.Any(i => i.Field is "spokenSummary" or "simplifiedExplanation"))
            {
                accessibility = null;
                dropped = true;
                removed.Add("accessible narration (failed checks; client-side deterministic description used)");
            }
            else if (aIssues.Count > 0)
            {
                accessibility = accessibility with
                {
                    VisualDescription = aIssues.Any(i => i.Field == "visualDescription") ? null : accessibility.VisualDescription,
                    ReplayAudioDescription = aIssues.Any(i => i.Field == "replayAudioDescription") ? null : accessibility.ReplayAudioDescription,
                };
                removed.Add("accessible optional descriptions");
            }
        }

        return new ValidationReport(new NarrativeCandidate(tactical, audience, accessibility), allIssues, removed, audienceValid, dropped);
    }
}
