using MatchShift.Core.Domain;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Clubs;
using MatchShift.SyntheticData.Scenarios;

namespace MatchShift.SyntheticData.Tests;

public class EventIntegrityTests
{
    public static TheoryData<string> Matches() => new() { "flagship", "seed:7", "seed:99", "scenario:SubstitutionImpact:5" };

    private static Match Load(string key)
    {
        var parts = key.Split(':');
        return parts[0] switch
        {
            "flagship" => SyntheticMatchGenerator.GenerateFlagship().Match,
            "seed" => SyntheticMatchGenerator.GenerateMatch(long.Parse(parts[1])),
            _ => SyntheticMatchGenerator.GenerateScenario(Enum.Parse<ScenarioKind>(parts[1]), long.Parse(parts[2])).Match,
        };
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void Events_are_ordered_and_clock_never_goes_backwards(string key)
    {
        var match = Load(key);
        for (var i = 1; i < match.Events.Count; i++)
        {
            Assert.Equal(match.Events[i - 1].SequenceNumber + 1, match.Events[i].SequenceNumber);
            Assert.True(match.Events[i].MatchSecond >= match.Events[i - 1].MatchSecond,
                $"Clock went backwards at {match.Events[i]}");
        }
        Assert.True(match.Events[^1].MatchSecond < MatchClock.FullTimeSecond);
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void Event_ids_are_unique_and_scoped_to_the_match(string key)
    {
        var match = Load(key);
        Assert.Equal(match.Events.Count, match.Events.Select(e => e.Id).Distinct().Count());
        Assert.All(match.Events, e => Assert.Equal(match.Id, e.MatchId));
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void Coordinates_are_normalized(string key)
    {
        var match = Load(key);
        foreach (var e in match.Events)
        {
            foreach (var v in new[] { e.StartX, e.StartY, e.EndX, e.EndY })
            {
                if (v is null) continue;
                Assert.InRange(v.Value, 0, 100);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void Players_in_events_belong_to_the_acting_team(string key)
    {
        var match = Load(key);
        foreach (var e in match.Events.Where(e => e.PlayerId is not null))
        {
            var player = match.FindPlayer(e.PlayerId);
            Assert.NotNull(player);
            Assert.Equal(e.TeamId, player!.TeamId);
        }
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void Periods_are_consistent_with_the_clock(string key)
    {
        var match = Load(key);
        Assert.All(match.Events, e => Assert.Equal(MatchClock.PeriodOf(e.MatchSecond), e.Period));
        Assert.Equal(2, match.Events.Count(e => e.Type == EventType.PeriodStart));
        Assert.Equal(2, match.Events.Count(e => e.Type == EventType.PeriodEnd));
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void Every_goal_is_preceded_by_a_scoring_shot(string key)
    {
        var match = Load(key);
        for (var i = 0; i < match.Events.Count; i++)
        {
            if (match.Events[i].Type != EventType.Goal) continue;
            var shot = match.Events[i - 1];
            Assert.Equal(EventType.Shot, shot.Type);
            Assert.Equal(EventOutcome.Goal, shot.Outcome);
            Assert.Equal(shot.TeamId, match.Events[i].TeamId);
        }
    }

    [Fact]
    public void Match_volume_is_football_realistic()
    {
        var match = SyntheticMatchGenerator.GenerateMatch(31);
        Assert.InRange(match.Events.Count, 1000, 2200);
        foreach (var team in new[] { match.HomeTeam, match.AwayTeam })
        {
            var passes = match.Events.Count(e => e.TeamId == team.Id && e.Type == EventType.Pass);
            var completed = match.Events.Count(e => e.TeamId == team.Id && e.Type == EventType.Pass && e.Outcome == EventOutcome.Complete);
            var shots = match.Events.Count(e => e.TeamId == team.Id && e.Type == EventType.Shot);
            Assert.InRange(passes, 250, 650);
            Assert.InRange(completed / (double)passes, 0.65, 0.92);
            Assert.InRange(shots, 2, 30);
        }
    }

    [Fact]
    public void Squads_are_complete_and_fictional()
    {
        foreach (var team in ClubCatalog.AllTeams())
        {
            Assert.Equal(18, team.Squad.Count);
            Assert.Equal(11, team.Starters.Count());
            Assert.Single(team.Starters, p => p.Position == Position.Goalkeeper);
            Assert.Equal(18, team.Squad.Select(p => p.ShirtNumber).Distinct().Count());
            Assert.Equal(18, team.Squad.Select(p => p.Id).Distinct().Count());
            Assert.All(team.Squad, p => Assert.Equal(team.Id, p.TeamId));
        }
    }

    [Fact]
    public void Dataset_clubs_are_generated_deterministically()
    {
        var a = ClubCatalog.ById("RVR");
        var b = ClubCatalog.ById("RVR");
        Assert.Equal(a.Squad.Select(p => p.Name), b.Squad.Select(p => p.Name));
    }
}
