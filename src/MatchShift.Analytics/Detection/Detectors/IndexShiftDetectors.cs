using MatchShift.Analytics.Indices;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Detection.Detectors;

/// <summary>
/// PRESSURE_INCREASE / PRESSURE_DECREASE on the MatchShift Pressure Index
/// (recent vs previous window). An increase must also leave the team clearly
/// ahead of the opponent's current pressure, so mutual end-to-end spells do not
/// read as one-sided pressure.
/// </summary>
public sealed class PressureShiftDetector : IShiftDetector
{
    public InsightType Type => InsightType.PressureIncrease;

    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        var o = ctx.Options.Pressure;
        var (bw, rw, comparison) = ctx.Compare(o.RecentMinutes, o.BaselineMinutes);

        foreach (var team in ctx.Match.TeamIds)
        {
            var opponent = ctx.Match.Opponent(team);
            var before = PressureIndex.Compute(bw.For(team));
            var now = PressureIndex.Compute(rw.For(team));
            var opponentNow = PressureIndex.Compute(rw.For(opponent));
            var diff = now.Value - before.Value;
            var events = rw.For(team).EventCount;
            if (events < o.MinRecentEvents) continue;

            var r = rw.For(team);
            var supporting = r.EntryIds.Concat(r.ShotIds).Concat(r.HighRecoveryIds).Concat(r.CornerIds).ToList();

            if (diff >= o.MinIncrease && now.Value >= o.MinRecentPressure && now.Value - opponentNow.Value >= o.MinMarginOverOpponent)
            {
                var gd = ctx.State.GoalDifference(team, ctx.Now);
                yield return new DetectionCandidate
                {
                    Type = InsightType.PressureIncrease,
                    TeamId = team,
                    Span = DetectionContext.Ref(comparison.Recent),
                    Severity = Statistics.SeverityOf(diff / o.MinIncrease),
                    ConfidenceScore = Statistics.ConfidenceFromMargin(diff / o.MinIncrease, events),
                    Evidence =
                    [
                        EvidenceFactory.Change("PressureIndex", MetricUnit.Index, before.Value, now.Value, comparison, o.MinIncrease,
                            events, bw.For(team).EventCount, o.MinRecentEvents, primary: true),
                        EvidenceFactory.Value("OpponentPressureIndex", MetricUnit.Index, opponentNow.Value, comparison.Recent),
                        .. Components(bw.For(team), r, comparison),
                    ],
                    SupportingEventIds = supporting,
                    Facts = new Dictionary<string, string> { ["direction"] = "increase", ["goalDifference"] = gd.ToString() },
                    Visuals =
                    [
                        new VisualLayer("entries", VisualStyle.Routes, r.EntryIds),
                        new VisualLayer("highRecoveries", VisualStyle.Points, r.HighRecoveryIds),
                        new VisualLayer("shots", VisualStyle.Shots, r.ShotIds),
                    ],
                    InterpretationCode = gd < 0 ? "chasing_game" : gd > 0 ? "pressing_lead" : "seizing_momentum",
                    KeyPlayerId = ctx.MostInvolvedPlayer(supporting),
                };
            }
            else if (diff <= -o.MinIncrease && before.Value >= o.DecreaseFromAtLeast && now.Value <= o.DecreaseToAtMost)
            {
                var gd = ctx.State.GoalDifference(team, ctx.Now);
                yield return new DetectionCandidate
                {
                    Type = InsightType.PressureDecrease,
                    TeamId = team,
                    Span = DetectionContext.Ref(comparison.Recent),
                    Severity = Statistics.SeverityOf(-diff / o.MinIncrease),
                    ConfidenceScore = Statistics.ConfidenceFromMargin(-diff / o.MinIncrease, events),
                    Evidence =
                    [
                        EvidenceFactory.Change("PressureIndex", MetricUnit.Index, before.Value, now.Value, comparison, -o.MinIncrease,
                            events, bw.For(team).EventCount, o.MinRecentEvents, primary: true),
                        .. Components(bw.For(team), r, comparison),
                    ],
                    SupportingEventIds = bw.For(team).EntryIds.Concat(bw.For(team).ShotIds).ToList(),
                    Facts = new Dictionary<string, string> { ["direction"] = "decrease", ["goalDifference"] = gd.ToString() },
                    Visuals = [new VisualLayer("entriesBefore", VisualStyle.Routes, bw.For(team).EntryIds)],
                    InterpretationCode = gd > 0 ? "protecting_lead" : "pushed_back",
                };
            }
        }
    }

    internal static IEnumerable<EvidenceItem> Components(Metrics.TeamWindowMetrics before, Metrics.TeamWindowMetrics now,
        Metrics.WindowComparison comparison)
    {
        yield return EvidenceFactory.Change("AttackingThirdEntries", MetricUnit.Count, before.FinalThirdEntries, now.FinalThirdEntries,
            comparison, 0, now.FinalThirdEntries, before.FinalThirdEntries, 0);
        yield return EvidenceFactory.Change("Shots", MetricUnit.Count, before.Shots, now.Shots, comparison, 0, now.Shots, before.Shots, 0);
        yield return EvidenceFactory.Change("HighRecoveries", MetricUnit.Count, before.HighRecoveries, now.HighRecoveries, comparison, 0,
            now.HighRecoveries, before.HighRecoveries, 0);
        yield return EvidenceFactory.Change("AverageActionX", MetricUnit.Index, before.AverageX, now.AverageX, comparison, 0,
            now.EventCount, before.EventCount, 0);
    }
}

