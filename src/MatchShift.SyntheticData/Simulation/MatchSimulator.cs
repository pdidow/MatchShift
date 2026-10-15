using MatchShift.Core.Domain;

namespace MatchShift.SyntheticData.Simulation;

/// <summary>
/// Possession-based football event simulator.
///
/// It is not a physics engine: each step the team in possession performs one
/// action (pass, carry, shot) or is dispossessed, with probabilities shaped by the
/// current <see cref="ScenarioPhase"/> and the players' <see cref="BehaviourProfile"/>s.
/// The ball location is tracked in the possessing team's frame (see <see cref="Pitch"/>)
/// and flipped whenever possession changes.
///
/// Fully deterministic for a given (script, seed).
/// </summary>
public sealed class MatchSimulator
{
    private readonly ScenarioScript _script;
    private readonly long _seed;
    private readonly DeterministicRandom _rng;
    private readonly List<MatchEvent> _events = [];
    private readonly Dictionary<string, List<Player>> _onPitch;
    private readonly List<ScriptedBeat> _pendingBeats;
    private int _possessionId;
    private int _counterActionsLeft;

    internal int Second { get; set; }
    internal int Period { get; private set; } = 1;
    internal string PossessionTeamId { get; private set; }
    internal Player Holder { get; private set; }
    internal double BallX { get; private set; } = 50;
    internal double BallY { get; private set; } = 50;
    internal ScenarioScript Script => _script;
    internal DeterministicRandom Rng => _rng;

    private MatchSimulator(ScenarioScript script, long seed)
    {
        _script = script;
        _seed = seed;
        _rng = new DeterministicRandom(seed);
        _onPitch = new Dictionary<string, List<Player>>
        {
            [script.Home.Id] = script.Home.Starters.ToList(),
            [script.Away.Id] = script.Away.Starters.ToList(),
        };
        _pendingBeats = script.Beats.OrderBy(b => b.AtSecond).ToList();
        PossessionTeamId = script.Home.Id;
        Holder = _onPitch[script.Home.Id].First(p => p.Position == Position.Striker);
    }

    public static Match Simulate(ScenarioScript script, long seed) => new MatchSimulator(script, seed).Run();

    private Match Run()
    {
        Emit(EventType.PeriodStart, _script.Home.Id, null);
        Kickoff(_script.Home.Id);

        while (true)
        {
            if (Period == 1 && Second >= MatchClock.SecondHalfStartSecond)
            {
                Second = MatchClock.SecondHalfStartSecond - 1;
                Emit(EventType.PeriodEnd, _script.Home.Id, null);
                Period = 2;
                Second = MatchClock.SecondHalfStartSecond;
                _counterActionsLeft = 0;
                Emit(EventType.PeriodStart, _script.Home.Id, null);
                Kickoff(_script.Away.Id);
                continue;
            }

            if (Period == 2 && Second >= MatchClock.FullTimeSecond)
            {
                Second = MatchClock.FullTimeSecond - 1;
                Emit(EventType.PeriodEnd, _script.Home.Id, null);
                break;
            }

            if (_pendingBeats.Count > 0 && _pendingBeats[0].AtSecond <= Second)
            {
                var beat = _pendingBeats[0];
                _pendingBeats.RemoveAt(0);
                _counterActionsLeft = 0;
                beat.Play(new ScriptContext(this));
                continue;
            }

            Step();
        }

        return new Match
        {
            Id = _script.MatchId,
            Competition = "MatchShift Synthetic League",
            Venue = _script.Venue,
            Date = _script.Date,
            HomeTeam = _script.Home,
            AwayTeam = _script.Away,
            Events = _events,
            Seed = _seed,
            ScenarioName = _script.Name,
        };
    }

    // ------------------------------------------------------------------ open play

