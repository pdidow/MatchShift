using MatchShift.Analytics.Detection;
using MatchShift.Analytics.Detection.Detectors;
using MatchShift.Analytics.Metrics;
using MatchShift.Core.Domain;

namespace MatchShift.Analytics.Tests;

public class PassingAndPlayerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Labelled_passing_change_is_detected_and_stationary_control_is_quiet(bool change)
    {
        var script = new EventScript().Possession(EventScript.Home);
        for (var minute = 0; minute < 24; minute++)
        for (var pass = 0; pass < 6; pass++)
            script.At(minute * 60 + pass * 9).Pass(20, 40, 60, 40, qualifier: change && minute >= 8 ? "LongBall" : null);
        var state = script.ToState();
        var findings = new PassingStyleShiftDetector().Evaluate(new DetectionContext(state, 16 * 60, DetectionOptions.Default)).ToArray();
        if (!change) { Assert.Empty(findings); return; }
        var finding = Assert.Single(findings);
        var evidence = Assert.Single(finding.Evidence);
        Assert.Equal(0, evidence.BaselineValue); Assert.Equal(1, evidence.RecentValue);
        Assert.Equal(48, evidence.SampleSize);
        Assert.Equal(96, finding.SupportingEventIds.Count);
        Assert.All(finding.SupportingEventIds, id => Assert.True(state.FindEvent(id)!.MatchSecond < 16 * 60));
    }

    [Fact]
    public void Small_samples_do_not_trigger_a_passing_change()
    {
        var state = new EventScript().Possession(EventScript.Home).AtMinute(1).Pass(20, 40, 60, 40)
            .AtMinute(10).Pass(20, 40, 60, 40, qualifier: "LongBall").ToState();
        Assert.Empty(new PassingStyleShiftDetector().Evaluate(new DetectionContext(state, 16 * 60, DetectionOptions.Default)));
    }

    [Fact]
    public void Player_totals_are_reproducible_from_exact_event_references()
    {
        var state = new EventScript().Possession(EventScript.Home).AtMinute(1)
            .Pass(20, 40, 60, 40).Pass(60, 40, 65, 40, complete: false)
            .Shot(80, 50).Recovery(EventScript.Home, 60, 30).ToState();
        var player = PlayerPerformance.Calculate(state, "H-P");
        Assert.Equal(2, player.Passes); Assert.Equal(1, player.Completed); Assert.Equal(1, player.Progressive);
        Assert.Equal(1, player.Shots); Assert.Equal(1, player.Recoveries);
        Assert.All(player.ProgressiveIds, id => Assert.True(MetricsCalculator.IsProgressivePass(state.FindEvent(id)!)));
        Assert.Equal(player.Passes, player.PassIds.Count);
        Assert.Equal(player.Events.Count, player.Events.Select(e => e.Id).Distinct().Count());
    }
}
