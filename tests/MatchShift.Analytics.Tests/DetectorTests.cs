using MatchShift.Analytics.Detection;
using MatchShift.Analytics.Metrics;
using MatchShift.Analytics.State;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Clubs;
using MatchShift.SyntheticData.Scenarios;
using static MatchShift.Analytics.Tests.EventScript;

namespace MatchShift.Analytics.Tests;

public class DetectorTests
{
    private static readonly Match Flagship = SyntheticMatchGenerator.GenerateFlagship().Match;
    private static readonly LiveMatchAnalyzer FlagshipAnalysis = LiveMatchAnalyzer.AnalyzeFull(Flagship);

    [Fact]
    public void Analysis_is_deterministic()
    {
        var again = LiveMatchAnalyzer.AnalyzeFull(Flagship);
        Assert.Equal(
            FlagshipAnalysis.Insights.Select(i => (i.Id, i.Type, i.TeamId, i.DetectedAtSecond)),
            again.Insights.Select(i => (i.Id, i.Type, i.TeamId, i.DetectedAtSecond)));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(63)]
    [InlineData(75)]
    public void Live_analysis_never_looks_ahead(int minute)
    {
        // Replaying to minute N must give exactly the insights the full run had raised by minute N.
        var partial = LiveMatchAnalyzer.AnalyzeUntil(Flagship, minute * 60);
        var expected = FlagshipAnalysis.Insights.Where(i => i.DetectedAtSecond <= minute * 60)
            .Select(i => (i.Id, i.Type, i.DetectedAtSecond)).ToList();
        Assert.Equal(expected, partial.Insights.Select(i => (i.Id, i.Type, i.DetectedAtSecond)));
    }

    [Fact]
    public void Flagship_discovers_the_scripted_right_side_shift_after_the_substitution()
    {
        var shift = Assert.Single(FlagshipAnalysis.Insights,
            i => i.Type == InsightType.AttackChannelShift && i.TeamId == ClubCatalog.WesthavenId);
        Assert.Equal("Right", shift.Fact("channel"));
        Assert.InRange(shift.DisplayMinute, 58, 70);
        Assert.Equal("true", shift.Fact("afterSubstitution"));
        Assert.Contains("WHA-17", shift.RelatedPlayerIds);
        Assert.True(shift.PrimaryEvidence!.RecentValue - shift.PrimaryEvidence.BaselineValue >= 0.25);
    }

    [Fact]
    public void Flagship_discovers_its_key_patterns()
    {
        var truth = FlagshipScenario.GroundTruth();
        bool Found(InsightType type, string team, int from, int to) => FlagshipAnalysis.Insights.Any(i =>
            i.Type == type && (team == "*" || i.TeamId == team) && i.DisplayMinute >= from && i.DisplayMinute <= to);

        Assert.True(Found(InsightType.ControlShift, ClubCatalog.NorthbridgeId, 15, 34), "Northbridge control");
        Assert.True(Found(InsightType.PressureIncrease, ClubCatalog.NorthbridgeId, 26, 40), "Northbridge pressure");
        Assert.True(Found(InsightType.PostSubstitutionChange, ClubCatalog.WesthavenId, 58, 72), "Post-substitution change");
        Assert.True(Found(InsightType.PressureIncrease, ClubCatalog.WesthavenId, 64, 76), "Westhaven pressure");
        Assert.True(Found(InsightType.DangerousTurnover, ClubCatalog.NorthbridgeId, 72, 74), "Northbridge turnover at 72'");
        Assert.True(Found(InsightType.PressureIncrease, ClubCatalog.NorthbridgeId, 75, 88), "Northbridge response");
        Assert.Equal(4, FlagshipAnalysis.Insights.Count(i => i.Type == InsightType.Goal));
        Assert.NotEmpty(truth.ExpectedPatterns);
    }

    [Fact]
    public void Shift_detectors_are_silent_during_warmup()
    {
        Assert.DoesNotContain(FlagshipAnalysis.Insights,
            i => i.Type.IsShift() && i.DetectedAtSecond < DetectionOptions.Default.WarmupMinutes * 60);
    }

    [Fact]
    public void Same_type_and_team_respects_the_cooldown()
    {
        foreach (var group in FlagshipAnalysis.Insights.Where(i => i.Type.IsShift()).GroupBy(i => (i.Type, i.TeamId)))
        {
            var times = group.Select(i => i.DetectedAtSecond).OrderBy(t => t).ToList();
            for (var k = 1; k < times.Count; k++)
                Assert.True(times[k] - times[k - 1] >= DetectionOptions.Default.CooldownMinutes * 30,
                    $"{group.Key} repeated after {(times[k] - times[k - 1]) / 60} minutes");
        }
    }

