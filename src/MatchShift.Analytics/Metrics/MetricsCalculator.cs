using MatchShift.Analytics.State;
using MatchShift.Core.Domain;

namespace MatchShift.Analytics.Metrics;

/// <summary>
/// Computes <see cref="WindowMetrics"/> for any window from a <see cref="MatchState"/>.
/// Pure and deterministic: the same events and window always give the same numbers.
/// </summary>
public static class MetricsCalculator
{
    /// <summary>Gaps longer than this (dead ball, injuries) do not count as possession time.</summary>
    public const int MaxPossessionGapSeconds = 20;

    /// <summary>A pass that moves the ball at least this far forward counts as a forward pass.</summary>
    public const double ForwardPassMinGain = 10;

    /// <summary>Opponent shot within this many seconds makes an own-half loss "dangerous".</summary>
    public const int DangerousLossShotWindowSeconds = 25;

    public const double HighRecoveryMinX = 60;

    public static WindowMetrics Compute(MatchState state, AnalysisWindow window)
    {
        var ctx = state.Context;
        var home = new Accumulator(ctx.HomeTeamId);
        var away = new Accumulator(ctx.AwayTeamId);
        Accumulator For(string teamId) => teamId == ctx.HomeTeamId ? home : away;

        var events = state.Events;
        var (start, end) = IndexRange(events, window);

        for (var i = start; i < end; i++)
        {
            var e = events[i];

            if (e.PossessionTeamId is not null && i + 1 < events.Count && events[i + 1].Period == e.Period)
            {
                var gap = Math.Min(events[i + 1].MatchSecond, window.ToSecond) - e.MatchSecond;
                For(e.PossessionTeamId).PossessionSeconds += Math.Clamp(gap, 0, MaxPossessionGapSeconds);
            }

            if (e.Type is EventType.PeriodStart or EventType.PeriodEnd or EventType.Substitution) continue;
            var acc = For(e.TeamId);
            acc.EventCount++;

            var onBall = e.Type is EventType.Pass or EventType.Carry or EventType.Shot;
            if (onBall && e.StartX is not null && e.PossessionTeamId == e.TeamId)
            {
                acc.SumX += e.StartX.Value;
                acc.OnBallActions++;
            }

            switch (e.Type)
            {
                case EventType.Pass:
                    acc.PassesAttempted++;
                    if (e.Outcome == EventOutcome.Complete) acc.PassesCompleted++;
                    if (e is { StartX: not null, EndX: not null })
                    {
                        var gain = e.EndX.Value - e.StartX.Value;
                        if (gain >= ForwardPassMinGain) acc.ForwardPasses++;
                        if (e.Outcome == EventOutcome.Complete &&
                            ((gain >= 15 && e.EndX.Value >= 50) ||
                             (e.EndY is not null && Pitch.IsInPenaltyArea(e.EndX.Value, e.EndY.Value) &&
                              !Pitch.IsInPenaltyArea(e.StartX.Value, e.StartY ?? 0))))
                            acc.ProgressivePasses++;
                        if (Pitch.IsInAttackingThird(e.StartX.Value)) acc.FinalThirdPasses++;
                    }
                    if (e.Qualifier == "LongBall") acc.LongBalls++;
                    acc.Actions++;
                    break;

                case EventType.Carry:
                    acc.Actions++;
                    break;

                case EventType.Shot:
                    acc.Shots++;
                    acc.ShotIds.Add(e.Id);
                    if (e.Outcome is EventOutcome.OnTarget or EventOutcome.Goal) acc.ShotsOnTarget++;
                    if (e.Outcome == EventOutcome.Goal) acc.Goals++;
                    if (e is { StartX: not null, StartY: not null })
                    {
                        var q = State.ChanceQuality.Estimate(e.StartX.Value, e.StartY.Value, e.Qualifier == "Header");
                        acc.ChanceQuality += q;
                        if (q >= State.ChanceQuality.BigChanceThreshold) acc.BigChances++;
                    }
                    break;

                case EventType.Corner:
                    acc.Corners++;
                    acc.CornerIds.Add(e.Id);
                    break;

                case EventType.Tackle when e.Outcome == EventOutcome.Won:
                case EventType.Interception:
                case EventType.BallRecovery:
                    acc.Recoveries++;
                    acc.RecoveryIds.Add(e.Id);
                    if (e.Type == EventType.Tackle) acc.Tackles++;
                    if (e.Type == EventType.Interception) acc.Interceptions++;
                    if (e.StartX >= HighRecoveryMinX)
                    {
                        acc.HighRecoveries++;
                        acc.HighRecoveryIds.Add(e.Id);
                    }
                    break;

                case EventType.Tackle:
                    acc.Tackles++;
                    break;

                case EventType.Foul:
                    acc.Fouls++;
                    break;
            }
        }

        foreach (var entry in state.Entries)
        {
            if (window.Contains(entry.Second)) For(entry.TeamId).EntryIds.Add(entry.EventId);
        }

        // Attacks: each attack's actions inside the window, classified by majority channel.
        foreach (var attack in state.AttackingActions
                     .Where(a => window.Contains(a.Second))
                     .GroupBy(a => (a.TeamId, a.AttackId)))
        {
            var acc = For(attack.Key.TeamId);
            var actions = attack.ToList();
            acc.AttackingThirdActions += actions.Count;
            var channel = ClassifyAttack(actions);
            (channel switch
            {
                AttackChannel.Left => acc.LeftAttackIds,
                AttackChannel.Centre => acc.CentreAttackIds,
                _ => acc.RightAttackIds,
            }).AddRange(actions.Select(a => a.EventId));
            acc.AttackChannels.Add(channel);
        }

        foreach (var box in state.BoxEntries)
        {
            if (window.Contains(box.Second)) For(box.TeamId).BoxEntryIds.Add(box.EventId);
        }

        foreach (var loss in state.Losses)
        {
            if (!window.Contains(loss.Second)) continue;
            var acc = For(loss.TeamId);
            acc.TurnoverIds.Add(loss.EventId);
            if (IsDangerous(state, loss)) acc.DangerousTurnoverIds.Add(loss.EventId);
        }

        foreach (var p in state.Possessions)
        {
            if (!window.Contains(p.StartSecond)) continue;
            var acc = For(p.TeamId);
            acc.PossessionsStarted++;
            acc.PossessionPasses += p.PassCount;
            if (p.IsFastTransition())
            {
                acc.FastTransitions++;
                foreach (var id in p.EventIds)
                {
                    var ev = state.FindEvent(id);
                    if (ev is not null && ev.TeamId == p.TeamId) acc.FastTransitionIds.Add(id);
                }
            }
            if (p.IsCounterAttackWithShot()) acc.CounterAttackShots++;
        }

        var totalPossession = home.PossessionSeconds + away.PossessionSeconds;
        var totalFinalThirdPasses = home.FinalThirdPasses + away.FinalThirdPasses;
        return new WindowMetrics(window,
            home.Build(window, totalPossession, totalFinalThirdPasses),
            away.Build(window, totalPossession, totalFinalThirdPasses));
    }

