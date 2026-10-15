using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Detection.Detectors;

/// <summary>POSSESSION_SHIFT: time-based possession share moves by at least the threshold.</summary>
public sealed class PossessionShiftDetector : IShiftDetector
{
    public InsightType Type => InsightType.PossessionShift;

    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        var o = ctx.Options.Possession;
        var (bw, rw, comparison) = ctx.Compare(o.RecentMinutes, o.BaselineMinutes);
        var home = ctx.Match.HomeTeamId;
        var diff = rw.Home.PossessionShare - bw.Home.PossessionShare;
        if (Math.Abs(diff) < o.MinShareChange) yield break;
        var team = diff > 0 ? home : ctx.Match.AwayTeamId;
        var r = rw.For(team);
        var b = bw.For(team);
        if (r.PossessionSeconds + rw.Against(team).PossessionSeconds < o.MinRecentPossessionSeconds) yield break;

        yield return new DetectionCandidate
        {
            Type = Type,
            TeamId = team,
            Span = DetectionContext.Ref(comparison.Recent),
            Severity = Statistics.SeverityOf(Math.Abs(diff) / o.MinShareChange),
            ConfidenceScore = Statistics.ConfidenceFromMargin(Math.Abs(diff) / o.MinShareChange, rw.TotalPossessions, 25),
            Evidence =
            [
                EvidenceFactory.Change("PossessionShare", MetricUnit.Share, b.PossessionShare, r.PossessionShare, comparison, o.MinShareChange,
                    rw.TotalPossessions, bw.TotalPossessions, 10, primary: true),
                EvidenceFactory.Change("PassesCompleted", MetricUnit.Count, b.PassesCompleted, r.PassesCompleted, comparison, 0,
                    r.PassesAttempted, b.PassesAttempted, 0),
                EvidenceFactory.Change("PassesPerPossession", MetricUnit.Quality, b.AveragePassesPerPossession, r.AveragePassesPerPossession,
                    comparison, 0, r.PossessionsStarted, b.PossessionsStarted, 0),
            ],
            SupportingEventIds = r.EntryIds,
            Visuals = [new VisualLayer("entries", VisualStyle.Routes, r.EntryIds)],
            InterpretationCode = "keeping_the_ball",
        };
    }
}

/// <summary>TERRITORIAL_SHIFT: a team's share of attacking-third passes (field tilt) rises sharply.</summary>
public sealed class TerritorialShiftDetector : IShiftDetector
{
    public InsightType Type => InsightType.TerritorialShift;

    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        var o = ctx.Options.Territory;
        var (bw, rw, comparison) = ctx.Compare(o.RecentMinutes, o.BaselineMinutes);
        if (rw.Home.FinalThirdPasses + rw.Away.FinalThirdPasses < o.MinFinalThirdPasses) yield break;
        var diff = rw.Home.FieldTilt - bw.Home.FieldTilt;
        if (Math.Abs(diff) < o.MinFieldTiltChange) yield break;
        var team = diff > 0 ? ctx.Match.HomeTeamId : ctx.Match.AwayTeamId;
        var r = rw.For(team);
        var b = bw.For(team);
        if (r.FieldTilt < o.MinRecentFieldTilt) yield break;

        var sample = rw.Home.FinalThirdPasses + rw.Away.FinalThirdPasses;
        yield return new DetectionCandidate
        {
            Type = Type,
            TeamId = team,
            Span = DetectionContext.Ref(comparison.Recent),
            Severity = Statistics.SeverityOf(Math.Abs(diff) / o.MinFieldTiltChange),
            ConfidenceScore = Statistics.ConfidenceFromMargin(Math.Abs(diff) / o.MinFieldTiltChange, sample, 40),
            Evidence =
            [
                EvidenceFactory.Change("FieldTilt", MetricUnit.Share, b.FieldTilt, r.FieldTilt, comparison, o.MinFieldTiltChange,
                    sample, bw.Home.FinalThirdPasses + bw.Away.FinalThirdPasses, o.MinFinalThirdPasses, primary: true),
                EvidenceFactory.Change("AverageActionX", MetricUnit.Index, b.AverageX, r.AverageX, comparison, 0, r.EventCount, b.EventCount, 0),
                EvidenceFactory.Change("AttackingThirdEntries", MetricUnit.Count, b.FinalThirdEntries, r.FinalThirdEntries, comparison, 0,
                    r.FinalThirdEntries, b.FinalThirdEntries, 0),
            ],
            SupportingEventIds = r.EntryIds,
            Visuals = [new VisualLayer("entries", VisualStyle.Routes, r.EntryIds)],
            InterpretationCode = "pinning_back",
        };
    }
}