    private void Step()
    {
        var phase = _script.PhaseAt(Second);
        var att = BehaviourOf(phase, PossessionTeamId);
        var def = BehaviourOf(phase, OpponentOf(PossessionTeamId));
        var counter = _counterActionsLeft > 0;
        if (counter) _counterActionsLeft--;

        if (!counter && _rng.Chance(0.009 + 0.008 * def.Press))
        {
            Foul();
            return;
        }

        var zone = BallX >= Pitch.AttackingThirdStart ? 1.5 : BallX < Pitch.DefensiveThirdEnd ? 0.8 : 1.0;
        var dispossession = 0.021 * (0.55 + 0.9 * def.Press) * (1.5 - att.Control) * zone *
                            (0.6 + Holder.Profile.TurnoverTendency) * (counter ? 0.7 : 1.0);
        if (_rng.Chance(dispossession))
        {
            Dispossessed();
            return;
        }

        if (BallX >= 70)
        {
            var distance = Pitch.DistanceToGoal(BallX, BallY);
            var shotChance = Math.Clamp(0.27 - distance * 0.008, 0.01, 0.26) *
                             att.ShotPropensity * (0.55 + Holder.Profile.ShotFrequency * 1.4) * (counter ? 1.4 : 1.0);
            if (_rng.Chance(shotChance))
            {
                OpenPlayShot();
                return;
            }
        }

        if (!counter && _rng.Chance(0.16))
        {
            Carry(att);
            return;
        }

        Pass(att, def, counter);
    }

    private void Pass(TeamBehaviour att, TeamBehaviour def, bool counter)
    {
        var isKeeper = Holder.Position == Position.Goalkeeper;
        var longBall = isKeeper ? _rng.Chance(0.45) : _rng.Chance(counter ? 0.35 : att.Directness);
        var dx = counter
            ? _rng.Range(10, 22)
            : longBall
                ? _rng.Range(22, 40)
                : isKeeper
                    ? _rng.Range(8, 20)
                    : _rng.NextGaussian(-1.0 + 15 * att.Progression * (0.5 + Holder.Profile.ForwardPassTendency), 9);

        // Nobody plays the ball back into their own six-yard box in open play.
        var endX = Math.Clamp(BallX + dx, Math.Min(BallX, 12), 97);
        double endY;
        var entersFinalThird = BallX < Pitch.AttackingThirdStart && endX >= Pitch.AttackingThirdStart;
        var entersAttackZone = BallX < 55 && endX >= 55;
        if (entersFinalThird || entersAttackZone || (endX >= 55 && _rng.Chance(0.35)))
        {
            // Entering or building in the opponent's half: teams play into their favoured lanes.
            endY = LaneY(_rng.PickWeighted(att.Channels.AsArray()), 7);
        }
        else if (_rng.Chance(0.2))
        {
            endY = LaneY(_rng.PickWeighted(att.Channels.AsArray()), 10);
        }
        else
        {
            endY = BallY + _rng.NextGaussian(0, endX >= Pitch.AttackingThirdStart ? 9 : 14);
        }
        endY = Math.Clamp(endY, 2, 98);

        var distance = Math.Sqrt(Math.Pow((endX - BallX) * 1.05, 2) + Math.Pow((endY - BallY) * 0.68, 2));
        var success = att.PassAccuracy
                      + 0.12 * (att.Control - 0.5)
                      + (endX < 50 ? 0.08 : endX < Pitch.AttackingThirdStart ? 0.06 : 0.01)
                      - 0.005 * Math.Max(0, distance - 18)
                      - (endX >= Pitch.AttackingThirdStart ? 0.07 : 0)
                      - (endX >= Pitch.PenaltyAreaStartX ? 0.13 : 0)
                      - 0.14 * (def.Press - 0.5)
                      + (counter ? 0.05 : 0);

        var recipient = PickTeammate(PossessionTeamId, endX, endY, Holder.Id);
        var dt = Math.Max(1, (int)Math.Round((counter ? 2.4 : 4.0) / att.Tempo + _rng.NextGaussian(0, 1.1) + distance * 0.035));

        if (endX >= 80 && _rng.Chance(0.03))
        {
            Emit(EventType.Pass, PossessionTeamId, Holder.Id, BallX, BallY, endX, endY, EventOutcome.Incomplete, recipient.Id);
            Second += dt;
            Emit(EventType.Offside, PossessionTeamId, recipient.Id, endX, endY);
            Second += _rng.Next(12, 25);
            var (fx, fy) = Pitch.Flip(endX, endY);
            var taker = PickDefender(OpponentOf(PossessionTeamId), fx, fy);
            SetPossession(OpponentOf(PossessionTeamId), taker, fx, fy, allowCounter: false);
            return;
        }

        if (_rng.Chance(success))
        {
            Emit(EventType.Pass, PossessionTeamId, Holder.Id, BallX, BallY, endX, endY, EventOutcome.Complete, recipient.Id,
                longBall ? "LongBall" : null);
            Holder = recipient;
            BallX = endX;
            BallY = endY;
            Second += dt;
            return;
        }

        Emit(EventType.Pass, PossessionTeamId, Holder.Id, BallX, BallY, endX, endY, EventOutcome.Incomplete, recipient.Id,
            longBall ? "LongBall" : null);
        Second += Math.Max(1, dt - 1);
        var defTeam = OpponentOf(PossessionTeamId);
        var (ix, iy) = Pitch.Flip(endX, endY);
        var winner = PickDefender(defTeam, ix, iy);
        Emit(distance < 28 && _rng.Chance(0.6) ? EventType.Interception : EventType.BallRecovery,
            defTeam, winner.Id, ix, iy, outcome: EventOutcome.Won, possessionOverride: (_possessionId, PossessionTeamId));
        SetPossession(defTeam, winner, ix, iy, allowCounter: true);
        Second += 1;
    }

