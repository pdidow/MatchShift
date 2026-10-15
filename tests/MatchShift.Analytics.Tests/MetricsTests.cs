using MatchShift.Analytics.Metrics;
using MatchShift.Analytics.State;
using MatchShift.Core.Domain;
using MatchShift.SyntheticData;
using static MatchShift.Analytics.Tests.EventScript;

namespace MatchShift.Analytics.Tests;

public class MetricsTests
{
    private static readonly AnalysisWindow All = new(0, 90 * 60);

    [Theory]
    [InlineData(10, AttackChannel.Left)]
    [InlineData(50, AttackChannel.Centre)]
    [InlineData(90, AttackChannel.Right)]
    public void Attacking_third_actions_are_classified_by_channel(double y, AttackChannel expected)
    {
        var state = new EventScript().Possession(Home).Pass(60, 50, 80, y).ToState();
        var action = Assert.Single(state.AttackingActions);
        Assert.Equal(expected, action.Channel);
        Assert.Equal(1, MetricsCalculator.Compute(state, All).Home.AttacksIn(expected));
    }

    [Fact]
    public void Channel_uses_the_attacking_teams_own_left_and_right()
    {
        // Same physical touchline, opposite teams: each team's events are already in its own frame.
        var state = new EventScript()
            .Possession(Home).Pass(60, 50, 80, 90)
            .Possession(Away).Pass(60, 50, 80, 90)
            .ToState();
        var m = MetricsCalculator.Compute(state, All);
        Assert.Equal(1, m.Home.RightAttacks);
        Assert.Equal(1, m.Away.RightAttacks);
    }

    [Fact]
    public void Incomplete_passes_and_corner_kicks_are_not_attacking_actions()
    {
        var state = new EventScript()
            .Possession(Home)
            .Pass(60, 50, 80, 90, complete: false)
            .Corner()
            .Pass(99.5, 0.5, 90, 50, qualifier: "Cross")
            .ToState();
        Assert.Empty(state.AttackingActions);
    }

    [Fact]
    public void Shots_count_as_attacking_actions_at_their_location()
    {
        var state = new EventScript().Possession(Home).Shot(88, 20).ToState();
        Assert.Equal(AttackChannel.Left, Assert.Single(state.AttackingActions).Channel);
    }

    [Fact]
    public void Final_third_entries_require_crossing_the_line()
    {
        var state = new EventScript()
            .Possession(Home)
            .Pass(50, 50, 70, 50)   // entry
            .Pass(70, 50, 80, 50)   // already inside: not an entry
            .Carry(60, 50, 68, 50)  // entry by carry
            .ToState();
        Assert.Equal(2, state.Entries.Count);
        Assert.Equal(2, MetricsCalculator.Compute(state, All).Home.FinalThirdEntries);
    }

    [Fact]
    public void Possession_share_is_time_based()
    {
        var script = new EventScript();
        script.At(0).Possession(Home);
        for (var i = 0; i < 6; i++) script.Pass(40, 50, 45, 50, advance: 10); // 60s of home possession
        script.Possession(Away);
        for (var i = 0; i < 3; i++) script.Pass(40, 50, 45, 50, advance: 10); // 30s of away possession
        script.Possession(Home).Pass(40, 50, 45, 50);

        var m = MetricsCalculator.Compute(script.ToState(), All);
        Assert.Equal(60, m.Home.PossessionSeconds);
        Assert.Equal(30, m.Away.PossessionSeconds);
        Assert.Equal(2.0 / 3.0, m.Home.PossessionShare, 3);
        Assert.Equal(1.0, m.Home.PossessionShare + m.Away.PossessionShare, 6);
    }

    [Fact]
    public void Misplaced_pass_followed_by_opponent_possession_is_a_turnover()
    {
        var state = new EventScript()
            .Possession(Home).Pass(40, 50, 60, 50).Pass(60, 50, 70, 30, complete: false)
            .Recovery(Away, 30, 70)
            .Possession(Away).Pass(30, 70, 40, 60)
            .ToState();

        var loss = Assert.Single(state.Losses);
        Assert.Equal(Home, loss.TeamId);
        Assert.Equal(70, loss.X);
        Assert.Equal(LossKind.MisplacedPass, loss.Kind);
        Assert.Equal(PossessionStartReason.Regain, state.Possessions[^1].StartReason);
    }