/// <summary>TEMPO_SHIFT: actions per minute of possession change by at least the relative threshold.</summary>
public sealed class TempoShiftDetector : IShiftDetector
{
    public InsightType Type => InsightType.TempoShift;

    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        var o = ctx.Options.Tempo;
        var (bw, rw, comparison) = ctx.Compare(o.RecentMinutes, o.BaselineMinutes);
        foreach (var team in ctx.Match.TeamIds)
        {
            var r = rw.For(team);
            var b = bw.For(team);
            var recentActions = (int)Math.Round(r.Tempo * r.PossessionSeconds / 60);
            var baselineActions = (int)Math.Round(b.Tempo * b.PossessionSeconds / 60);
            if (recentActions < o.MinRecentActions || baselineActions < o.MinRecentActions || b.Tempo <= 0) continue;
            var relative = (r.Tempo - b.Tempo) / b.Tempo;
            if (Math.Abs(relative) < o.MinRelativeChange) continue;
            var gd = ctx.State.GoalDifference(team, ctx.Now);

            yield return new DetectionCandidate
            {
                Type = Type,
                TeamId = team,
                Variant = relative > 0 ? "up" : "down",
                Span = DetectionContext.Ref(comparison.Recent),
                Severity = Statistics.SeverityOf(Math.Abs(relative) / o.MinRelativeChange),
                ConfidenceScore = Statistics.ConfidenceFromMargin(Math.Abs(relative) / o.MinRelativeChange, recentActions, 60),
                Evidence =
                [
                    EvidenceFactory.Change("Tempo", MetricUnit.PerMinute, b.Tempo, r.Tempo, comparison, o.MinRelativeChange,
                        recentActions, baselineActions, o.MinRecentActions, primary: true),
                    EvidenceFactory.Value("TempoRelativeChange", MetricUnit.Share, relative, comparison.Recent, recentActions),
                    EvidenceFactory.Change("LongBalls", MetricUnit.Count, b.LongBalls, r.LongBalls, comparison, 0, r.PassesAttempted, b.PassesAttempted, 0),
                ],
                SupportingEventIds = r.EntryIds,
                Facts = new Dictionary<string, string> { ["direction"] = relative > 0 ? "up" : "down", ["goalDifference"] = gd.ToString() },
                Visuals = [new VisualLayer("entries", VisualStyle.Routes, r.EntryIds)],
                InterpretationCode = relative > 0 ? "raising_tempo" : gd > 0 ? "managing_game" : "slowing_down",
            };
        }
    }
}

/// <summary>SHOT_PRESSURE: a burst of shots well above the team's recent rate.</summary>
public sealed class ShotPressureDetector : IShiftDetector
{
    public InsightType Type => InsightType.ShotPressure;

    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        var o = ctx.Options.ShotPressure;
        var (bw, rw, comparison) = ctx.Compare(o.RecentMinutes, o.BaselineMinutes);
        foreach (var team in ctx.Match.TeamIds)
        {
            var r = rw.For(team);
            var b = bw.For(team);
            if (r.Shots < o.MinRecentShots || r.ChanceQuality < o.MinRecentChanceQuality) continue;
            var ratio = Statistics.RateRatio(r.Shots, comparison.Recent.Minutes, b.Shots, comparison.Baseline.Minutes);
            if (ratio < o.MinRateRatio) continue;

            yield return new DetectionCandidate
            {
                Type = Type,
                TeamId = team,
                Span = DetectionContext.Ref(comparison.Recent),
                Severity = Statistics.SeverityOf(ratio / o.MinRateRatio),
                ConfidenceScore = Statistics.ConfidenceFromMargin(ratio / o.MinRateRatio, r.Shots * 6),
                Evidence =
                [
                    EvidenceFactory.Change("Shots", MetricUnit.Count, b.Shots, r.Shots, comparison, o.MinRecentShots, r.Shots, b.Shots,
                        o.MinRecentShots, primary: true),
                    EvidenceFactory.Change("ChanceQuality", MetricUnit.Quality, b.ChanceQuality, r.ChanceQuality, comparison,
                        o.MinRecentChanceQuality, r.Shots, b.Shots, 0),
                    EvidenceFactory.Change("ShotsOnTarget", MetricUnit.Count, b.ShotsOnTarget, r.ShotsOnTarget, comparison, 0, r.Shots, b.Shots, 0),
                    EvidenceFactory.Value("ShotRateRatio", MetricUnit.Statistic, ratio, comparison.Recent, r.Shots, threshold: o.MinRateRatio),
                ],
                SupportingEventIds = r.ShotIds,
                Visuals = [new VisualLayer("shots", VisualStyle.Shots, r.ShotIds), new VisualLayer("box", VisualStyle.Routes, r.BoxEntryIds)],
                InterpretationCode = "goal_threat_building",
                KeyPlayerId = ctx.MostInvolvedPlayer(r.ShotIds),
            };
        }
    }
}