    /// <summary>
    /// A possession loss is dangerous when it happens in the team's own defensive
    /// third, or anywhere in its own half and the opponent shoots within
    /// <see cref="DangerousLossShotWindowSeconds"/>.
    /// </summary>
    public static bool IsDangerous(MatchState state, PossessionLoss loss)
    {
        if (loss.InOwnDefensiveThird) return true;
        if (!loss.InOwnHalf) return false;
        return OpponentShotAfter(state, loss) is not null;
    }

    public static ShotRecord? OpponentShotAfter(MatchState state, PossessionLoss loss)
    {
        foreach (var shot in state.Shots)
        {
            if (shot.Second < loss.Second) continue;
            if (shot.Second > loss.Second + DangerousLossShotWindowSeconds) break;
            if (shot.TeamId != loss.TeamId) return shot;
        }
        return null;
    }

    /// <summary>
    /// The channel of an attack is where most of its attacking-third actions happened;
    /// ties go to the channel of the first action (where the attack arrived).
    /// </summary>
    public static AttackChannel ClassifyAttack(IReadOnlyList<AttackingAction> actions)
    {
        var counts = new int[3];
        foreach (var a in actions) counts[(int)a.Channel]++;
        var max = counts.Max();
        var first = actions[0].Channel;
        return counts[(int)first] == max ? first : (AttackChannel)Array.IndexOf(counts, max);
    }

