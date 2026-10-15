using MatchShift.Analytics.Metrics;
using MatchShift.Analytics.State;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Detection.Detectors;

/// <summary>
/// Event-anchored key moments, evaluated as each event arrives:
///
/// - GOAL: every goal, with its build-up.
/// - BIG_CHANCE: a non-scoring shot with MatchShift Chance Quality ≥ <see cref="MomentOptions.BigChanceQuality"/>.
/// - DANGEROUS_TURNOVER: an open-play loss in the team's own half that the opponent punishes with
///   a shot within <see cref="MomentOptions.DangerousTurnoverConsequenceSeconds"/> (any real attempt
///   after an own-third loss; a good chance after a loss elsewhere in the own half).
/// </summary>
public sealed class MomentDetector
{
    public IEnumerable<DetectionCandidate> OnEvent(MatchState state, MatchEvent e, DetectionOptions options)
    {
        var o = options.Moments;

        if (e.Type == EventType.Goal)
        {
            var shot = state.Shots.LastOrDefault(s => s.TeamId == e.TeamId && s.IsGoal && s.Second <= e.MatchSecond);
            if (shot is not null) yield return Goal(state, e, shot);
        }

        var turnoverReported = false;
        if (e.Type == EventType.Shot)
        {
            foreach (var loss in state.Losses)
            {
                if (loss.TeamId == e.TeamId || !loss.InOwnHalf) continue;
                // Own-third losses are dangerous if punished by any real attempt; elsewhere in the
                // own half only if the resulting chance is a good one.
                var quality = state.Shots[^1].Quality;
                if (quality < (loss.InOwnDefensiveThird ? o.DangerousTurnoverMinQualityOwnThird : o.DangerousTurnoverMinQualityOwnHalf)) continue;
                if (loss.Second > e.MatchSecond || e.MatchSecond - loss.Second > o.DangerousTurnoverConsequenceSeconds) continue;
                turnoverReported = true;
                yield return DangerousTurnover(state, loss, e);
            }
        }

        // A chance created by a dangerous turnover is told once, as part of the turnover story.
        if (e.Type == EventType.Shot && e.Outcome != EventOutcome.Goal && !turnoverReported)
        {
            var shot = state.Shots[^1];
            if (shot.EventId == e.Id && shot.Quality >= o.BigChanceQuality) yield return BigChance(state, shot, o);
        }
    }

    private static DetectionCandidate Goal(MatchState state, MatchEvent goal, ShotRecord shot)
    {
        var possession = state.Possessions.LastOrDefault(p => p.Id == shot.PossessionId);
        var buildUp = possession is null
            ? [shot.EventId]
            : possession.EventIds.Where(id => state.FindEvent(id)?.TeamId == goal.TeamId).ToList();
        var assist = buildUp
            .Select(id => state.FindEvent(id)!)
            .LastOrDefault(ev => ev.Type == EventType.Pass && ev.Outcome == EventOutcome.Complete && ev.RelatedPlayerId == shot.PlayerId);
        var fastBreak = possession?.IsFastTransition() ?? false;
        var shotEvent = state.FindEvent(shot.EventId);
        var home = state.GoalsBefore(state.Context.HomeTeamId, goal.MatchSecond + 1);
        var away = state.GoalsBefore(state.Context.AwayTeamId, goal.MatchSecond + 1);

        var pressureBefore = Indices.PressureIndex.Compute(
            MetricsCalculator.Compute(state, AnalysisWindow.Last(goal.MatchSecond, 8)).For(goal.TeamId)).Value;

        var interpretation =
            shotEvent?.Qualifier == "Header" && buildUp.Any(id => state.FindEvent(id)?.Type == EventType.Corner) ? "goal_from_set_piece" :
            fastBreak ? "goal_on_counter" :
            pressureBefore >= 50 ? "goal_after_pressure" :
            "goal_open_play";

        var facts = new Dictionary<string, string>
        {
            ["scorerId"] = shot.PlayerId ?? "",
            ["homeScore"] = home.ToString(),
            ["awayScore"] = away.ToString(),
            ["fastBreak"] = fastBreak ? "true" : "false",
            ["header"] = shot.IsHeader ? "true" : "false",
        };
        if (assist?.PlayerId is not null) facts["assistId"] = assist.PlayerId;

        var start = possession?.StartSecond ?? shot.Second;
        return new DetectionCandidate
        {
            Type = InsightType.Goal,
            TeamId = goal.TeamId,
            AnchorEventId = goal.Id,
            Span = new TimeSpanRef(start, goal.MatchSecond + 1),
            Severity = Severity.High,
            ConfidenceScore = 0.99,
            Evidence =
            [
                EvidenceFactory.Value("ChanceQuality", MetricUnit.Quality, shot.Quality, primary: true),
                EvidenceFactory.Value("BuildUpPasses", MetricUnit.Count, possession?.PassCount ?? 0),
                EvidenceFactory.Value("BuildUpSeconds", MetricUnit.Seconds, Math.Max(0, shot.Second - start)),
                EvidenceFactory.Value("PressureIndexBefore", MetricUnit.Index, pressureBefore, AnalysisWindow.Last(goal.MatchSecond, 8)),
            ],
            SupportingEventIds = buildUp,
            Facts = facts,
            Visuals = [new VisualLayer("buildUp", VisualStyle.Routes, buildUp), new VisualLayer("shot", VisualStyle.Shots, [shot.EventId])],
            InterpretationCode = interpretation,
            KeyPlayerId = shot.PlayerId,
            RelatedPlayerIds = assist?.PlayerId is null ? [] : [assist.PlayerId],
        };
    }

