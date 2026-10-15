using MatchShift.Analytics.Indices;
using MatchShift.Analytics.Metrics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Story;

/// <summary>
/// Builds <see cref="RecapFacts"/> for half-time or full-time. Pure and deterministic:
/// every number in a recap comes from here, never from a language model.
/// </summary>
public static class RecapBuilder
{
    /// <summary>Match Control (10-minute rolling) at or beyond which a team "had the momentum".</summary>
    public const double MomentumControlThreshold = 60;

    /// <summary>Minimum length in minutes of a momentum period.</summary>
    public const int MinMomentumMinutes = 6;

    private static readonly InsightType[] ShiftTypes =
    [
        InsightType.AttackChannelShift, InsightType.PostSubstitutionChange, InsightType.PressureIncrease,
        InsightType.CounterAttackThreat, InsightType.ChaosIncrease, InsightType.ShotPressure, InsightType.TempoShift,
    ];

    public static RecapFacts Build(LiveMatchAnalyzer analysis, RecapPeriod period)
    {
        var upTo = period == RecapPeriod.HalfTime ? MatchClock.SecondHalfStartSecond : MatchClock.FullTimeSecond;
        var state = analysis.State;
        var ctx = state.Context;
        var insights = analysis.Insights.Where(i => i.DetectedAtSecond <= upTo).ToList();
        var timeline = analysis.Timeline.Where(t => t.Minute * 60 <= upTo).ToList();
        var whole = MetricsCalculator.Compute(state, new AnalysisWindow(0, upTo));
        var highlights = HighlightRanker.Rank(analysis, upTo);

        var goals = insights.Where(i => i.Type == InsightType.Goal)
            .Select(i => new GoalSummary(i.Span.ToSecond - 1, i.TeamId!, i.Fact("scorerId"), i.Fact("assistId"), i.Id, i.InterpretationCode,
                int.Parse(i.Fact("homeScore") ?? "0"), int.Parse(i.Fact("awayScore") ?? "0")))
            .ToList();

        var momentum = MomentumPeriods(timeline, ctx.HomeTeamId, ctx.AwayTeamId);
        var keyShifts = insights.Where(i => ShiftTypes.Contains(i.Type))
            .OrderByDescending(i => i.Importance).ThenBy(i => i.DetectedAtSecond).Take(5)
            .OrderBy(i => i.DetectedAtSecond).ToList();
        var controlChanges = insights.Where(i => i.Type == InsightType.ControlShift).ToList();

        var peaks = ctx.TeamIds.Select(team =>
        {
            var isHome = team == ctx.HomeTeamId;
            var best = timeline.OrderByDescending(t => isHome ? t.HomePressure : t.AwayPressure).ThenBy(t => t.Minute).FirstOrDefault();
            return best is null ? null : new PressurePeak(team, best.Minute, isHome ? best.HomePressure : best.AwayPressure);
        }).Where(p => p is not null).Cast<PressurePeak>().ToList();

        return new RecapFacts
        {
            MatchId = ctx.MatchId,
            Period = period,
            UpToSecond = upTo,
            HomeTeamId = ctx.HomeTeamId,
            AwayTeamId = ctx.AwayTeamId,
            HomeScore = state.GoalsBefore(ctx.HomeTeamId, upTo),
            AwayScore = state.GoalsBefore(ctx.AwayTeamId, upTo),
            Goals = goals,
            MomentumPeriods = momentum,
            KeyShifts = keyShifts,
            ControlChanges = controlChanges,
            PressurePeaks = peaks,
            TopHighlights = highlights.Take(6).ToList(),
            Home = TeamStats(state, whole.Home, timeline, true),
            Away = TeamStats(state, whole.Away, timeline, false),
            KeyPlayers = PlayerContributions(analysis, upTo, insights),
            Story = StoryBeats(goals, momentum, keyShifts, insights),
            ChaoticMinuteShare = timeline.Count == 0 ? 0 : Math.Round((double)timeline.Count(t => t.ChaosState == ChaosState.Chaotic) / timeline.Count, 3),
        };
    }