    [Fact]
    public void Possession_ending_in_a_shot_is_not_a_turnover()
    {
        var state = new EventScript()
            .Possession(Home).Pass(60, 50, 85, 50).Shot(85, 50)
            .Possession(Away).Pass(6, 50, 30, 50)
            .ToState();
        Assert.Empty(state.Losses);
        Assert.Equal(PossessionEndReason.Shot, state.Possessions[0].EndReason);
    }

    [Fact]
    public void Loss_in_own_defensive_third_is_dangerous()
    {
        var state = new EventScript()
            .Possession(Home).Pass(15, 50, 25, 40, complete: false)
            .Possession(Away).Pass(75, 60, 80, 60)
            .ToState();
        var m = MetricsCalculator.Compute(state, All);
        Assert.Equal(1, m.Home.Turnovers);
        Assert.Equal(1, m.Home.DangerousTurnovers);
    }

    [Fact]
    public void Own_half_loss_is_dangerous_only_if_the_opponent_shoots_quickly()
    {
        var quiet = new EventScript()
            .Possession(Home).Pass(35, 50, 45, 50, complete: false)
            .Possession(Away).Pass(55, 50, 60, 50, advance: 30).Shot(85, 50)
            .ToState();
        Assert.Equal(0, MetricsCalculator.Compute(quiet, All).Home.DangerousTurnovers);

        var punished = new EventScript()
            .Possession(Home).Pass(35, 50, 45, 50, complete: false)
            .Possession(Away).Pass(55, 50, 85, 50, advance: 5).Shot(85, 50)
            .ToState();
        Assert.Equal(1, MetricsCalculator.Compute(punished, All).Home.DangerousTurnovers);
    }

    [Fact]
    public void High_recoveries_are_counted_in_the_recovering_teams_frame()
    {
        var state = new EventScript()
            .Possession(Home).Pass(20, 50, 30, 50, complete: false)
            .Recovery(Away, 70, 50)
            .Possession(Away).Pass(70, 50, 80, 50)
            .ToState();
        var m = MetricsCalculator.Compute(state, All);
        Assert.Equal(1, m.Away.Recoveries);
        Assert.Equal(1, m.Away.HighRecoveries);
        Assert.Contains(m.Away.HighRecoveryIds, id => state.FindEvent(id)!.Type == EventType.Interception);
    }

    [Fact]
    public void Fast_transition_is_a_regain_that_reaches_the_attacking_third_quickly()
    {
        var state = new EventScript()
            .Possession(Home).Pass(50, 50, 60, 50, complete: false)
            .Possession(Away).Pass(40, 50, 60, 50, advance: 3).Pass(60, 50, 80, 50, advance: 3).Shot(85, 50)
            .ToState();
        var m = MetricsCalculator.Compute(state, All);
        Assert.Equal(1, m.Away.FastTransitions);
        Assert.Equal(1, m.Away.CounterAttackShots);
    }

    [Fact]
    public void Incremental_ingest_matches_batch_build()
    {
        var match = SyntheticMatchGenerator.GenerateMatch(5);
        var ctx = MatchContext.From(match);
        var incremental = new MatchState(ctx);
        foreach (var e in match.Events) incremental.Ingest(e);
        var batch = MatchState.FromEvents(ctx, match.Events);

        var a = MetricsCalculator.Compute(incremental, AnalysisWindow.OfMinutes(30, 60));
        var b = MetricsCalculator.Compute(batch, AnalysisWindow.OfMinutes(30, 60));
        Assert.Equivalent(a, b, strict: true);
    }

    [Fact]
    public void Ingest_rejects_out_of_order_events()
    {
        var events = new EventScript().Possession(Home).Pass(40, 50, 50, 50).Pass(50, 50, 60, 50).Events;
        var state = new MatchState(Context);
        state.Ingest(events[1]);
        Assert.Throws<InvalidOperationException>(() => state.Ingest(events[0]));
    }

    [Fact]
    public void Chance_quality_rewards_closer_and_more_central_shots()
    {
        Assert.True(ChanceQuality.Estimate(95, 50) > ChanceQuality.Estimate(85, 50));
        Assert.True(ChanceQuality.Estimate(88, 50) > ChanceQuality.Estimate(88, 20));
        Assert.True(ChanceQuality.Estimate(90, 50, header: true) < ChanceQuality.Estimate(90, 50));
        Assert.InRange(ChanceQuality.Estimate(70, 50), 0.01, 0.1);
        Assert.InRange(ChanceQuality.Estimate(99, 50), 0.4, 0.85);
    }
}
