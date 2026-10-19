using System.Globalization;
using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Analytics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;

namespace MatchShift.Agents.Tests;

/// <summary>Typed numeric grounding: every number is checked against the authoritative values of its own category.</summary>
public class NumericGroundingTests
{
    private static readonly Match Flagship = SyntheticMatchGenerator.GenerateFlagship().Match;
    private static readonly IReadOnlyList<Insight> Insights = LiveMatchAnalyzer.AnalyzeFull(Flagship).Insights;

    private static InsightContext Ctx(InsightType type) =>
        InsightContextBuilder.Build(Insights.First(i => i.Type == type), Flagship, new ViewerProfile(ViewerMode.Analyst, "en"),
            AccessibilityRequirements.All, "wf-test", ["Ravensmoor Rovers"]);

    private static InsightContext Shift => Ctx(InsightType.AttackChannelShift);

    /// <summary>A context with one known metric: 6 of the last 8 progressive actions (75%), previously 3 of 12, minimum sample 8.</summary>
    private static InsightContext Progressive => Shift with
    {
        DeterministicMetrics =
        [
            new MetricFact("ProgressiveShare", "Progressive actions (share)", "Share", 0.25, 0.75, 0.5, 0.2, "25%", "75%", "+50 pts", "20 pts",
                new WindowFact(40, 50, "40'-50'"), new WindowFact(50, 60, "50'-60'"), 8, 12, 8, true, "last 10 vs previous 10", true),
        ],
    };

    private static IReadOnlyList<GroundingIssue> Check(InsightContext c, string text) => GroundingValidator.CheckText(c, "agent", "field", text, TextKind.Narrative);

    private static bool Numeric(GroundingIssue i) => i.Code is IssueCodes.UnsupportedNumber or IssueCodes.UnsupportedTime or IssueCodes.FutureTime or IssueCodes.WrongScore;

    // 1, 2, 14: identifiers

    [Fact]
    public void Player_identifier_digits_are_not_numeric_claims_but_the_identifier_is_still_validated()
    {
        var c = Shift;
        var involved = c.Players.First(p => p.Id.Contains('-'));
        Assert.DoesNotContain(Check(c, $"The substitute ({involved.Id}) changed the picture."), Numeric);

        var outsider = c.AllMatchPlayers.First(p => c.Players.All(q => q.Id != p.Id));
        var issues = Check(c, $"Then {outsider.Id} came into the move.");
        Assert.Contains(issues, i => i.Code == IssueCodes.UnknownPlayer && i.Message.Contains(outsider.Id));
        Assert.DoesNotContain(issues, Numeric);
    }

    [Theory]
    [InlineData("See event-203 for the pass.")]
    [InlineData("Tracked as player_11 in the feed.")]
    [InlineData("Reference id:17 in the log.")]
    [InlineData("HOME-8 pressed high.")]
    public void Generic_identifiers_create_no_numeric_claim(string text) =>
        Assert.DoesNotContain(Check(Shift, text), Numeric);

    [Fact]
    public void Event_identifiers_are_validated_as_evidence_not_as_numbers()
    {
        var c = Shift;
        Assert.Empty(Check(c, $"It began with {c.SupportingEventIds[0]}."));
        var issues = Check(c, "It began with MS-FLAGSHIP-001-E99999.");
        Assert.Contains(issues, i => i.Code == IssueCodes.UnknownEvidenceId);
        Assert.DoesNotContain(issues, Numeric);
    }

    // 3: counts are checked in their own category, never against unrelated values

    [Fact]
    public void Player_count_is_rejected_even_when_another_field_holds_the_same_number()
    {
        var c = Progressive; // minimum sample 8 and sample size 8 exist, but no player count does
        Assert.Contains(Check(c, "The move involved 8 key players."), i => i.Code == IssueCodes.UnsupportedNumber && i.Message.Contains("players"));
        Assert.Contains(Check(c, "The move involved eight key players."), i => i.Code == IssueCodes.UnsupportedNumber);
    }