    [Fact]
    public void Goal_insight_carries_scorer_assist_and_score()
    {
        var goal = FlagshipAnalysis.Insights.First(i => i.Type == InsightType.Goal);
        Assert.Equal(ClubCatalog.NorthbridgeId, goal.TeamId);
        Assert.Equal("NBR-09", goal.Fact("scorerId"));
        Assert.Equal("NBR-07", goal.Fact("assistId"));
        Assert.Equal("1", goal.Fact("homeScore"));
        Assert.Equal("0", goal.Fact("awayScore"));
        Assert.Equal(100, goal.Importance);
    }

    [Fact]
    public void Pressure_increase_requires_a_real_rise()
    {
        var script = new EventScript()
            .AttackingSpell(Home, 0, 40, rightShare: 0.33)
            .PressureSpell(Away, 40, 60);
        var analyzer = new LiveMatchAnalyzer(Context);
        foreach (var e in script.Events) analyzer.Ingest(e);
        analyzer.AdvanceTo(60 * 60);

        var rise = analyzer.Insights.FirstOrDefault(i => i.Type == InsightType.PressureIncrease && i.TeamId == Away);
        Assert.NotNull(rise);
        Assert.InRange(rise!.DisplayMinute, 41, 52);
        Assert.DoesNotContain(analyzer.Insights, i => i.Type == InsightType.PressureIncrease && i.TeamId == Home && i.DisplayMinute > 40);
    }

    [Fact]
    public void Stationary_matches_rarely_produce_channel_shifts()
    {
        // Negative control: behaviour never changes, so channel shifts can only come from chance.
        var matches = SyntheticMatchGenerator.GenerateDataset(20, 7000, ScenarioMix.Only(ScenarioKind.Stationary)).Matches;
        var falseAlarms = matches.Sum(m => LiveMatchAnalyzer.AnalyzeFull(m.Match)
            .Insights.Count(i => i.Type == InsightType.AttackChannelShift));
        Assert.True(falseAlarms <= 5, $"{falseAlarms} channel shifts in 20 stationary matches");
    }

    [Fact]
    public void Every_insight_has_traceable_evidence()
    {
        var ids = Flagship.Events.Select(e => e.Id).ToHashSet();
        foreach (var insight in FlagshipAnalysis.Insights)
        {
            Assert.NotEmpty(insight.Evidence);
            Assert.NotNull(insight.PrimaryEvidence);
            Assert.NotEmpty(insight.SupportingEventIds);
            Assert.All(insight.SupportingEventIds, id => Assert.Contains(id, ids));
            Assert.All(insight.Visuals.SelectMany(v => v.EventIds), id => Assert.Contains(id, ids));
            Assert.InRange(insight.ConfidenceScore, 0, 1);
            Assert.True(insight.Span.FromSecond <= insight.Span.ToSecond);
            Assert.True(insight.Span.FromSecond <= insight.DetectedAtSecond);
        }
    }

    [Fact]
    public void Channel_evidence_can_be_recomputed_from_events()
    {
        // Evidence is not decoration: recomputing the metric over the cited windows gives the cited numbers.
        var shift = FlagshipAnalysis.Insights.First(i => i.Type == InsightType.AttackChannelShift);
        var primary = shift.PrimaryEvidence!;
        var state = MatchState.FromEvents(MatchContext.From(Flagship), Flagship.Events.Where(e => e.MatchSecond < shift.DetectedAtSecond));
        var baseline = MetricsCalculator.Compute(state, new AnalysisWindow(primary.BaselineWindow!.FromSecond, primary.BaselineWindow.ToSecond)).For(shift.TeamId!);
        var recent = MetricsCalculator.Compute(state, new AnalysisWindow(primary.RecentWindow!.FromSecond, primary.RecentWindow.ToSecond)).For(shift.TeamId!);
        var channel = Enum.Parse<AttackChannel>(shift.Fact("channel")!);

        Assert.Equal(primary.BaselineValue!.Value, Math.Round(baseline.ChannelShare(channel), 3), 3);
        Assert.Equal(primary.RecentValue, Math.Round(recent.ChannelShare(channel), 3), 3);
        Assert.Equal(primary.SampleSize, recent.Attacks);
    }

    [Fact]
    public void Insight_ids_are_unique_and_scoped_to_the_match()
    {
        Assert.Equal(FlagshipAnalysis.Insights.Count, FlagshipAnalysis.Insights.Select(i => i.Id).Distinct().Count());
        Assert.All(FlagshipAnalysis.Insights, i => Assert.StartsWith(Flagship.Id, i.Id));
    }

    [Fact]
    public void Timeline_has_one_sample_per_minute()
    {
        Assert.Equal(90, FlagshipAnalysis.Timeline.Count);
        Assert.Equal(Enumerable.Range(1, 90), FlagshipAnalysis.Timeline.Select(t => t.Minute));
        Assert.Equal((2, 2), (FlagshipAnalysis.Timeline[^1].HomeGoals, FlagshipAnalysis.Timeline[^1].AwayGoals));
    }
}
