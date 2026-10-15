using MatchShift.Analytics.Metrics;

namespace MatchShift.Analytics.Tests;

public class RollingWindowTests
{
    [Fact]
    public void Last_window_ends_now_and_clamps_at_kickoff()
    {
        Assert.Equal(new AnalysisWindow(3300, 3600), AnalysisWindow.Last(3600, 5));
        Assert.Equal(new AnalysisWindow(0, 120), AnalysisWindow.Last(120, 5));
    }

    [Fact]
    public void Before_window_is_the_span_preceding_the_recent_window()
    {
        // "last 5 vs previous 10" at 60'
        var c = WindowComparison.Last5VsPrevious10(3600);
        Assert.Equal(new AnalysisWindow(2700, 3300), c.Baseline);
        Assert.Equal(new AnalysisWindow(3300, 3600), c.Recent);
        Assert.Equal(c.Baseline.ToSecond, c.Recent.FromSecond);
    }

    [Fact]
    public void Last10_vs_previous15_and_match_baseline()
    {
        var c = WindowComparison.Last10VsPrevious15(65 * 60);
        Assert.Equal(AnalysisWindow.OfMinutes(40, 55), c.Baseline);
        Assert.Equal(AnalysisWindow.OfMinutes(55, 65), c.Recent);

        var m = WindowComparison.RecentVsMatch(65 * 60, 10);
        Assert.Equal(AnalysisWindow.OfMinutes(0, 55), m.Baseline);
    }

    [Fact]
    public void Substitution_comparison_splits_at_the_substitution()
    {
        var c = WindowComparison.AroundSubstitution(55 * 60, 65 * 60);
        Assert.Equal(AnalysisWindow.OfMinutes(40, 55), c.Baseline);
        Assert.Equal(AnalysisWindow.OfMinutes(55, 65), c.Recent);
    }

    [Fact]
    public void Window_is_half_open()
    {
        var w = new AnalysisWindow(60, 120);
        Assert.True(w.Contains(60));
        Assert.True(w.Contains(119));
        Assert.False(w.Contains(120));
        Assert.Equal("1'-2'", w.Label);
        Assert.Equal(1, w.Minutes);
    }

    [Fact]
    public void Index_range_uses_window_bounds()
    {
        var script = new EventScript().Possession(EventScript.Home);
        for (var s = 0; s < 600; s += 10) script.At(s).Pass(40, 50, 50, 50, advance: 0);
        var (start, end) = MetricsCalculator.IndexRange(script.Events, new AnalysisWindow(100, 200));
        Assert.Equal(10, start);
        Assert.Equal(20, end);
        Assert.All(script.Events.Skip(start).Take(end - start), e => Assert.InRange(e.MatchSecond, 100, 199));
    }

    [Fact]
    public void Metrics_only_count_events_inside_the_window()
    {
        var script = new EventScript()
            .AttackingSpell(EventScript.Home, 0, 10, rightShare: 0.3)
            .AttackingSpell(EventScript.Home, 10, 20, rightShare: 0.8);
        var state = script.ToState();

        var early = MetricsCalculator.Compute(state, AnalysisWindow.OfMinutes(0, 10)).Home;
        var late = MetricsCalculator.Compute(state, AnalysisWindow.OfMinutes(10, 20)).Home;

        Assert.InRange(early.RightShare, 0.25, 0.4);
        Assert.InRange(late.RightShare, 0.75, 0.9);
        Assert.Equal(state.AttackingActions.Count(a => a.TeamId == EventScript.Home),
            early.AttackingThirdActions + late.AttackingThirdActions);
    }
}