    // 4: "X of Y" must come from one metric

    [Theory]
    [InlineData("They completed 6 of the last 8 progressive actions.", true)]
    [InlineData("Earlier it was 3 of 12.", true)]
    [InlineData("They completed 5 of the last 8 progressive actions.", false)]
    [InlineData("They completed 6 of the last 9 progressive actions.", false)]
    [InlineData("They completed 3 of the last 8 progressive actions.", false)]
    public void Fractions_must_be_the_numerator_and_sample_of_a_single_metric(string text, bool valid) =>
        Assert.Equal(valid, !Check(Progressive, text).Any(Numeric));

    // 5, 6: percentages

    [Fact]
    public void Percentages_are_checked_against_share_values_only()
    {
        var c = Shift;
        var share = c.DeterministicMetrics.First(m => m.Unit == "Share" && m.Baseline is not null);
        var before = Math.Round(share.Baseline!.Value * 100).ToString(CultureInfo.InvariantCulture);
        var after = Math.Round(share.Current * 100).ToString(CultureInfo.InvariantCulture);
        Assert.DoesNotContain(Check(c, $"Right-side attacks rose from {before}% to {after}%."), Numeric);
        Assert.DoesNotContain(Check(c, $"It rose to {after} per cent."), Numeric);
        Assert.Contains(Check(c, "Possession rose by 17%."), i => i.Code == IssueCodes.UnsupportedNumber && i.Message.Contains("17%"));
        Assert.Contains(Check(c, "Progression fell 38%."), i => i.Code == IssueCodes.UnsupportedNumber);
        // The sample size (an integer that exists in the context) is not a valid percentage.
        Assert.Contains(Check(c, $"Only {share.SampleSize}% of attacks went right."), i => i.Code == IssueCodes.UnsupportedNumber);
    }

    // 7, 8: scores

    [Fact]
    public void Scores_are_checked_semantically()
    {
        var c = Shift;
        Assert.DoesNotContain(Check(c, $"With the score {c.Score.Home}-{c.Score.Away}, they pushed on."), Numeric);
        Assert.Contains(Check(c, "The score is 2-1."), i => i.Code == IssueCodes.WrongScore);
        Assert.Contains(Check(c, "They led 3 nil."), i => i.Code == IssueCodes.WrongScore);
    }

    // 9, 10: match time

    [Fact]
    public void Match_minutes_must_not_be_future_and_must_be_a_known_time()
    {
        var c = Shift;
        var window = c.DeterministicMetrics.First(m => m.CurrentWindow is not null).CurrentWindow!;
        Assert.DoesNotContain(Check(c, $"Since the {window.FromMinute}th minute they went right."), Numeric);
        Assert.DoesNotContain(Check(c, $"At {window.FromMinute} minutes the pattern changed."), Numeric);
        Assert.Contains(Check(c, $"At {c.DisplayMinute + 4} minutes they scored."), i => i.Code == IssueCodes.FutureTime);
        Assert.Contains(Check(c, $"In the {c.DisplayMinute + 4}' it changed."), i => i.Code == IssueCodes.FutureTime);
        Assert.Contains(Check(c, "At 3 minutes the pattern began."), i => i.Code == IssueCodes.UnsupportedTime);
        // Ranges seen in real model output (English and Spanish) are two match minutes, not metric values.
        Assert.DoesNotContain(Check(c, $"From minutes {window.FromMinute} to {window.ToMinute}, they went right."), Numeric);
        Assert.DoesNotContain(Check(c, $"Entre el minuto {window.FromMinute} y {window.ToMinute}, atacaron por la derecha."), Numeric);
        Assert.Contains(Check(c, $"From minutes {window.FromMinute} to {c.DisplayMinute + 9}, they went right."), i => i.Code == IssueCodes.FutureTime);
    }

