using System.Globalization;
using System.Text.RegularExpressions;
using MatchShift.Agents.Agents;

namespace MatchShift.Agents.Workflow;

/// <summary>
/// Typed numeric grounding. Each number in generated text is classified by its context (score, match time, clock,
/// duration, percentage, points, "X of Y", a counted noun, confidence/z-score, or a plain metric value) and checked
/// ONLY against the authoritative values of that category. A number is never accepted merely because the same digits
/// appear somewhere else in the <see cref="InsightContext"/> (e.g. "8 key players" is not grounded by a minimum sample of 8).
///
/// Identifiers (WHA-17, event-203, player_11, id:17, MS-...-E00510) are not numeric claims: event IDs are checked as
/// evidence elsewhere, player IDs must be players involved in the evidence, and other identifiers are ignored.
/// Sentences copied verbatim from the deterministic wording are grounded by construction.
/// </summary>
public static partial class NumericGrounding
{
    public sealed record Problem(string Code, string Message);


    public static IReadOnlyList<Problem> Check(InsightContext c, string text)
    {
        var problems = new List<Problem>();
        var v = Values.For(c);
        var work = StripAuthoritative(c, text);

        // 1. Identifiers: never numeric claims. Player IDs must be involved players.
        work = EventIdPattern().Replace(work, Blank);
        work = PlayerIdPattern().Replace(work, m =>
        {
            var id = m.Value;
            if (c.AllMatchPlayers.Any(p => p.Id == id) && c.Players.All(p => p.Id != id))
                problems.Add(new(IssueCodes.UnknownPlayer, $"Player '{id}' is not involved in this insight's evidence."));
            return Blank(m);
        });
        work = OtherIdentifierPattern().Replace(work, Blank);

        // 2. Common English number words before a unit become digits, so spelled-out claims are checked too.
        work = NormalizeNumberWords(work);

        // 3. Clocks ("34:33", "34 minutes and 33 seconds").
        work = Take(work, ClockPattern(), m => CheckClock(c, v, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), m.Value, problems));
        work = Take(work, ClockWordsPattern(), m => CheckClock(c, v, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), m.Value, problems));

        // 4. Scores ("2-1", "1 nil").
        work = Take(work, ScorePattern(), m => CheckScore(c, m.Groups[1].Value[0] - '0', m.Groups[2].Value[0] - '0', m.Value, problems));
        work = Take(work, NilPattern(), m => CheckScore(c, m.Groups[1].Value[0] - '0', 0, m.Value, problems));

        // 5. Match minutes ("67'", "minute 67", "67th minute", "at/since/after 67 minutes"): not in the future, and a known time.
        //    Ranges ("minutes 50 to 62", "entre el minuto 50 y 62", "between the 50th and 62nd minute") are two match minutes.
        work = Take(work, MinuteRangePattern(), m =>
        {
            CheckMinute(c, v, int.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture), problems);
            CheckMinute(c, v, int.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture), problems);
        });
        foreach (var pattern in new[] { MinuteMarkPattern(), MinuteWordPattern(), OrdinalMinutePattern(), PrepositionMinutePattern() })
            work = Take(work, pattern, m => CheckMinute(c, v, int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture), problems));

        // 6. "X of (the last) Y <noun>": both values must come from ONE metric (numerator and its sample).
        work = Take(work, FractionPattern(), m => CheckFraction(c, Num(m.Groups[1].Value), Num(m.Groups[2].Value), m.Value, problems));

        // 7. Percentages and points.
        work = Take(work, PercentPattern(), m => Require(v.Percentages, Num(m.Groups[1].Value), $"{m.Groups[1].Value}%", "percentage", problems));
        work = Take(work, PointsPattern(), m => Require(v.Points, Num(m.Groups[1].Value), m.Value.Trim(), "change in points", problems));

        // 8. Confidence and z-scores.
        work = Take(work, ConfidencePattern(), m => Require(v.Confidence, Num(m.Groups["n"].Value), m.Value.Trim(), "confidence value", problems));
        work = Take(work, ZScorePattern(), m => Require(v.Statistics, Num(m.Groups[1].Value), m.Value.Trim(), "test statistic", problems));

        // 9. Durations.
        work = Take(work, SecondsPattern(), m => Require(v.Seconds, Num(m.Groups[1].Value), m.Value.Trim(), "duration in seconds", problems));
        work = Take(work, MinutesDurationPattern(), m => Require(v.DurationMinutes, Num(m.Groups[1].Value), m.Value.Trim(), "duration in minutes", problems));

        // 10. Counted nouns ("10 attacks", "8 key players", "12 supporting events").
        work = Take(work, CountPattern(), m => CheckCount(c, v, Num(m.Groups[1].Value), m.Groups["noun"].Value.ToLowerInvariant(),
            m.Value.Trim(), work, m.Index, problems));

        // 11. Ordinals that are not minutes ("1st goal", "second half" is words) are not quantity claims.
        work = OrdinalPattern().Replace(work, Blank);

        // 12. Anything left is a plain value and must be a metric value (never a sample size, window bound or event time).
        foreach (Match m in NumberPattern().Matches(work))
            Require(v.MetricValues, Num(m.Value), m.Value, "metric value", problems);

        return problems;
    }

    // ---------------------------------------------------------------- categorised authoritative values

    private sealed class Values
    {
        public List<double> MetricValues { get; } = [0];
        public List<double> Percentages { get; } = [0, 100];
        public List<double> Points { get; } = [];
        public List<double> Confidence { get; } = [];
        public List<double> Statistics { get; } = [];
        public List<double> Seconds { get; } = [];
        public List<double> DurationMinutes { get; } = [45, 90];
        public HashSet<int> KnownMinutes { get; } = [];
        public HashSet<int> KnownClockSeconds { get; } = [];

        public static Values For(InsightContext c)
        {
            var v = new Values();
            v.Confidence.AddRange([c.ConfidenceInputs.Score, Math.Round(c.ConfidenceInputs.Score * 100)]);
            v.Percentages.Add(Math.Round(c.ConfidenceInputs.Score * 100));
            foreach (var m in c.DeterministicMetrics)
            {
                var values = new[] { (double?)m.Current, m.Baseline, Abs(m.Change), Abs(m.Threshold) }.OfType<double>().ToList();
                v.MetricValues.AddRange(values);
                foreach (var display in new[] { m.DisplayBaseline, m.DisplayCurrent, m.DisplayChange, m.DisplayThreshold })
                    if (display is not null) v.MetricValues.AddRange(VerificationAgent.ExtractNumbers(display));
                switch (m.Unit)
                {
                    case "Share":
                        v.Percentages.AddRange(values.Select(x => x * 100));
                        v.Points.AddRange(new[] { Abs(m.Change), Abs(m.Threshold) }.OfType<double>().Select(x => x * 100));
                        v.MetricValues.AddRange(values.Select(x => x * 100));
                        break;
                    case "Index":
                        v.Points.AddRange(values);
                        break;
                    case "Statistic":
                        v.Statistics.AddRange(values);
                        break;
                    case "Seconds":
                        v.Seconds.AddRange(values);
                        break;
                }
                foreach (var w in new[] { m.BaselineWindow, m.CurrentWindow }.OfType<WindowFact>())
                {
                    v.KnownMinutes.Add(w.FromMinute);
                    v.KnownMinutes.Add(w.ToMinute);
                    v.DurationMinutes.Add(w.ToMinute - w.FromMinute);
                }
            }
            v.KnownMinutes.UnionWith([c.DisplayMinute, c.MatchSecond / 60, 45]);
            v.KnownClockSeconds.Add(c.MatchSecond);
            foreach (var e in c.SourceEvents)
            {
                v.KnownMinutes.UnionWith([e.Second / 60, e.Second / 60 + 1]);
                v.KnownClockSeconds.Add(e.Second);
            }
            if (c.Facts.TryGetValue("substitutionMinute", out var sub) && int.TryParse(sub, out var subMinute))
                v.KnownMinutes.UnionWith([subMinute, subMinute + 1]);
            return v;
        }

        private static double? Abs(double? x) => x is null ? null : Math.Abs(x.Value);
    }

    // ---------------------------------------------------------------- checks

    private static void CheckClock(InsightContext c, Values v, int minutes, int seconds, string text, List<Problem> problems)
    {
        var at = minutes * 60 + seconds;
        if (at > c.MatchSecond) problems.Add(new(IssueCodes.FutureTime, $"Time '{text.Trim()}' is after the insight ({c.MatchTime})."));
        else if (!v.KnownClockSeconds.Contains(at)) problems.Add(new(IssueCodes.UnsupportedTime, $"Time '{text.Trim()}' is not the time of the insight or of a supporting event."));
    }

    private static void CheckScore(InsightContext c, int a, int b, string text, List<Problem> problems)
    {
        var isScore = (a == c.Score.Home && b == c.Score.Away) || (a == c.Score.Away && b == c.Score.Home);
        var isWindow = c.DeterministicMetrics.Any(x => x.CurrentWindow is { } w && w.FromMinute == a && w.ToMinute == b);
        if (!isScore && !isWindow) problems.Add(new(IssueCodes.WrongScore, $"'{text.Trim()}' does not match the score at {c.MatchTime} ({c.Score})."));
    }

    private static void CheckMinute(InsightContext c, Values v, int minute, List<Problem> problems)
    {
        if (minute > c.DisplayMinute) problems.Add(new(IssueCodes.FutureTime, $"Minute {minute} is after the insight (minute {c.DisplayMinute})."));
        else if (!v.KnownMinutes.Contains(minute)) problems.Add(new(IssueCodes.UnsupportedTime, $"Minute {minute} is not a window boundary, supporting-event time or the insight time."));
    }

    private static void CheckFraction(InsightContext c, double part, double whole, string text, List<Problem> problems)
    {
        foreach (var m in c.DeterministicMetrics)
        {
            var pairs = new List<(double Part, double Whole)>();
            if (m.Unit == "Share")
            {
                pairs.Add((Math.Round(m.Current * m.SampleSize), m.SampleSize));
                if (m is { Baseline: { } b, BaselineSampleSize: { } bs }) pairs.Add((Math.Round(b * bs), bs));
            }
            else if (m.Unit == "Count")
            {
                pairs.Add((m.Current, m.SampleSize));
                if (m is { Baseline: { } b, BaselineSampleSize: { } bs }) pairs.Add((b, bs));
            }
            if (pairs.Any(p => p.Whole > 0 && VerificationAgent.Matches(part, p.Part) && VerificationAgent.Matches(whole, p.Whole))) return;
        }
        problems.Add(new(IssueCodes.UnsupportedNumber, $"'{text.Trim()}' is not a numerator and sample of any single metric."));
    }

    private static void CheckCount(InsightContext c, Values v, double n, string noun, string text, string work, int index, List<Problem> problems)
    {
        var allowed = new List<double>();
        var stem = Stem(noun);
        var minimumNearby = MinimumWordsPattern().IsMatch(work[Math.Max(0, index - 30)..index]);
        switch (stem)
        {
            case "player":
                break; // InsightContext has no grounded player-count fact.
            case "event":
                allowed.AddRange([c.SupportingEventIds.Length, c.SourceEvents.Length]);
                break;
            case "goal":
                allowed.AddRange([c.Score.Home, c.Score.Away, Math.Abs(c.Score.Home - c.Score.Away)]);
                break;
            default:
                var sampleNoun = stem is "attack" or "possession" or "observation" or "action";
                foreach (var m in c.DeterministicMetrics)
                {
                    var related = m.Key.Contains(stem, StringComparison.OrdinalIgnoreCase) || m.Label.Contains(stem, StringComparison.OrdinalIgnoreCase);
                    if (related && m.Unit == "Count") allowed.AddRange(new[] { (double?)m.Current, m.Baseline, m.Change is { } d ? Math.Abs(d) : null }.OfType<double>());
                    if (related || sampleNoun)
                    {
                        allowed.Add(m.SampleSize);
                        if (m.BaselineSampleSize is { } bs) allowed.Add(bs);
                        if (minimumNearby && m.MinimumSample is { } min) allowed.Add(min);
                    }
                }
                break;
        }
        if (!allowed.Any(a => VerificationAgent.Matches(n, a)))
            problems.Add(new(IssueCodes.UnsupportedNumber, $"'{text}' is not a grounded count of {noun}."));
    }

    private static void Require(List<double> allowed, double n, string text, string category, List<Problem> problems)
    {
        if (!allowed.Any(a => VerificationAgent.Matches(n, a)))
            problems.Add(new(IssueCodes.UnsupportedNumber, $"'{text}' is not an authoritative {category}."));
    }

    private static string Stem(string noun) => noun switch
    {
        "recoveries" or "recovery" => "recover",
        "passes" => "pass",
        "possessions" => "possession",
        _ when noun.EndsWith('s') => noun[..^1],
        _ => noun,
    };

    // ---------------------------------------------------------------- text helpers

    /// <summary>Sentences copied verbatim from the deterministic wording are authoritative.</summary>
    private static string StripAuthoritative(InsightContext c, string text)
    {
        var w = c.DeterministicNarrative;
        var sentences = new[] { w.Headline, w.Fact, w.Inference, w.PossibleExplanation, w.Ticker, w.StudioLine, w.Teaching }
            .OfType<string>()
            .SelectMany(s => SentenceSplit().Split(s))
            .Select(s => s.Trim().TrimEnd('.', '!', '?').Trim())
            .Where(s => s.Length >= 12)
            .OrderByDescending(s => s.Length);
        foreach (var s in sentences)
            text = Regex.Replace(text, Regex.Escape(s), m => new string(' ', m.Length), RegexOptions.IgnoreCase);
        return text;
    }

    private static string Take(string work, Regex pattern, Action<Match> check) =>
        pattern.Replace(work, m => { check(m); return Blank(m); });

    private static string Blank(Match m) => new(' ', m.Length);

    private static double Num(string s) => double.Parse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static readonly Dictionary<string, int> Units = new()
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8,
        ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15,
        ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
    };

    private static readonly Dictionary<string, int> Tens = new()
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    /// <summary>English number words (0-99) directly before a unit or counted noun become digits. "one of the" etc. are left alone.</summary>
    public static string NormalizeNumberWords(string text) =>
        NumberWordsPattern().Replace(text, m =>
        {
            var words = m.Groups["w"].Value.ToLowerInvariant().Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);
            var value = 0;
            foreach (var w in words) value += Tens.TryGetValue(w, out var t) ? t : Units[w];
            return value.ToString(CultureInfo.InvariantCulture);
        });

    private const string NumberWord =
        "(?:twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)(?:[\\s-](?:one|two|three|four|five|six|seven|eight|nine))?" +
        "|zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen";

    private const string CountNouns =
        "attacks?|shots?|passes|pass|players?|events?|actions?|goals?|possessions?|turnovers?|recoveries|recovery|chances?|transitions?|" +
        "observations?|crosses|tackles?|corners?|interceptions?|substitutions?";

    [GeneratedRegex(@"(?<![\p{L}])(?<w>" + NumberWord + @")(?=\s+(?:per\s?cent|percent|minutes?|seconds?|nil|points?|(?:[\p{L}-]+\s+){0,2}(?:" + CountNouns + @"))\b)", RegexOptions.IgnoreCase)]
    private static partial Regex NumberWordsPattern();

    // Split before a capital letter only, so abbreviations such as "min. 35" stay inside their sentence.
    [GeneratedRegex(@"(?<=[.!?])\s+(?=\p{Lu})")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"\b[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*-E\d{5}\b")]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"\b[A-Z]{2,4}-\d{1,3}\b")]
    private static partial Regex PlayerIdPattern();

    [GeneratedRegex(@"\b\p{L}[\p{L}\d]*(?:[-_#]|:\s?)\d+\b")]
    private static partial Regex OtherIdentifierPattern();

    [GeneratedRegex(@"(?<![\d:])(\d{1,2}):([0-5]\d)(?![\d:])")]
    private static partial Regex ClockPattern();

    [GeneratedRegex(@"\b(\d{1,2})\s+minutes?,?\s+(?:and\s+)?(\d{1,2})\s+seconds?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClockWordsPattern();

    [GeneratedRegex(@"(?<![\d'’′.,:-])(\d)\s*[-–]\s*(\d)(?![\d'’′%:–-]|[.,]\d)")]
    private static partial Regex ScorePattern();

    [GeneratedRegex(@"\b(\d)\s+nil\b", RegexOptions.IgnoreCase)]
    private static partial Regex NilPattern();

    [GeneratedRegex(@"\b(?:minutes?|minutos?|minuto)\s+(?<a>\d{1,3})\s*(?:to|and|y|et|-|–)\s*(?<b>\d{1,3})\b|\bbetween\s+(?:the\s+)?(?<a>\d{1,3})(?:st|nd|rd|th)?\s+and\s+(?:the\s+)?(?<b>\d{1,3})(?:st|nd|rd|th)?\s+minutes?\b", RegexOptions.IgnoreCase)]
    private static partial Regex MinuteRangePattern();

    [GeneratedRegex(@"(?<![\d.,])(?<n>\d{1,3})\s*['’′]")]
    private static partial Regex MinuteMarkPattern();

    [GeneratedRegex(@"(?:\b(?:minute|minuto)\s+|\bmin\.\s*)(?<n>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex MinuteWordPattern();

    [GeneratedRegex(@"\b(?<n>\d{1,3})(?:st|nd|rd|th|e|º|ª)?\s*(?:minute|min)\b(?!s)", RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalMinutePattern();

    [GeneratedRegex(@"\b(?:at|since|after|from|until|before|around|by)\s+(?:the\s+)?(?<n>\d{1,3})\s*(?:minutes?|mins?)\b(?!\s+and\s+\d)", RegexOptions.IgnoreCase)]
    private static partial Regex PrepositionMinutePattern();

    [GeneratedRegex(@"\b(\d+)\s+(?:out\s+)?of\s+(?:the\s+|their\s+|its\s+)?(?:last\s+|first\s+|previous\s+)?(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FractionPattern();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(?:%|per\s?cent\b|percent\b|pour\s?cent\b|por\s?ciento\b)", RegexOptions.IgnoreCase)]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s+(?:percentage\s+)?(?:points?|pts)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PointsPattern();

    [GeneratedRegex(@"confiden\w*[^\d.]{0,20}(?<n>\d+(?:[.,]\d+)?)|(?<n>\d+(?:[.,]\d+)?)\s*(?:%\s*)?confiden", RegexOptions.IgnoreCase)]
    private static partial Regex ConfidencePattern();

    [GeneratedRegex(@"\bz(?:-?score)?\s*(?:of|=|:)?\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex ZScorePattern();

    [GeneratedRegex(@"\b(\d+(?:[.,]\d+)?)\s*(?:seconds?|secs?|s)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SecondsPattern();

    [GeneratedRegex(@"\b(\d+)\s*(?:minutes?|mins?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MinutesDurationPattern();

    [GeneratedRegex(@"\b(\d+(?:[.,]\d+)?)\s+(?:[\p{L}-]+\s+){0,2}?(?<noun>" + CountNouns + @")\b", RegexOptions.IgnoreCase)]
    private static partial Regex CountPattern();

    [GeneratedRegex(@"\b(?:minimum|at least|required|requires|threshold of)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MinimumWordsPattern();

    [GeneratedRegex(@"\b\d+(?:st|nd|rd|th)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9])\d+(?:[.,]\d+)?")]
    private static partial Regex NumberPattern();
}
