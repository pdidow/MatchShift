using MatchShift.Core.Domain;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Clubs;
using MatchShift.SyntheticData.Scenarios;

namespace MatchShift.SyntheticData.Tests;

public class FlagshipScenarioTests
{
    private static readonly LabelledMatch Flagship = SyntheticMatchGenerator.GenerateFlagship();

    [Fact]
    public void Flagship_is_Northbridge_v_Westhaven()
    {
        Assert.Equal(ClubCatalog.NorthbridgeId, Flagship.Match.HomeTeam.Id);
        Assert.Equal(ClubCatalog.WesthavenId, Flagship.Match.AwayTeam.Id);
        Assert.Equal(FlagshipScenario.MatchId, Flagship.Match.Id);
    }

    [Fact]
    public void Flagship_score_line_follows_the_script()
    {
        var m = Flagship.Match;
        Assert.Equal(new Score(1, 1), m.ScoreAt(MatchClock.SecondHalfStartSecond - 1));
        Assert.Equal(new Score(2, 2), m.ScoreAt(MatchClock.FullTimeSecond));

        var goals = m.Events.Where(e => e.Type == EventType.Goal).ToList();
        Assert.Equal([34, 43, 74, 87], goals.Select(g => g.MatchMinute));
        Assert.Equal(
            [ClubCatalog.NorthbridgeId, ClubCatalog.WesthavenId, ClubCatalog.WesthavenId, ClubCatalog.NorthbridgeId],
            goals.Select(g => g.TeamId));
    }

    [Fact]
    public void Westhaven_substitution_happens_at_55()
    {
        var sub = Flagship.Match.Events.Single(e => e.Type == EventType.Substitution && e.TeamId == ClubCatalog.WesthavenId);
        Assert.Equal(55, sub.MatchMinute);
        Assert.Equal("WHA-11", sub.PlayerId);
        Assert.Equal("WHA-17", sub.RelatedPlayerId);
    }

    [Fact]
    public void Substituted_player_never_appears_after_leaving()
    {
        var sub = Flagship.Match.Events.Single(e => e.Type == EventType.Substitution && e.TeamId == ClubCatalog.WesthavenId);
        Assert.DoesNotContain(Flagship.Match.Events.Where(e => e.SequenceNumber > sub.SequenceNumber),
            e => e.PlayerId == "WHA-11" || e.RelatedPlayerId == "WHA-11");
        Assert.DoesNotContain(Flagship.Match.Events.Where(e => e.SequenceNumber < sub.SequenceNumber),
            e => e.PlayerId == "WHA-17");
    }

    [Fact]
    public void Northbridge_lose_the_ball_in_their_own_third_at_72()
    {
        var interception = Flagship.Match.Events.First(e =>
            e.Type == EventType.Interception && e.MatchMinute == 72 && e.TeamId == ClubCatalog.WesthavenId);
        var lostPass = Flagship.Match.Events[interception.SequenceNumber - 2];
        Assert.Equal(EventType.Pass, lostPass.Type);
        Assert.Equal(EventOutcome.Incomplete, lostPass.Outcome);
        Assert.Equal(ClubCatalog.NorthbridgeId, lostPass.TeamId);
        Assert.True(Pitch.IsInDefensiveThird(lostPass.EndX!.Value));
    }

    [Fact]
    public void Ground_truth_is_not_part_of_the_event_stream()
    {
        // The Match record carries events and metadata only; labels live in GroundTruth.
        var matchProperties = typeof(Match).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain("GroundTruth", matchProperties);
        Assert.DoesNotContain(typeof(MatchEvent).GetProperties(), p => p.Name.Contains("Shift", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(Flagship.GroundTruth.ExpectedPatterns);
    }
}