    [Fact]
    public void Clock_times_including_spelled_out_ones_must_be_known_and_not_future()
    {
        var c = Ctx(InsightType.Goal);
        var e = c.SourceEvents.Last();
        Assert.DoesNotContain(Check(c, $"The goal came at {e.Clock}."), Numeric);
        var (m, s) = (e.Second / 60, e.Second % 60);
        Assert.DoesNotContain(Check(c, $"He scored at {Words(m)} minutes and {Words(s)} seconds."), Numeric);
        Assert.DoesNotContain(Check(c, $"It starts with a pass at {m} minutes {s} seconds."), Numeric); // real model phrasing
        var firstEvent = c.SourceEvents.First();
        var unknownPast = Enumerable.Range(1, 59).Select(d => firstEvent.Second - d).First(t => c.SourceEvents.All(x => x.Second != t));
        Assert.Contains(Check(c, $"It starts with a pass at {unknownPast / 60} minutes {unknownPast % 60} seconds."), i => i.Code == IssueCodes.UnsupportedTime);
        Assert.Contains(Check(c, $"Then at {MatchClock.Format(c.MatchSecond + 30)} they scored again."), i => i.Code == IssueCodes.FutureTime);
    }

    // 11, 12: counts

    [Fact]
    public void Counts_are_checked_against_their_noun()
    {
        var c = Shift;
        var attacks = c.DeterministicMetrics.First(m => m.Unit == "Share").SampleSize;
        Assert.DoesNotContain(Check(c, $"That came over {attacks} attacks."), Numeric);
        Assert.Contains(Check(c, $"That came over {attacks + 13} attacks."), i => i.Code == IssueCodes.UnsupportedNumber);
        Assert.DoesNotContain(Check(c, $"It is backed by {c.SupportingEventIds.Length} supporting events."), Numeric);
        Assert.Contains(Check(c, $"It is backed by {c.SupportingEventIds.Length + 7} supporting events."), i => i.Code == IssueCodes.UnsupportedNumber);
        Assert.Contains(Check(c, "They had 23 shots."), i => i.Code == IssueCodes.UnsupportedNumber);
    }

    [Fact]
    public void Minimum_sample_is_only_accepted_where_the_text_talks_about_a_minimum()
    {
        var c = Progressive;
        Assert.DoesNotContain(Check(c, "It needed at least 8 attacks to count."), Numeric);
        Assert.DoesNotContain(Check(c, "The sample was 8 attacks."), Numeric); // also the actual sample size
        var noMinimumMatch = Progressive with
        {
            DeterministicMetrics = [Progressive.DeterministicMetrics[0] with { SampleSize = 10, BaselineSampleSize = 12, MinimumSample = 8 }],
        };
        Assert.Contains(Check(noMinimumMatch, "The sample was 8 attacks."), i => i.Code == IssueCodes.UnsupportedNumber);
        Assert.DoesNotContain(Check(noMinimumMatch, "It needed at least 8 attacks to count."), Numeric);
    }

    // 13: spelled-out numbers

    [Fact]
    public void Spelled_out_numbers_before_a_unit_are_checked()
    {
        var c = Shift;
        var share = c.DeterministicMetrics.First(m => m.Unit == "Share" && m.Baseline is not null);
        var after = (int)Math.Round(share.Current * 100);
        Assert.DoesNotContain(Check(c, $"Now {Words(after)} per cent of their attacks go right."), Numeric);
        Assert.Contains(Check(c, "Now seventeen per cent of their attacks go right."), i => i.Code == IssueCodes.UnsupportedNumber);
        Assert.Contains(Check(c, "They had twenty-three shots."), i => i.Code == IssueCodes.UnsupportedNumber);
        // Number words that are not quantities are left alone.
        Assert.DoesNotContain(Check(c, "This is one of the clearest shifts, and one side is now dominant."), Numeric);
    }