    private void Carry(TeamBehaviour att)
    {
        var endX = Math.Clamp(BallX + _rng.Range(3, 12) * (0.5 + att.Progression), 2, 96);
        var endY = Math.Clamp(BallY + _rng.NextGaussian(0, 6), 2, 98);
        Emit(EventType.Carry, PossessionTeamId, Holder.Id, BallX, BallY, endX, endY, EventOutcome.Complete);
        BallX = endX;
        BallY = endY;
        Second += _rng.Next(2, 5);
    }

    private void Dispossessed()
    {
        var defTeam = OpponentOf(PossessionTeamId);
        var (fx, fy) = Pitch.Flip(BallX, BallY);
        var tackler = PickDefender(defTeam, fx, fy);
        Emit(EventType.Tackle, defTeam, tackler.Id, fx, fy, outcome: EventOutcome.Won, related: Holder.Id,
            possessionOverride: (_possessionId, PossessionTeamId));
        Emit(EventType.Turnover, PossessionTeamId, Holder.Id, BallX, BallY, outcome: EventOutcome.Lost, qualifier: "Dispossessed");
        Second += _rng.Next(1, 3);
        SetPossession(defTeam, tackler, fx, fy, allowCounter: true);
    }

    private void Foul()
    {
        var defTeam = OpponentOf(PossessionTeamId);
        var (fx, fy) = Pitch.Flip(BallX, BallY);
        var fouler = PickDefender(defTeam, fx, fy);
        Emit(EventType.Foul, defTeam, fouler.Id, fx, fy, related: Holder.Id, possessionOverride: (_possessionId, PossessionTeamId));
        if (_rng.Chance(0.12))
        {
            Emit(EventType.Card, defTeam, fouler.Id, fx, fy, outcome: EventOutcome.Yellow,
                possessionOverride: (_possessionId, PossessionTeamId));
        }
        Second += _rng.Next(14, 30);
    }

    private void OpenPlayShot()
    {
        var outcome = RollShotOutcome(header: false);
        ResolveShot(outcome, qualifier: _counterActionsLeft > 0 ? "OpenPlay" : null);
    }