    /// <summary>Contiguous runs of minutes where one team's 10-minute Match Control is at or above the threshold.</summary>
    public static IReadOnlyList<MomentumPeriod> MomentumPeriods(IReadOnlyList<TimelinePoint> timeline, string homeId, string awayId)
    {
        var periods = new List<MomentumPeriod>();
        string? current = null;
        var start = 0;
        var run = new List<TimelinePoint>();

        void Close()
        {
            if (current is not null && run.Count >= MinMomentumMinutes)
            {
                var isHome = current == homeId;
                periods.Add(new MomentumPeriod(start, run[^1].Minute, current,
                    Math.Round(run.Average(t => isHome ? t.HomeControl : 100 - t.HomeControl), 1),
                    Math.Round(run.Max(t => isHome ? t.HomePressure : t.AwayPressure), 1)));
            }
            run.Clear();
            current = null;
        }

        foreach (var point in timeline.Where(t => t.Minute >= LiveMatchAnalyzer.TimelineControlWindowMinutes))
        {
            var leader = point.HomeControl >= MomentumControlThreshold ? homeId
                : 100 - point.HomeControl >= MomentumControlThreshold ? awayId
                : null;
            if (leader != current)
            {
                Close();
                if (leader is not null)
                {
                    current = leader;
                    start = Math.Max(0, point.Minute - LiveMatchAnalyzer.TimelineControlWindowMinutes / 2);
                }
            }
            if (leader is not null) run.Add(point);
        }
        Close();
        return periods;
    }

    private static TeamRecapStats TeamStats(Analytics.State.MatchState state, TeamWindowMetrics m, IReadOnlyList<TimelinePoint> timeline, bool isHome)
    {
        var pressures = timeline.Select(t => isHome ? t.HomePressure : t.AwayPressure).ToList();
        return new TeamRecapStats(
            m.TeamId,
            m.Goals,
            Math.Round(m.PossessionShare, 3),
            m.Shots,
            m.ShotsOnTarget,
            Math.Round(m.ChanceQuality, 2),
            m.BigChances,
            m.Attacks,
            Math.Round(m.LeftShare, 3),
            Math.Round(m.CentreShare, 3),
            Math.Round(m.RightShare, 3),
            m.Turnovers,
            m.DangerousTurnovers,
            m.HighRecoveries,
            pressures.Count == 0 ? 0 : Math.Round(pressures.Average(), 1),
            pressures.Count == 0 ? 0 : Math.Round(pressures.Max(), 1));
    }

    /// <summary>
    /// Transparent player contribution score:
    /// goals x10 + assists x6 + key passes x2 + shots x1 + chance quality x5 + recoveries x0.5 + insight mentions x3.
    /// </summary>
    public static IReadOnlyList<PlayerContribution> PlayerContributions(LiveMatchAnalyzer analysis, int upTo, IReadOnlyList<Insight> insights)
    {
        var state = analysis.State;
        var events = state.Events.Where(e => e.MatchSecond < upTo).ToList();
        var stats = new Dictionary<string, (string Team, int Goals, int Assists, int Shots, int KeyPasses, int Recoveries, double Quality, int Mentions)>();

        void Add(string? player, string team, Func<(string Team, int Goals, int Assists, int Shots, int KeyPasses, int Recoveries, double Quality, int Mentions),
            (string, int, int, int, int, int, double, int)> update)
        {
            if (string.IsNullOrEmpty(player)) return;
            var current = stats.TryGetValue(player, out var s) ? s : (team, 0, 0, 0, 0, 0, 0.0, 0);
            stats[player] = update(current);
        }

        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            switch (e.Type)
            {
                case EventType.Shot:
                {
                    var q = e is { StartX: not null, StartY: not null }
                        ? Analytics.State.ChanceQuality.Estimate(e.StartX.Value, e.StartY.Value, e.Qualifier == "Header") : 0;
                    Add(e.PlayerId, e.TeamId, s => (s.Team, s.Goals + (e.Outcome == EventOutcome.Goal ? 1 : 0), s.Assists, s.Shots + 1,
                        s.KeyPasses, s.Recoveries, s.Quality + q, s.Mentions));
                    // Key pass: the last completed pass to the shooter in the same possession.
                    for (var j = i - 1; j >= 0 && events[j].PossessionId == e.PossessionId; j--)
                    {
                        var p = events[j];
                        if (p.Type != EventType.Pass || p.TeamId != e.TeamId || p.Outcome != EventOutcome.Complete) continue;
                        if (p.RelatedPlayerId != e.PlayerId) break;
                        var isAssist = e.Outcome == EventOutcome.Goal;
                        Add(p.PlayerId, p.TeamId, s => (s.Team, s.Goals, s.Assists + (isAssist ? 1 : 0), s.Shots, s.KeyPasses + 1,
                            s.Recoveries, s.Quality, s.Mentions));
                        break;
                    }
                    break;
                }
                case EventType.Interception or EventType.BallRecovery:
                case EventType.Tackle when e.Outcome == EventOutcome.Won:
                    Add(e.PlayerId, e.TeamId, s => (s.Team, s.Goals, s.Assists, s.Shots, s.KeyPasses, s.Recoveries + 1, s.Quality, s.Mentions));
                    break;
            }
        }

