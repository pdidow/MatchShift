using MatchShift.Analytics.Metrics;
using MatchShift.Analytics.Story;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Clubs;

namespace MatchShift.Analytics.Tests;

public class StoryTests
{
    private static readonly Match Flagship = SyntheticMatchGenerator.GenerateFlagship().Match;
    private static readonly LiveMatchAnalyzer Analysis = LiveMatchAnalyzer.AnalyzeFull(Flagship);
    private static readonly RecapFacts HalfTime = RecapBuilder.Build(Analysis, RecapPeriod.HalfTime);
    private static readonly RecapFacts FullTime = RecapBuilder.Build(Analysis, RecapPeriod.FullTime);

    [Fact]
    public void Recap_score_matches_goal_events()
    {
        Assert.Equal(Flagship.ScoreAt(MatchClock.SecondHalfStartSecond - 1), new Score(HalfTime.HomeScore, HalfTime.AwayScore));
        Assert.Equal(Flagship.ScoreAt(MatchClock.FullTimeSecond), new Score(FullTime.HomeScore, FullTime.AwayScore));
        Assert.Null(FullTime.Winner);
    }

    [Fact]
    public void Recap_goals_match_the_event_stream_exactly()
    {
        var goalEvents = Flagship.Events.Where(e => e.Type == EventType.Goal).ToList();
        Assert.Equal(goalEvents.Select(g => (g.TeamId, g.PlayerId)), FullTime.Goals.Select(g => (g.TeamId, g.ScorerId)));
        Assert.Equal(goalEvents.Select(g => g.MatchMinute + 1), FullTime.Goals.Select(g => g.DisplayMinute));
        Assert.Equal(2, HalfTime.Goals.Count);
        Assert.Equal((2, 2), (FullTime.Goals[^1].HomeScore, FullTime.Goals[^1].AwayScore));
    }

    [Fact]
    public void Half_time_recap_never_uses_second_half_information()
    {
        Assert.All(HalfTime.KeyShifts, i => Assert.True(i.DetectedAtSecond <= MatchClock.SecondHalfStartSecond));
        Assert.All(HalfTime.TopHighlights, h => Assert.True(h.Second <= MatchClock.SecondHalfStartSecond));
        Assert.All(HalfTime.MomentumPeriods, m => Assert.True(m.ToMinute <= 45));
        Assert.All(HalfTime.Story, b => Assert.True(b.Minute <= 46));
    }

    [Fact]
    public void Recap_team_stats_equal_the_metrics_engine()
    {
        var m = MetricsCalculator.Compute(Analysis.State, new AnalysisWindow(0, MatchClock.FullTimeSecond));
        Assert.Equal(m.Home.Shots, FullTime.Home.Shots);
        Assert.Equal(m.Away.Shots, FullTime.Away.Shots);
        Assert.Equal(Math.Round(m.Home.PossessionShare, 3), FullTime.Home.PossessionShare);
        Assert.Equal(m.Away.Attacks, FullTime.Away.Attacks);
        Assert.Equal(Math.Round(m.Away.RightShare, 3), FullTime.Away.RightAttackShare);
        Assert.Equal(1.0, FullTime.Home.PossessionShare + FullTime.Away.PossessionShare, 2);
    }

    [Fact]
    public void Recap_tells_the_flagship_story_in_order()
    {
        Assert.Contains(FullTime.MomentumPeriods, p => p.TeamId == ClubCatalog.NorthbridgeId && p.FromMinute < 35 && p.ToMinute > 25);
        Assert.Contains(FullTime.MomentumPeriods, p => p.TeamId == ClubCatalog.WesthavenId && p.FromMinute >= 55 && p.FromMinute < 75);
        Assert.Contains(FullTime.KeyShifts, i => i.Type == InsightType.AttackChannelShift && i.TeamId == ClubCatalog.WesthavenId);
        Assert.Equal(FullTime.Story.OrderBy(b => b.Minute).Select(b => b.Minute), FullTime.Story.Select(b => b.Minute));
        Assert.Equal(4, FullTime.Story.Count(b => b.Code == "goal"));
    }