    /// <summary>Binary search the ordered event list for events with MatchSecond inside the window.</summary>
    public static (int Start, int End) IndexRange(IReadOnlyList<MatchEvent> events, AnalysisWindow window)
    {
        return (LowerBound(events, window.FromSecond), LowerBound(events, window.ToSecond));

        static int LowerBound(IReadOnlyList<MatchEvent> list, int second)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) >>> 1;
                if (list[mid].MatchSecond < second) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
    }

    private sealed class Accumulator(string teamId)
    {
        public double PossessionSeconds;
        public int EventCount, OnBallActions, Actions;
        public double SumX;
        public int PassesAttempted, PassesCompleted, ForwardPasses, ProgressivePasses, LongBalls, FinalThirdPasses;
        public int Shots, ShotsOnTarget, Goals, BigChances, Corners;
        public double ChanceQuality;
        public int Recoveries, HighRecoveries, Tackles, Interceptions, Fouls;
        public int PossessionsStarted, PossessionPasses, FastTransitions, CounterAttackShots;
        public readonly List<string> EntryIds = [], LeftAttackIds = [], CentreAttackIds = [], RightAttackIds = [], BoxEntryIds = [];
        public readonly List<AttackChannel> AttackChannels = [];
        public int AttackingThirdActions;
        public readonly List<string> ShotIds = [], TurnoverIds = [], DangerousTurnoverIds = [], HighRecoveryIds = [];
        public readonly List<string> RecoveryIds = [], CornerIds = [], FastTransitionIds = [];

        public TeamWindowMetrics Build(AnalysisWindow window, double totalPossession, int totalFinalThirdPasses) => new()
        {
            TeamId = teamId,
            Window = window,
            PossessionSeconds = PossessionSeconds,
            PossessionShare = totalPossession <= 0 ? 0.5 : PossessionSeconds / totalPossession,
            PassesAttempted = PassesAttempted,
            PassesCompleted = PassesCompleted,
            ForwardPasses = ForwardPasses,
            ProgressivePasses = ProgressivePasses,
            LongBalls = LongBalls,
            FinalThirdPasses = FinalThirdPasses,
            FinalThirdEntries = EntryIds.Count,
            LeftAttacks = AttackChannels.Count(c => c == AttackChannel.Left),
            CentreAttacks = AttackChannels.Count(c => c == AttackChannel.Centre),
            RightAttacks = AttackChannels.Count(c => c == AttackChannel.Right),
            AttackingThirdActions = AttackingThirdActions,
            BoxEntries = BoxEntryIds.Count,
            Shots = Shots,
            ShotsOnTarget = ShotsOnTarget,
            Goals = Goals,
            ChanceQuality = Math.Round(ChanceQuality, 3),
            BigChances = BigChances,
            Corners = Corners,
            Turnovers = TurnoverIds.Count,
            DangerousTurnovers = DangerousTurnoverIds.Count,
            Recoveries = Recoveries,
            HighRecoveries = HighRecoveries,
            Tackles = Tackles,
            Interceptions = Interceptions,
            Fouls = Fouls,
            EventCount = EventCount,
            AverageX = OnBallActions == 0 ? 50 : SumX / OnBallActions,
            FieldTilt = totalFinalThirdPasses == 0 ? 0.5 : (double)FinalThirdPasses / totalFinalThirdPasses,
            PossessionsStarted = PossessionsStarted,
            AveragePassesPerPossession = PossessionsStarted == 0 ? 0 : (double)PossessionPasses / PossessionsStarted,
            FastTransitions = FastTransitions,
            CounterAttackShots = CounterAttackShots,
            Tempo = PossessionSeconds < 30 ? 0 : Actions / (PossessionSeconds / 60.0),
            EntryIds = EntryIds,
            LeftAttackIds = LeftAttackIds,
            CentreAttackIds = CentreAttackIds,
            RightAttackIds = RightAttackIds,
            BoxEntryIds = BoxEntryIds,
            ShotIds = ShotIds,
            TurnoverIds = TurnoverIds,
            DangerousTurnoverIds = DangerousTurnoverIds,
            HighRecoveryIds = HighRecoveryIds,
            RecoveryIds = RecoveryIds,
            CornerIds = CornerIds,
            FastTransitionIds = FastTransitionIds,
        };
    }
}
