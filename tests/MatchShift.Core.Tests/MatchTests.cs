using MatchShift.Core.Domain;
using MatchShift.SyntheticData;

namespace MatchShift.Core.Tests;

public class MatchTests
{
    private static readonly Match Flagship = SyntheticMatchGenerator.GenerateFlagship().Match;

    [Fact]
    public void Score_is_computed_from_goal_events_up_to_the_clock()
    {
        Assert.Equal(new Score(0, 0), Flagship.ScoreAt(0));
        Assert.Equal(new Score(1, 0), Flagship.ScoreAt(40 * 60));
        Assert.Equal(new Score(2, 2), Flagship.ScoreAt(MatchClock.FullTimeSecond));
    }

    [Fact]
    public void Team_lookups_work_both_ways()
    {
        Assert.Same(Flagship.AwayTeam, Flagship.Opponent(Flagship.HomeTeam.Id));
        Assert.Same(Flagship.HomeTeam, Flagship.TeamById(Flagship.HomeTeam.Id));
        Assert.True(Flagship.IsHome(Flagship.HomeTeam.Id));
        Assert.Throws<ArgumentException>(() => Flagship.TeamById("NOPE"));
    }

    [Fact]
    public void Player_lookup_finds_players_from_either_squad()
    {
        Assert.Equal("Tavian Brooks", Flagship.FindPlayer("WHA-17")!.Name);
        Assert.Equal("Owen Blackwood", Flagship.FindPlayer("NBR-09")!.Name);
        Assert.Null(Flagship.FindPlayer("XXX-01"));
        Assert.Null(Flagship.FindPlayer(null));
    }

    [Fact]
    public void Matches_are_always_flagged_synthetic() => Assert.True(Flagship.IsSynthetic);
}
