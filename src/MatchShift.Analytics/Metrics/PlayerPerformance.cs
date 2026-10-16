using MatchShift.Analytics.State;
using MatchShift.Core.Domain;

namespace MatchShift.Analytics.Metrics;

public sealed record PlayerPerformance(string PlayerId, int Passes, int Completed, int Progressive,
    int Shots, int Recoveries, int Turnovers, IReadOnlyList<MatchEvent> Events,
    IReadOnlyList<string> PassIds, IReadOnlyList<string> CompletedIds, IReadOnlyList<string> ProgressiveIds,
    IReadOnlyList<string> ShotIds, IReadOnlyList<string> RecoveryIds, IReadOnlyList<string> TurnoverIds)
{
    public static PlayerPerformance Calculate(MatchState state, string playerId)
    {
        var involved = state.Events.Where(e => e.PlayerId == playerId || e.RelatedPlayerId == playerId).ToArray();
        var own = involved.Where(e => e.PlayerId == playerId).ToArray();
        string[] Ids(Func<MatchEvent, bool> predicate) => own.Where(predicate).Select(e => e.Id).ToArray();
        var passes = Ids(e => e.Type == EventType.Pass);
        var completed = Ids(e => e.Type == EventType.Pass && e.Outcome == EventOutcome.Complete);
        var progressive = Ids(MetricsCalculator.IsProgressivePass);
        var shots = Ids(e => e.Type == EventType.Shot);
        var recoveries = Ids(e => e.Type is EventType.Interception or EventType.BallRecovery ||
            e.Type == EventType.Tackle && e.Outcome == EventOutcome.Won);
        var turnovers = state.Losses.Where(l => l.PlayerId == playerId).Select(l => l.EventId).Distinct().ToArray();
        return new(playerId, passes.Length, completed.Length, progressive.Length, shots.Length, recoveries.Length,
            turnovers.Length, involved, passes, completed, progressive, shots, recoveries, turnovers);
    }
}