        // Positive mentions only: being the player who lost the ball is not a contribution.
        foreach (var insight in insights.Where(i => i.KeyPlayerId is not null && i.TeamId is not null &&
                                                    i.Type is not (InsightType.Goal or InsightType.DangerousTurnover or InsightType.TurnoverSpike)))
        {
            Add(insight.KeyPlayerId, insight.TeamId!, s => (s.Team, s.Goals, s.Assists, s.Shots, s.KeyPasses, s.Recoveries, s.Quality, s.Mentions + 1));
        }

        return stats
            .Select(kv =>
            {
                var s = kv.Value;
                var score = s.Goals * 10 + s.Assists * 6 + s.KeyPasses * 2 + s.Shots + s.Quality * 5 + s.Recoveries * 0.5 + s.Mentions * 3;
                return new PlayerContribution(kv.Key, s.Team, s.Goals, s.Assists, s.Shots, s.KeyPasses, s.Recoveries,
                    Math.Round(s.Quality, 2), s.Mentions, Math.Round(score, 1));
            })
            .OrderByDescending(p => p.Score).ThenBy(p => p.PlayerId, StringComparer.Ordinal)
            .Take(6)
            .ToList();
    }

    private static IReadOnlyList<StoryBeat> StoryBeats(
        IReadOnlyList<GoalSummary> goals,
        IReadOnlyList<MomentumPeriod> momentum,
        IReadOnlyList<Insight> keyShifts,
        IReadOnlyList<Insight> insights)
    {
        var beats = new List<StoryBeat>();
        foreach (var m in momentum)
        {
            beats.Add(new StoryBeat(m.FromMinute, "momentum", m.TeamId, null, new Dictionary<string, string>
            {
                ["from"] = m.FromMinute.ToString(),
                ["to"] = m.ToMinute.ToString(),
            }));
        }
        foreach (var g in goals)
        {
            beats.Add(new StoryBeat(g.DisplayMinute, "goal", g.TeamId, g.InsightId, new Dictionary<string, string>
            {
                ["scorerId"] = g.ScorerId ?? "",
                ["homeScore"] = g.HomeScore.ToString(),
                ["awayScore"] = g.AwayScore.ToString(),
                ["how"] = g.InterpretationCode ?? "",
            }));
        }
        foreach (var s in keyShifts)
        {
            beats.Add(new StoryBeat(s.DisplayMinute, s.Type.Code().ToLowerInvariant(), s.TeamId, s.Id, s.Facts));
        }
        var turnover = insights.Where(i => i.Type == InsightType.DangerousTurnover).OrderByDescending(i => i.Importance).FirstOrDefault();
        if (turnover is not null)
            beats.Add(new StoryBeat(turnover.DisplayMinute, "dangerous_turnover", turnover.TeamId, turnover.Id, turnover.Facts));

        return beats.OrderBy(b => b.Minute).ThenBy(b => b.Code == "goal" ? 1 : 0).ToList();
    }
}