    [Fact]
    public void Z_scores_are_checked_as_statistics_even_next_to_the_word_confidence()
    {
        var c = Shift;
        var z = c.DeterministicMetrics.First(m => m.Unit == "Statistic");
        var zText = z.Current.ToString("0.##", CultureInfo.InvariantCulture);
        // The real hosted phrasing that was a false positive: the z-value is a statistic, not a confidence score.
        Assert.DoesNotContain(Check(c, $"The shift is significant with high confidence (z={zText})."), Numeric);
        Assert.DoesNotContain(Check(c, $"It has a z-score of {zText} against a threshold of {z.Threshold!.Value.ToString("0.##", CultureInfo.InvariantCulture)}."), Numeric);
        // An unsupported z-score is still rejected, including next to "confidence".
        Assert.Contains(Check(c, "The shift is significant with high confidence (z=5.12)."), i => i.Code == IssueCodes.UnsupportedNumber && i.Message.Contains("statistic"));
        Assert.Contains(Check(c, "It has a z-score of 4.4."), i => i.Code == IssueCodes.UnsupportedNumber);
    }

    [Fact]
    public void Confidence_values_are_still_checked_as_confidence()
    {
        var c = Shift;
        var score = c.ConfidenceInputs.Score;
        Assert.DoesNotContain(Check(c, $"Deterministic confidence: {score.ToString("0.00", CultureInfo.InvariantCulture)}."), Numeric);
        Assert.DoesNotContain(Check(c, $"MatchShift is {Math.Round(score * 100).ToString(CultureInfo.InvariantCulture)}% confident in this reading."), Numeric);
        Assert.Contains(Check(c, "Confidence is 0.42."), i => i.Code == IssueCodes.UnsupportedNumber && i.Message.Contains("confidence"));
        Assert.Contains(Check(c, "We have 42% confidence here."), i => i.Code == IssueCodes.UnsupportedNumber);
        // A z-value is not a confidence score: writing the statistic as "confidence" is rejected.
        var z = c.DeterministicMetrics.First(m => m.Unit == "Statistic").Current.ToString("0.##", CultureInfo.InvariantCulture);
        Assert.Contains(Check(c, $"Confidence is {z}."), i => i.Code == IssueCodes.UnsupportedNumber && i.Message.Contains("confidence"));
    }

    [Fact]
    public void Labelled_values_after_a_colon_are_claims_not_identifiers()
    {
        var c = Shift;
        Assert.Contains(Check(c, "Possession: 75%."), i => i.Code == IssueCodes.UnsupportedNumber && i.Message.Contains("75%"));
        Assert.Contains(Check(c, "Shots: 23."), i => i.Code == IssueCodes.UnsupportedNumber);
        Assert.DoesNotContain(Check(c, "Reference id:17 in the log."), Numeric);
    }

    [Theory]
    [InlineData("The ratio settled at 0.37.")]
    [InlineData("A value of 1.83 was recorded.")]
    [InlineData("Expected goals of 2.41 followed.")]
    public void Unrelated_decimals_are_rejected(string text) =>
        Assert.Contains(Check(Shift, text), i => i.Code == IssueCodes.UnsupportedNumber);

    [Fact]
    public void Plain_numbers_must_be_metric_values_not_samples_windows_or_event_times()
    {
        var c = Progressive;
        Assert.DoesNotContain(Check(c, "Progressive share reached 0.75."), Numeric);
        Assert.Contains(Check(c, "The index stood at 12."), i => i.Code == IssueCodes.UnsupportedNumber); // 12 is only a baseline sample size
    }

    [Fact]
    public void Verbatim_deterministic_sentences_are_grounded_by_construction()
    {
        foreach (var type in new[] { InsightType.AttackChannelShift, InsightType.Goal })
        {
            var c = Ctx(type);
            Assert.Empty(Check(c, c.DeterministicNarrative.Fact));
            Assert.Empty(Check(c, c.DeterministicNarrative.Ticker));
        }
    }

    private static string Words(int n)
    {
        string[] units = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen",
            "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
        string[] tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];
        return n < 20 ? units[n] : n % 10 == 0 ? tens[n / 10] : $"{tens[n / 10]}-{units[n % 10]}";
    }
}