    [Fact]
    public void Key_players_are_ranked_by_a_transparent_score()
    {
        Assert.NotEmpty(FullTime.KeyPlayers);
        Assert.Equal(FullTime.KeyPlayers.OrderByDescending(p => p.Score).Select(p => p.PlayerId), FullTime.KeyPlayers.Select(p => p.PlayerId));
        foreach (var p in FullTime.KeyPlayers)
        {
            var expected = p.Goals * 10 + p.Assists * 6 + p.KeyPasses * 2 + p.Shots + p.ChanceQuality * 5 + p.Recoveries * 0.5 + p.InsightMentions * 3;
            Assert.True(Math.Abs(expected - p.Score) <= 0.1, $"{p.PlayerId}: {expected} vs {p.Score}");
            Assert.NotNull(Flagship.FindPlayer(p.PlayerId));
        }
        var scorers = FullTime.Goals.Select(g => g.ScorerId).ToHashSet();
        Assert.Equal(scorers.Count, Flagship.Events.Where(e => e.Type == EventType.Goal).Select(e => e.PlayerId).Distinct().Count());
    }

    [Fact]
    public void Momentum_periods_require_sustained_control()
    {
        var timeline = Enumerable.Range(1, 30).Select(m => new TimelinePoint(m, m is >= 12 and <= 20 ? 70 : 50, 0, 0, 0,
            Indices.ChaosState.Balanced, 0, 0)).ToList();
        var periods = RecapBuilder.MomentumPeriods(timeline, "H", "A");
        var p = Assert.Single(periods);
        Assert.Equal("H", p.TeamId);
        Assert.Equal(70, p.AverageControl);

        var blip = Enumerable.Range(1, 30).Select(m => new TimelinePoint(m, m is >= 12 and <= 14 ? 70 : 50, 0, 0, 0,
            Indices.ChaosState.Balanced, 0, 0)).ToList();
        Assert.Empty(RecapBuilder.MomentumPeriods(blip, "H", "A"));
    }

    [Fact]
    public void Goals_rank_above_everything_and_late_equaliser_ranks_first()
    {
        var highlights = HighlightRanker.Rank(Analysis);
        Assert.Equal(HighlightKind.Goal, highlights[0].Kind);
        Assert.Equal(87, highlights[0].DisplayMinute - 1);
        Assert.Equal("goal_equaliser", highlights[0].WhyItMattersCode);
        Assert.All(highlights.Take(4), h => Assert.Equal(HighlightKind.Goal, h.Kind));
        Assert.Equal(highlights.OrderByDescending(h => h.ImportanceScore).Select(h => h.Id), highlights.Select(h => h.Id));
    }

    [Fact]
    public void Highlights_classify_goals_by_what_they_did_to_the_score()
    {
        var goals = HighlightRanker.Rank(Analysis).Where(h => h.Kind == HighlightKind.Goal).OrderBy(h => h.Second).ToList();
        Assert.Equal(["goal_opener", "goal_equaliser", "goal_go_ahead", "goal_equaliser"], goals.Select(g => g.WhyItMattersCode));
    }

    [Fact]
    public void Highlights_carry_evidence_and_relevant_events()
    {
        var ids = Flagship.Events.Select(e => e.Id).ToHashSet();
        foreach (var h in HighlightRanker.Rank(Analysis))
        {
            Assert.NotEmpty(h.Evidence);
            Assert.NotEmpty(h.RelevantEventIds);
            Assert.All(h.RelevantEventIds, id => Assert.Contains(id, ids));
            Assert.InRange(h.ImportanceScore, 0, 100);
        }
    }

    [Fact]
    public void A_sequence_produces_one_highlight_not_several()
    {
        var highlights = HighlightRanker.Rank(Analysis);
        foreach (var group in highlights.Where(h => h.Kind is HighlightKind.BigChance or HighlightKind.DangerousTurnover).GroupBy(h => h.TeamId))
        {
            var times = group.Select(h => h.Second).OrderBy(s => s).ToList();
            for (var i = 1; i < times.Count; i++) Assert.True(times[i] - times[i - 1] > HighlightRanker.MergeWindowSeconds);
        }
    }
}
