using MatchShift.Core.Domain;

namespace MatchShift.Core.Tests;

public class PitchTests
{
    [Theory]
    [InlineData(0, AttackChannel.Left)]
    [InlineData(33.2, AttackChannel.Left)]
    [InlineData(33.4, AttackChannel.Centre)]
    [InlineData(50, AttackChannel.Centre)]
    [InlineData(66.6, AttackChannel.Centre)]
    [InlineData(66.7, AttackChannel.Right)]
    [InlineData(100, AttackChannel.Right)]
    public void Channel_is_classified_from_team_relative_y(double y, AttackChannel expected) =>
        Assert.Equal(expected, Pitch.ChannelOf(y));

    [Fact]
    public void Flip_moves_point_into_opponent_frame()
    {
        Assert.Equal((70.0, 20.0), Pitch.Flip(30, 80));
        Assert.Equal((30.0, 80.0), Pitch.Flip(70, 20));
    }

    [Fact]
    public void Absolute_frame_keeps_home_and_mirrors_away()
    {
        Assert.Equal((80.0, 10.0), Pitch.ToAbsolute(80, 10, isHomeTeam: true));
        Assert.Equal((20.0, 90.0), Pitch.ToAbsolute(80, 10, isHomeTeam: false));
    }

    [Theory]
    [InlineData(90, 50, true)]
    [InlineData(84, 22, true)]
    [InlineData(80, 50, false)]
    [InlineData(90, 15, false)]
    public void Penalty_area_detection(double x, double y, bool expected) =>
        Assert.Equal(expected, Pitch.IsInPenaltyArea(x, y));

    [Fact]
    public void Distance_to_goal_is_smaller_closer_to_goal()
    {
        Assert.True(Pitch.DistanceToGoal(95, 50) < Pitch.DistanceToGoal(80, 50));
        Assert.True(Pitch.DistanceToGoal(90, 50) < Pitch.DistanceToGoal(90, 10));
        Assert.Equal(0, Pitch.DistanceToGoal(100, 50), 6);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2699, 1)]
    [InlineData(2700, 2)]
    [InlineData(5399, 2)]
    public void Period_from_clock(int second, int period) => Assert.Equal(period, MatchClock.PeriodOf(second));

    [Fact]
    public void Display_minute_is_broadcast_style()
    {
        Assert.Equal(1, MatchClock.DisplayMinute(0));
        Assert.Equal(64, MatchClock.DisplayMinute(63 * 60 + 10));
        Assert.Equal(90, MatchClock.DisplayMinute(5399));
        Assert.Equal("63:10", MatchClock.Format(63 * 60 + 10));
    }
}