/// <summary>
/// CONTROL_SHIFT: Match Control (10-minute window) moves by at least
/// <see cref="ControlOptions.MinChange"/> points towards a team that is now clearly in control.
/// </summary>
public sealed class ControlShiftDetector : IShiftDetector
{
    public InsightType Type => InsightType.ControlShift;

    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        var o = ctx.Options.Control;
        var (bw, rw, comparison) = ctx.Compare(o.RecentMinutes, o.BaselineMinutes);
        var before = MatchControl.Compute(bw);
        var now = MatchControl.Compute(rw);
        var change = now.HomeControl - before.HomeControl;
        if (Math.Abs(change) < o.MinChange) yield break;

        var team = change > 0 ? ctx.Match.HomeTeamId : ctx.Match.AwayTeamId;
        if (now.ControlFor(team) < o.MinRecentControl) yield break;

        var sample = rw.TotalEvents;
        var evidence = new List<EvidenceItem>
        {
            EvidenceFactory.Change("MatchControl", MetricUnit.Index, before.ControlFor(team), now.ControlFor(team), comparison, o.MinChange,
                sample, bw.TotalEvents, 40, primary: true),
        };
        foreach (var (b, n) in before.Components.Zip(now.Components))
        {
            var bShare = team == ctx.Match.HomeTeamId ? b.RawValue : 1 - b.RawValue;
            var nShare = team == ctx.Match.HomeTeamId ? n.RawValue : 1 - n.RawValue;
            evidence.Add(EvidenceFactory.Change($"Control.{n.Key}", MetricUnit.Share, bShare, nShare, comparison, 0, sample, bw.TotalEvents, 0));
        }

        var r = rw.For(team);
        var supporting = r.EntryIds.Concat(r.ShotIds).Concat(r.RecoveryIds).ToList();
        yield return new DetectionCandidate
        {
            Type = Type,
            TeamId = team,
            Span = DetectionContext.Ref(comparison.Recent),
            Severity = Statistics.SeverityOf(Math.Abs(change) / o.MinChange),
            ConfidenceScore = Statistics.ConfidenceFromMargin(Math.Abs(change) / o.MinChange, sample, 120),
            Evidence = evidence,
            SupportingEventIds = supporting,
            Facts = new Dictionary<string, string>
            {
                ["previousLeader"] = before.Leader ?? "none",
                ["goalDifference"] = ctx.State.GoalDifference(team, ctx.Now).ToString(),
            },
            Visuals =
            [
                new VisualLayer("entries", VisualStyle.Routes, r.EntryIds),
                new VisualLayer("recoveries", VisualStyle.Points, r.RecoveryIds),
                new VisualLayer("shots", VisualStyle.Shots, r.ShotIds),
            ],
            InterpretationCode = before.Leader is not null && before.Leader != team ? "control_swing" : "control_gained",
        };
    }
}

/// <summary>CHAOS_INCREASE: the Control-vs-Chaos index enters CHAOTIC after a calmer spell.</summary>
public sealed class ChaosIncreaseDetector : IShiftDetector
{
    public InsightType Type => InsightType.ChaosIncrease;

    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        var o = ctx.Options.Chaos;
        var (bw, rw, comparison) = ctx.Compare(o.RecentMinutes, o.BaselineMinutes);
        var before = ChaosIndex.Compute(bw);
        var now = ChaosIndex.Compute(rw);
        var diff = now.Value - before.Value;
        if (now.State != ChaosState.Chaotic || before.Value > o.MaxBaseline || diff < o.MinIncrease) yield break;

        var evidence = new List<EvidenceItem>
        {
            EvidenceFactory.Change("ChaosIndex", MetricUnit.Index, before.Value, now.Value, comparison, o.MinIncrease,
                rw.TotalEvents, bw.TotalEvents, 40, primary: true),
        };
        foreach (var (b, n) in before.Components.Zip(now.Components))
            evidence.Add(EvidenceFactory.Change($"Chaos.{n.Key}", MetricUnit.Quality, b.RawValue, n.RawValue, comparison, 0, rw.TotalEvents, bw.TotalEvents, 0));

        var supporting = rw.Home.TurnoverIds.Concat(rw.Away.TurnoverIds)
            .Concat(rw.Home.FastTransitionIds).Concat(rw.Away.FastTransitionIds)
            .Concat(rw.Home.ShotIds).Concat(rw.Away.ShotIds).Distinct().ToList();
        yield return new DetectionCandidate
        {
            Type = Type,
            TeamId = null,
            Span = DetectionContext.Ref(comparison.Recent),
            Severity = Statistics.SeverityOf(diff / o.MinIncrease),
            ConfidenceScore = Statistics.ConfidenceFromMargin(diff / o.MinIncrease, rw.TotalEvents, 120),
            Evidence = evidence,
            SupportingEventIds = supporting,
            Facts = new Dictionary<string, string> { ["state"] = "CHAOTIC", ["previousState"] = before.State.ToString().ToUpperInvariant() },
            Visuals =
            [
                new VisualLayer("turnovers", VisualStyle.Points, rw.Home.TurnoverIds.Concat(rw.Away.TurnoverIds).ToList()),
                new VisualLayer("transitions", VisualStyle.Routes, rw.Home.FastTransitionIds.Concat(rw.Away.FastTransitionIds).ToList()),
                new VisualLayer("shots", VisualStyle.Shots, rw.Home.ShotIds.Concat(rw.Away.ShotIds).ToList()),
            ],
            InterpretationCode = "open_end_to_end",
        };
    }
}