    private EventOutcome RollShotOutcome(bool header)
    {
        var quality = GeneratorChanceQuality(BallX, BallY, header);
        var roll = _rng.NextDouble();
        if (roll < 0.24) return EventOutcome.Blocked;
        if (roll < 0.62) return EventOutcome.OffTarget;
        if (_script.AllowRandomGoals && _rng.Chance(Math.Min(0.9, quality / 0.38 * 0.6))) return EventOutcome.Goal;
        return EventOutcome.OnTarget;
    }

    /// <summary>
    /// Resolve a shot from the current holder at the current ball location and continue play.
    /// Shared by open play and scripted beats.
    /// </summary>
    internal void ResolveShot(EventOutcome outcome, string? qualifier)
    {
        var shooterTeam = PossessionTeamId;
        var defTeam = OpponentOf(shooterTeam);
        var targetY = outcome == EventOutcome.OffTarget
            ? (_rng.Chance(0.5) ? _rng.Range(36, 44) : _rng.Range(56, 64))
            : _rng.Range(45.5, 54.5);
        Emit(EventType.Shot, shooterTeam, Holder.Id, BallX, BallY, 100, targetY, outcome, qualifier: qualifier);
        Second += 1;

        switch (outcome)
        {
            case EventOutcome.Goal:
                Emit(EventType.Goal, shooterTeam, Holder.Id, BallX, BallY, 100, targetY, EventOutcome.Goal);
                Second += _rng.Next(45, 70);
                Kickoff(defTeam);
                break;

            case EventOutcome.OnTarget:
            {
                var keeper = Goalkeeper(defTeam);
                Emit(EventType.Save, defTeam, keeper.Id, 2, 100 - targetY, outcome: EventOutcome.Won,
                    possessionOverride: (_possessionId, shooterTeam));
                Second += 2;
                if (_rng.Chance(0.35)) Corner(shooterTeam);
                else
                {
                    SetPossession(defTeam, keeper, 6, 50, allowCounter: false);
                    Second += _rng.Next(6, 14);
                }
                break;
            }

            case EventOutcome.Blocked:
            {
                var (fx, fy) = Pitch.Flip(BallX, BallY);
                var blocker = PickDefender(defTeam, fx, fy);
                Emit(EventType.Clearance, defTeam, blocker.Id, fx, fy, Math.Min(fx + 25, 60), fy,
                    possessionOverride: (_possessionId, shooterTeam), qualifier: "Block");
                Second += 2;
                var roll = _rng.NextDouble();
                if (roll < 0.4) Corner(shooterTeam);
                else if (roll < 0.7)
                {
                    var (rx, ry) = Pitch.Flip(Math.Min(fx + 25, 60), fy);
                    var recoverer = PickTeammate(shooterTeam, rx, ry, null);
                    Emit(EventType.BallRecovery, shooterTeam, recoverer.Id, rx, ry, outcome: EventOutcome.Won);
                    Holder = recoverer;
                    BallX = rx;
                    BallY = ry;
                    Second += 2;
                }
                else
                {
                    var fx2 = Math.Min(fx + 25, 60);
                    SetPossession(defTeam, PickDefender(defTeam, fx2, fy), fx2, fy, allowCounter: true);
                }
                break;
            }

            default:
            {
                var keeper = Goalkeeper(defTeam);
                Second += _rng.Next(18, 30);
                SetPossession(defTeam, keeper, 6, 50, allowCounter: false);
                break;
            }
        }
    }