    private static DetectionCandidate BigChance(MatchState state, ShotRecord shot, MomentOptions o)
    {
        var possession = state.Possessions.LastOrDefault(p => p.Id == shot.PossessionId);
        var buildUp = possession is null
            ? [shot.EventId]
            : possession.EventIds.Where(id => state.FindEvent(id)?.TeamId == shot.TeamId).ToList();
        var shotEvent = state.FindEvent(shot.EventId)!;
        var start = possession?.StartSecond ?? shot.Second;
        return new DetectionCandidate
        {
            Type = InsightType.BigChance,
            TeamId = shot.TeamId,
            AnchorEventId = shot.EventId,
            Span = new TimeSpanRef(start, shot.Second + 1),
            Severity = shot.Quality >= 0.35 ? Severity.High : Severity.Medium,
            ConfidenceScore = 0.95,
            Evidence =
            [
                EvidenceFactory.Value("ChanceQuality", MetricUnit.Quality, shot.Quality, primary: true, threshold: o.BigChanceQuality),
                EvidenceFactory.Value("BuildUpPasses", MetricUnit.Count, possession?.PassCount ?? 0),
            ],
            SupportingEventIds = buildUp,
            Facts = new Dictionary<string, string>
            {
                ["shooterId"] = shot.PlayerId ?? "",
                ["outcome"] = shotEvent.Outcome.ToString(),
                ["header"] = shot.IsHeader ? "true" : "false",
            },
            Visuals = [new VisualLayer("buildUp", VisualStyle.Routes, buildUp), new VisualLayer("shot", VisualStyle.Shots, [shot.EventId])],
            InterpretationCode = shotEvent.Outcome == EventOutcome.OnTarget ? "big_chance_saved" : "big_chance_missed",
            KeyPlayerId = shot.PlayerId,
        };
    }

    private static DetectionCandidate DangerousTurnover(MatchState state, PossessionLoss loss, MatchEvent consequence)
    {
        var opponent = state.Context.Opponent(loss.TeamId);
        var followUp = state.Events
            .Where(ev => ev.TeamId == opponent && ev.MatchSecond >= loss.Second && ev.SequenceNumber <= consequence.SequenceNumber)
            .Select(ev => ev.Id)
            .ToList();
        var shot = consequence.Type == EventType.Shot ? state.Shots.LastOrDefault(s => s.EventId == consequence.Id) : null;
        var evidence = new List<EvidenceItem>
        {
            EvidenceFactory.Value("LossLocationX", MetricUnit.Index, loss.X, primary: true, threshold: Pitch.DefensiveThirdEnd),
            EvidenceFactory.Value("SecondsToConsequence", MetricUnit.Seconds, consequence.MatchSecond - loss.Second),
        };
        if (shot is not null) evidence.Add(EvidenceFactory.Value("ChanceQuality", MetricUnit.Quality, shot.Quality));

        return new DetectionCandidate
        {
            Type = InsightType.DangerousTurnover,
            TeamId = loss.TeamId,
            AnchorEventId = loss.EventId,
            Span = new TimeSpanRef(loss.Second, consequence.MatchSecond + 1),
            Severity = shot is not null && (shot.Quality >= 0.25 || shot.IsGoal) ? Severity.High : Severity.Medium,
            ConfidenceScore = 0.97,
            Evidence = evidence,
            SupportingEventIds = [loss.EventId, .. followUp],
            Facts = new Dictionary<string, string>
            {
                ["zone"] = loss.InOwnDefensiveThird ? "defensiveThird" : "ownHalf",
                ["consequence"] = shot?.IsGoal == true ? "goal" : "shot",
                ["lossKind"] = loss.Kind.ToString(),
                ["opponentId"] = opponent,
                ["playerId"] = loss.PlayerId ?? "",
            },
            Visuals =
            [
                new VisualLayer("loss", VisualStyle.Points, [loss.EventId]),
                new VisualLayer("punishment", VisualStyle.Routes, followUp),
                .. shot is null ? Array.Empty<VisualLayer>() : [new VisualLayer("shot", VisualStyle.Shots, [shot.EventId])],
            ],
            InterpretationCode = loss.Kind == LossKind.MisplacedPass ? "costly_pass" : "caught_in_possession",
            KeyPlayerId = loss.PlayerId,
        };
    }
}
