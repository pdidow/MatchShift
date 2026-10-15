using MatchShift.Analytics.Indices;
using MatchShift.Analytics.Metrics;
using MatchShift.SyntheticData;
using static MatchShift.Analytics.Tests.EventScript;

namespace MatchShift.Analytics.Tests;

public class IndexTests
{
    private static WindowMetrics Spell(double homeRightShare = 0.33) =>
        MetricsCalculator.Compute(new EventScript().AttackingSpell(Home, 0, 10, homeRightShare).ToState(), AnalysisWindow.OfMinutes(0, 10));

    [Fact]
    public void Pressure_components_sum_to_the_index_and_stay_in_range()
    {
        var w = Spell();
        foreach (var team in new[] { w.Home, w.Away })
        {
            var p = PressureIndex.Compute(team);
            Assert.InRange(p.Value, 0, 100);
            Assert.Equal(p.Value, Math.Round(p.Components.Sum(c => c.Contribution), 1), 1);
            Assert.Equal(1.0, p.Components.Sum(c => c.Weight), 6);
        }
    }

    [Fact]
    public void Attacking_team_has_higher_pressure()
    {
        var w = Spell();
        Assert.True(PressureIndex.Compute(w.Home).Value > PressureIndex.Compute(w.Away).Value + 15);
    }

    [Fact]
    public void Control_is_fifty_for_an_empty_window_and_sums_to_one_hundred()
    {
        var empty = MetricsCalculator.Compute(new EventScript().ToState(), AnalysisWindow.OfMinutes(0, 10));
        var c = MatchControl.Compute(empty);
        Assert.Equal(50, c.HomeControl);
        Assert.Null(c.Leader);

        var w = MatchControl.Compute(Spell());
        Assert.Equal(100, w.HomeControl + w.AwayControl, 6);
        Assert.Equal(1.0, w.Components.Sum(x => x.Weight), 6);
    }

    [Fact]
    public void Dominant_team_controls_the_match()
    {
        var c = MatchControl.Compute(Spell());
        Assert.True(c.HomeControl > 60, $"Home control {c.HomeControl}");
        Assert.Equal(Home, c.Leader);
    }

    [Theory]
    [InlineData(10, ChaosState.Controlled)]
    [InlineData(24.9, ChaosState.Controlled)]
    [InlineData(25, ChaosState.Balanced)]
    [InlineData(54.9, ChaosState.Balanced)]
    [InlineData(55, ChaosState.Chaotic)]
    [InlineData(95, ChaosState.Chaotic)]
    public void Chaos_thresholds_are_documented_bands(double value, ChaosState expected) =>
        Assert.Equal(expected, ChaosIndex.Classify(value));

    [Fact]
    public void Frantic_end_to_end_play_is_more_chaotic_than_patient_possession()
    {
        var patient = new EventScript().Possession(Home);
        for (var i = 0; i < 60; i++) patient.Pass(40 + i % 5, 50, 45 + i % 5, 50, advance: 9);

        var frantic = new EventScript();
        for (var i = 0; i < 30; i++)
        {
            var team = i % 2 == 0 ? Home : Away;
            frantic.Possession(team).Pass(40, 50, 75, 50, qualifier: "LongBall", advance: 4).Shot(85, 50, advance: 4)
                .Possession(team == Home ? Away : Home).Pass(30, 50, 45, 50, complete: false, advance: 3);
        }

        var calm = ChaosIndex.Compute(MetricsCalculator.Compute(patient.ToState(), AnalysisWindow.OfMinutes(0, 9)));
        var wild = ChaosIndex.Compute(MetricsCalculator.Compute(frantic.ToState(), AnalysisWindow.OfMinutes(0, 9)));
        Assert.Equal(ChaosState.Controlled, calm.State);
        Assert.Equal(ChaosState.Chaotic, wild.State);
    }

    [Fact]
    public void Indices_are_deterministic_for_generated_matches()
    {
        var a = MetricsCalculator.Compute(State.MatchState.FromEvents(State.MatchContext.From(SyntheticMatchGenerator.GenerateMatch(9)),
            SyntheticMatchGenerator.GenerateMatch(9).Events), AnalysisWindow.OfMinutes(20, 40));
        var b = MetricsCalculator.Compute(State.MatchState.FromEvents(State.MatchContext.From(SyntheticMatchGenerator.GenerateMatch(9)),
            SyntheticMatchGenerator.GenerateMatch(9).Events), AnalysisWindow.OfMinutes(20, 40));
        Assert.Equal(MatchControl.Compute(a).HomeControl, MatchControl.Compute(b).HomeControl);
        Assert.Equal(ChaosIndex.Compute(a).Value, ChaosIndex.Compute(b).Value);
    }
}