    internal void Corner(string attackingTeam)
    {
        Second += _rng.Next(15, 25);
        var fromRight = _rng.Chance(0.5);
        var cy = fromRight ? 99.5 : 0.5;
        var taker = PickTeammate(attackingTeam, 99, cy, null);
        SetPossession(attackingTeam, taker, 99.5, cy, allowCounter: false);
        Emit(EventType.Corner, attackingTeam, taker.Id, 99.5, cy);
        var tx = _rng.Range(86, 95);
        var ty = _rng.Range(38, 62);
        var target = PickTeammate(attackingTeam, tx, ty, taker.Id);
        var won = _rng.Chance(0.33);
        Emit(EventType.Pass, attackingTeam, taker.Id, 99.5, cy, tx, ty, won ? EventOutcome.Complete : EventOutcome.Incomplete,
            target.Id, "Cross");
        Second += 2;
        if (won)
        {
            Holder = target;
            BallX = tx;
            BallY = ty;
            ResolveShot(RollShotOutcome(header: true), "Header");
            return;
        }

        var defTeam = OpponentOf(attackingTeam);
        var (fx, fy) = Pitch.Flip(tx, ty);
        var clearer = PickDefender(defTeam, fx, fy);
        var clearX = _rng.Range(25, 45);
        var clearY = Math.Clamp(fy + _rng.NextGaussian(0, 15), 5, 95);
        Emit(EventType.Clearance, defTeam, clearer.Id, fx, fy, clearX, clearY,
            possessionOverride: (_possessionId, attackingTeam));
        Second += 2;
        if (_rng.Chance(0.5))
        {
            var (rx, ry) = Pitch.Flip(clearX, clearY);
            var recoverer = PickTeammate(attackingTeam, rx, ry, null);
            Emit(EventType.BallRecovery, attackingTeam, recoverer.Id, rx, ry, outcome: EventOutcome.Won);
            Holder = recoverer;
            BallX = rx;
            BallY = ry;
        }
        else
        {
            var winner = PickDefender(defTeam, clearX, clearY);
            Emit(EventType.BallRecovery, defTeam, winner.Id, clearX, clearY, outcome: EventOutcome.Won,
                possessionOverride: (_possessionId, attackingTeam));
            SetPossession(defTeam, winner, clearX, clearY, allowCounter: true);
        }
        Second += 1;
    }

    internal void Kickoff(string teamId)
    {
        var striker = _onPitch[teamId].FirstOrDefault(p => p.Position == Position.Striker) ?? _onPitch[teamId][^1];
        SetPossession(teamId, striker, 50, 50, allowCounter: false);
        var receiver = PickTeammate(teamId, 40, 45, striker.Id);
        Emit(EventType.Pass, teamId, striker.Id, 50, 50, 40, 45, EventOutcome.Complete, receiver.Id, "KickOff");
        Holder = receiver;
        BallX = 40;
        BallY = 45;
        Second += 3;
    }

    // ------------------------------------------------------------------ state helpers

    internal void SetPossession(string teamId, Player holder, double x, double y, bool allowCounter)
    {
        _possessionId++;
        PossessionTeamId = teamId;
        Holder = holder;
        BallX = x;
        BallY = y;
        _counterActionsLeft = 0;
        if (!allowCounter) return;

        var behaviour = BehaviourOf(_script.PhaseAt(Second), teamId);
        var heightFactor = x > 40 ? 1.3 : x > 25 ? 1.0 : 0.6;
        if (_rng.Chance(behaviour.CounterAttack * heightFactor))
        {
            _counterActionsLeft = _rng.Next(3, 6);
        }
    }

    internal void Substitute(string teamId, int offShirt, int onShirt)
    {
        var team = teamId == _script.Home.Id ? _script.Home : _script.Away;
        var lineup = _onPitch[teamId];
        var off = lineup.First(p => p.ShirtNumber == offShirt);
        var on = team.Squad.First(p => p.ShirtNumber == onShirt);
        lineup[lineup.IndexOf(off)] = on;
        if (Holder.Id == off.Id) Holder = on;
        Emit(EventType.Substitution, teamId, off.Id, related: on.Id);
    }

    internal Player OnPitch(string teamId, int shirt) =>
        _onPitch[teamId].FirstOrDefault(p => p.ShirtNumber == shirt)
        ?? throw new InvalidOperationException($"Shirt {shirt} is not on the pitch for {teamId}");

    internal void MoveBall(Player holder, double x, double y)
    {
        Holder = holder;
        BallX = x;
        BallY = y;
    }

    internal string OpponentOf(string teamId) => teamId == _script.Home.Id ? _script.Away.Id : _script.Home.Id;