/// <summary>TURNOVER_SPIKE: a team gives the ball away far more often than in the previous spell.</summary>
public sealed class TurnoverSpikeDetector : IShiftDetector
{
    public InsightType Type => InsightType.TurnoverSpike;

    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        var o = ctx.Options.TurnoverSpike;
        var (bw, rw, comparison) = ctx.Compare(o.RecentMinutes, o.BaselineMinutes);
        foreach (var team in ctx.Match.TeamIds)
        {
            var r = rw.For(team);
            var b = bw.For(team);
            if (r.Turnovers < o.MinRecentTurnovers) continue;
            var ratio = Statistics.RateRatio(r.Turnovers, comparison.Recent.Minutes, b.Turnovers, comparison.Baseline.Minutes);
            if (ratio < o.MinRateRatio) continue;
            var opponent = rw.Against(team);

            yield return new DetectionCandidate
            {
                Type = Type,
                TeamId = team,
                Span = DetectionContext.Ref(comparison.Recent),
                Severity = Statistics.SeverityOf(ratio / o.MinRateRatio),
                ConfidenceScore = Statistics.ConfidenceFromMargin(ratio / o.MinRateRatio, r.Turnovers * 4),
                Evidence =
                [
                    EvidenceFactory.Change("Turnovers", MetricUnit.Count, b.Turnovers, r.Turnovers, comparison, o.MinRecentTurnovers,
                        r.Turnovers, b.Turnovers, o.MinRecentTurnovers, primary: true),
                    EvidenceFactory.Value("TurnoverRateRatio", MetricUnit.Statistic, ratio, comparison.Recent, r.Turnovers, threshold: o.MinRateRatio),
                    EvidenceFactory.Value("DangerousTurnovers", MetricUnit.Count, r.DangerousTurnovers, comparison.Recent, r.Turnovers),
                    EvidenceFactory.Value("OpponentHighRecoveries", MetricUnit.Count, opponent.HighRecoveries, comparison.Recent, opponent.Recoveries),
                ],
                SupportingEventIds = r.TurnoverIds,
                Visuals = [new VisualLayer("turnovers", VisualStyle.Points, r.TurnoverIds)],
                InterpretationCode = opponent.HighRecoveries >= 3 ? "pressed_into_errors" : "loose_in_possession",
                KeyPlayerId = ctx.MostInvolvedPlayer(r.TurnoverIds),
            };
        }
    }
}

/// <summary>COUNTER_ATTACK_THREAT: repeated fast transitions from regains, at least one ending in a shot.</summary>
public sealed class CounterAttackThreatDetector : IShiftDetector
{
    public InsightType Type => InsightType.CounterAttackThreat;

    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        var o = ctx.Options.CounterAttack;
        var (bw, rw, comparison) = ctx.Compare(o.RecentMinutes, o.BaselineMinutes);
        foreach (var team in ctx.Match.TeamIds)
        {
            var r = rw.For(team);
            var b = bw.For(team);
            if (r.FastTransitions < o.MinRecentTransitions || r.CounterAttackShots < o.MinRecentCounterShots) continue;
            var ratio = Statistics.RateRatio(r.FastTransitions, comparison.Recent.Minutes, b.FastTransitions, comparison.Baseline.Minutes);
            if (ratio < o.MinRateRatio) continue;
            var opponentControl = rw.Against(team).PossessionShare;

            yield return new DetectionCandidate
            {
                Type = Type,
                TeamId = team,
                Span = DetectionContext.Ref(comparison.Recent),
                Severity = Statistics.SeverityOf(ratio / o.MinRateRatio),
                ConfidenceScore = Statistics.ConfidenceFromMargin(ratio / o.MinRateRatio, r.FastTransitions * 8),
                Evidence =
                [
                    EvidenceFactory.Change("FastTransitions", MetricUnit.Count, b.FastTransitions, r.FastTransitions, comparison,
                        o.MinRecentTransitions, r.FastTransitions, b.FastTransitions, o.MinRecentTransitions, primary: true),
                    EvidenceFactory.Value("CounterAttackShots", MetricUnit.Count, r.CounterAttackShots, comparison.Recent, r.FastTransitions),
                    EvidenceFactory.Value("OpponentPossessionShare", MetricUnit.Share, opponentControl, comparison.Recent),
                    EvidenceFactory.Value("TransitionRateRatio", MetricUnit.Statistic, ratio, comparison.Recent, r.FastTransitions, threshold: o.MinRateRatio),
                ],
                SupportingEventIds = r.FastTransitionIds,
                Visuals = [new VisualLayer("transitions", VisualStyle.Routes, r.FastTransitionIds), new VisualLayer("shots", VisualStyle.Shots, r.ShotIds)],
                InterpretationCode = opponentControl >= 0.55 ? "counter_while_opponent_dominates" : "exploiting_transitions",
                KeyPlayerId = ctx.MostInvolvedPlayer(r.FastTransitionIds),
            };
        }
    }
}
