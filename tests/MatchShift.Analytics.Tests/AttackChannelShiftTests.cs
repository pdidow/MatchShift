using MatchShift.Analytics.Detection;
using MatchShift.Analytics.Detection.Detectors;
using MatchShift.Core.Insights;
using static MatchShift.Analytics.Tests.EventScript;

namespace MatchShift.Analytics.Tests;

public class AttackChannelShiftTests
{
    private static readonly IShiftDetector[] OnlyChannel = [new AttackChannelShiftDetector()];

    private static IReadOnlyList<Insight> Run(EventScript script, int untilMinute = 90)
    {
        var analyzer = new LiveMatchAnalyzer(Context, detectors: OnlyChannel);
        foreach (var e in script.Events) analyzer.Ingest(e);
        analyzer.AdvanceTo(untilMinute * 60);
        return analyzer.Insights.Where(i => i.Type == InsightType.AttackChannelShift).ToList();
    }

    [Fact]
    public void Baseline_30_percent_right_to_recent_70_percent_triggers_a_right_shift()
    {
        var script = new EventScript()
            .AttackingSpell(Home, 0, 50, rightShare: 0.3)
            .AttackingSpell(Home, 50, 64, rightShare: 0.7);

        var insight = Assert.Single(Run(script, 64));
        Assert.Equal(Home, insight.TeamId);
        Assert.Equal("Right", insight.Fact("channel"));
        Assert.InRange(insight.DetectedAtSecond / 60, 52, 64);

        var primary = insight.PrimaryEvidence!;
        Assert.Equal("RightAttackShare", primary.Metric);
        Assert.InRange(primary.BaselineValue!.Value, 0.25, 0.35);
        Assert.InRange(primary.RecentValue, 0.6, 0.8);
        Assert.True(primary.SampleRequirementMet);
        Assert.True(primary.Difference >= primary.Threshold);
    }

    [Fact]
    public void Stable_channel_mix_never_triggers()
    {
        var script = new EventScript().AttackingSpell(Home, 0, 90, rightShare: 0.3);
        Assert.Empty(Run(script));
    }

    [Fact]
    public void Small_fluctuation_does_not_trigger()
    {
        var script = new EventScript()
            .AttackingSpell(Home, 0, 50, rightShare: 0.3)
            .AttackingSpell(Home, 50, 70, rightShare: 0.42);
        Assert.Empty(Run(script, 70));
    }

    [Fact]
    public void Large_change_with_too_few_attacks_does_not_trigger()
    {
        var script = new EventScript().AttackingSpell(Home, 0, 45, rightShare: 0.3);
        // Only five attacks in the recent window, all down the right: dramatic, but not enough evidence.
        for (var i = 0; i < 5; i++)
        {
            script.At((52 + i * 2) * 60)
                .Possession(Home).Pass(40, 85, 60, 85).Pass(60, 85, 75, 85).Pass(75, 85, 80, 85, complete: false)
                .Possession(Away).Pass(20, 15, 45, 50);
        }
        Assert.Empty(Run(script, 62));
    }

    [Fact]
    public void Shift_is_reported_once_per_cooldown()
    {
        var script = new EventScript()
            .AttackingSpell(Home, 0, 50, rightShare: 0.3)
            .AttackingSpell(Home, 50, 75, rightShare: 0.8);
        var insights = Run(script, 75);
        Assert.Single(insights);
    }

    [Fact]
    public void Left_shift_is_detected_for_the_away_team()
    {
        var script = new EventScript()
            .AttackingSpell(Away, 0, 50, rightShare: 0.35, leftShare: 0.3)
            .AttackingSpell(Away, 50, 64, rightShare: 0.15, leftShare: 0.75);
        var insight = Assert.Single(Run(script, 64));
        Assert.Equal(Away, insight.TeamId);
        Assert.Equal("Left", insight.Fact("channel"));
    }

    [Fact]
    public void Supporting_events_are_the_attacks_on_the_shifted_flank()
    {
        var script = new EventScript()
            .AttackingSpell(Home, 0, 50, rightShare: 0.3)
            .AttackingSpell(Home, 50, 64, rightShare: 0.7);
        var insight = Assert.Single(Run(script, 64));
        var state = script.ToState();
        Assert.NotEmpty(insight.SupportingEventIds);
        Assert.All(insight.SupportingEventIds, id =>
        {
            var e = state.FindEvent(id);
            Assert.NotNull(e);
            Assert.Equal(Home, e!.TeamId);
            Assert.True(e.EndY >= 66.7, $"Event {id} is not on the right");
        });
    }

    [Fact]
    public void Two_proportion_z_and_normal_cdf_are_sane()
    {
        Assert.Equal(0.5, Statistics.NormalCdf(0), 6);
        Assert.Equal(0.975, Statistics.NormalCdf(1.96), 3);
        Assert.Equal(0.025, Statistics.NormalCdf(-1.96), 3);
        Assert.True(Statistics.TwoProportionZ(0.3, 40, 0.7, 20) > 2.8);
        Assert.True(Statistics.TwoProportionZ(0.3, 40, 0.35, 20) < 0.5);
        Assert.Equal(0, Statistics.TwoProportionZ(0.3, 0, 0.7, 20));
    }

    [Theory]
    [InlineData(1.0, Severity.Low)]
    [InlineData(1.4, Severity.Medium)]
    [InlineData(2.0, Severity.High)]
    public void Severity_scales_with_threshold_exceedance(double ratio, Severity expected) =>
        Assert.Equal(expected, Statistics.SeverityOf(ratio));
}