    internal Player Goalkeeper(string teamId) =>
        _onPitch[teamId].FirstOrDefault(p => p.Position == Position.Goalkeeper) ?? _onPitch[teamId][0];

    private TeamBehaviour BehaviourOf(ScenarioPhase phase, string teamId) =>
        teamId == _script.Home.Id ? phase.Home : phase.Away;

    private double LaneY(int channel, double spread)
    {
        var (centre, min, max) = channel switch
        {
            0 => (16.0, 3.0, 32.0),
            1 => (50.0, 35.0, 65.0),
            _ => (84.0, 68.0, 97.0),
        };
        return Math.Clamp(centre + _rng.NextGaussian(0, spread), min, max);
    }

    internal Player PickTeammate(string teamId, double x, double y, string? excludeId)
    {
        var players = _onPitch[teamId];
        var weights = new double[players.Count];
        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p.Id == excludeId) continue;
            var px = p.Position == Position.Goalkeeper ? p.Profile.AverageX : p.Profile.AverageX + (x - 50) * 0.35;
            var dist = Math.Sqrt(Math.Pow(px - x, 2) + Math.Pow(p.Profile.AverageY - y, 2));
            var w = (0.2 + p.Profile.PassingInvolvement) * Math.Exp(-dist / 16);
            if (p.Position == Position.Goalkeeper) w *= x < 25 ? 0.6 : 0.01;
            weights[i] = w;
        }
        return players[_rng.PickWeighted(weights)];
    }

    internal Player PickDefender(string teamId, double x, double y)
    {
        var players = _onPitch[teamId];
        var weights = new double[players.Count];
        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            var px = p.Position == Position.Goalkeeper ? p.Profile.AverageX : p.Profile.AverageX + (x - 50) * 0.3;
            var dist = Math.Sqrt(Math.Pow(px - x, 2) + Math.Pow(p.Profile.AverageY - y, 2));
            var w = (0.25 + p.Profile.RecoveryTendency + 0.5 * p.Profile.PressingActivity) * Math.Exp(-dist / 14);
            if (p.Position == Position.Goalkeeper) w *= 0.05;
            weights[i] = w;
        }
        return players[_rng.PickWeighted(weights)];
    }

    /// <summary>Generator-side chance quality. Independent from the analytics model on purpose.</summary>
    private static double GeneratorChanceQuality(double x, double y, bool header)
    {
        var distance = Pitch.DistanceToGoal(x, y);
        var q = 0.85 * Math.Exp(-distance / 7.5);
        return header ? q * 0.6 : q;
    }

    internal MatchEvent Emit(
        EventType type,
        string teamId,
        string? playerId,
        double? startX = null,
        double? startY = null,
        double? endX = null,
        double? endY = null,
        EventOutcome outcome = EventOutcome.None,
        string? related = null,
        string? qualifier = null,
        (int Id, string TeamId)? possessionOverride = null)
    {
        var maxSecond = Period == 1 ? MatchClock.SecondHalfStartSecond - 1 : MatchClock.FullTimeSecond - 1;
        if (Second > maxSecond) Second = maxSecond;

        var sequence = _events.Count + 1;
        var e = new MatchEvent
        {
            Id = $"{_script.MatchId}-E{sequence:D5}",
            MatchId = _script.MatchId,
            SequenceNumber = sequence,
            MatchSecond = Second,
            Period = Period,
            Type = type,
            Outcome = outcome,
            TeamId = teamId,
            PlayerId = playerId,
            RelatedPlayerId = related,
            PossessionId = possessionOverride?.Id ?? _possessionId,
            PossessionTeamId = possessionOverride?.TeamId ?? PossessionTeamId,
            StartX = Round(startX),
            StartY = Round(startY),
            EndX = Round(endX),
            EndY = Round(endY),
            Qualifier = qualifier,
        };
        _events.Add(e);
        return e;
    }

    private static double? Round(double? v) => v.HasValue ? Math.Round(v.Value, 1) : null;
}
