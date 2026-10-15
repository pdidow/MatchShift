using MatchShift.Core.Domain;

namespace MatchShift.Analytics.State;

/// <summary>Minimal identity the analytics engine needs. No scenario metadata, no labels.</summary>
public sealed record MatchContext(string MatchId, string HomeTeamId, string AwayTeamId)
{
    public static MatchContext From(Match match) => new(match.Id, match.HomeTeam.Id, match.AwayTeam.Id);

    public string Opponent(string teamId) => teamId == HomeTeamId ? AwayTeamId : HomeTeamId;

    public IEnumerable<string> TeamIds => [HomeTeamId, AwayTeamId];
}

/// <summary>
/// Incrementally maintained, derived view of the event stream.
///
/// Events arrive one by one (as in a live feed) through <see cref="Ingest"/>. The
/// state derives possession chains, attacking-third entries, possession losses,
/// shots and substitutions as it goes. Everything here is a pure function of the
/// events ingested so far - nothing looks ahead.
/// </summary>
public sealed class MatchState
{
    private readonly List<MatchEvent> _events = [];
    private readonly Dictionary<string, MatchEvent> _byId = [];
    private readonly List<Possession> _possessions = [];
    private readonly List<ThirdEntry> _entries = [];
    private readonly List<BoxEntry> _boxEntries = [];
    private readonly List<AttackingAction> _attackingActions = [];
    private readonly List<PossessionLoss> _losses = [];
    private readonly List<ShotRecord> _shots = [];
    private readonly List<SubstitutionRecord> _substitutions = [];
    private Possession? _current;
    private int _attackSequence;
    private bool _ballRecycled = true;
    private int _attackPossessionId = -1;

    public MatchState(MatchContext context) => Context = context;

    public MatchContext Context { get; }
    public IReadOnlyList<MatchEvent> Events => _events;
    public IReadOnlyList<Possession> Possessions => _possessions;
    public IReadOnlyList<ThirdEntry> Entries => _entries;
    public IReadOnlyList<BoxEntry> BoxEntries => _boxEntries;
    public IReadOnlyList<AttackingAction> AttackingActions => _attackingActions;
    public IReadOnlyList<PossessionLoss> Losses => _losses;
    public IReadOnlyList<ShotRecord> Shots => _shots;
    public IReadOnlyList<SubstitutionRecord> Substitutions => _substitutions;

    /// <summary>The latest match second the state has seen.</summary>
    public int ClockSecond { get; private set; }

    public MatchEvent? FindEvent(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Goals scored by <paramref name="teamId"/> strictly before <paramref name="beforeSecond"/>.</summary>
    public int GoalsBefore(string teamId, int beforeSecond) =>
        _shots.Count(s => s.IsGoal && s.TeamId == teamId && s.Second < beforeSecond);

    /// <summary>Goal difference from <paramref name="teamId"/>'s point of view.</summary>
    public int GoalDifference(string teamId, int beforeSecond) =>
        GoalsBefore(teamId, beforeSecond) - GoalsBefore(Context.Opponent(teamId), beforeSecond);

    public static MatchState FromEvents(MatchContext context, IEnumerable<MatchEvent> events)
    {
        var state = new MatchState(context);
        foreach (var e in events) state.Ingest(e);
        return state;
    }

    public void Ingest(MatchEvent e)
    {
        if (_events.Count > 0 && e.SequenceNumber <= _events[^1].SequenceNumber)
            throw new InvalidOperationException($"Events must arrive in sequence order (got {e.SequenceNumber} after {_events[^1].SequenceNumber}).");

        _events.Add(e);
        _byId[e.Id] = e;
        ClockSecond = Math.Max(ClockSecond, e.MatchSecond);

        TrackPossession(e);
        TrackEntries(e);
        TrackShot(e);

        if (e.Type == EventType.Substitution && e.PlayerId is not null && e.RelatedPlayerId is not null)
            _substitutions.Add(new SubstitutionRecord(e.Id, e.MatchSecond, e.TeamId, e.PlayerId, e.RelatedPlayerId));

        if (e.Type == EventType.PeriodEnd && _current is not null)
        {
            _current.EndReason = PossessionEndReason.PeriodEnd;
            _current = null;
        }
    }

    /// <summary>Advance the analytics clock without a new event (e.g. playback ticking through a quiet spell).</summary>
    public void AdvanceClock(int matchSecond) => ClockSecond = Math.Max(ClockSecond, matchSecond);

    private void TrackPossession(MatchEvent e)
    {
        if (e.PossessionTeamId is null || e.PossessionId <= 0) return;

        if (_current is null || _current.Id != e.PossessionId)
        {
            var previous = _current;
            var startReason = PossessionStartReason.Restart;
            if (previous is not null)
            {
                if (previous.TeamId != e.PossessionTeamId)
                {
                    var loss = ClassifyEnd(previous);
                    if (loss is not null)
                    {
                        _losses.Add(loss);
                        startReason = PossessionStartReason.Regain;
                    }
                }
                else if (previous.EndReason == PossessionEndReason.Open)
                {
                    previous.EndReason = PossessionEndReason.Continued;
                }
            }

            if (e.Type == EventType.Pass && e.Qualifier == "KickOff") startReason = PossessionStartReason.KickOff;
            if (e.Type == EventType.Corner) startReason = PossessionStartReason.SetPiece;

            _current = new Possession(e.PossessionId, e.PossessionTeamId, e.MatchSecond, startReason);
            _possessions.Add(_current);
        }

        _current.EventIds.Add(e.Id);
        _current.LastSecond = e.MatchSecond;
        if (e.TeamId == _current.TeamId)
        {
            _current.TeamEventCount++;
            if (_current.StartX is null && e.StartX is not null) _current.StartX = e.StartX;
            if (e.Type is EventType.Pass or EventType.Carry or EventType.Shot) _current.ActionCount++;
            if (e.Type == EventType.Pass) _current.PassCount++;
            if (e.Type == EventType.Shot)
            {
                _current.ShotCount++;
                _current.FirstShotSecond ??= e.MatchSecond;
            }
            var reachedX = e.Outcome == EventOutcome.Complete && e.EndX is not null ? e.EndX : e.StartX;
            if (reachedX is not null && Pitch.IsInAttackingThird(reachedX.Value))
                _current.FirstFinalThirdSecond ??= e.MatchSecond;
        }
    }

    /// <summary>Decide whether a possession that just ended (opponent now has the ball) was LOST in play.</summary>
    private PossessionLoss? ClassifyEnd(Possession p)
    {
        if (p.ShotCount > 0)
        {
            p.EndReason = PossessionEndReason.Shot;
            return null;
        }

        MatchEvent? last = null;
        for (var i = p.EventIds.Count - 1; i >= 0; i--)
        {
            var candidate = _byId[p.EventIds[i]];
            if (candidate.TeamId == p.TeamId) { last = candidate; break; }
        }
        if (last is null) { p.EndReason = PossessionEndReason.Other; return null; }

        if (last.Type == EventType.Offside)
        {
            p.EndReason = PossessionEndReason.Offside;
            return null;
        }

        var lost = (last.Type == EventType.Pass && last.Outcome == EventOutcome.Incomplete) ||
                   (last.Type == EventType.Turnover && last.Outcome == EventOutcome.Lost);
        if (!lost)
        {
            p.EndReason = PossessionEndReason.Other;
            return null;
        }

        p.EndReason = PossessionEndReason.Lost;
        var x = last.Type == EventType.Pass ? last.EndX ?? last.StartX ?? 50 : last.StartX ?? 50;
        var y = last.Type == EventType.Pass ? last.EndY ?? last.StartY ?? 50 : last.StartY ?? 50;
        return new PossessionLoss(last.Id, last.MatchSecond, p.TeamId, last.PlayerId, p.Id, x, y,
            last.Type == EventType.Turnover ? LossKind.Dispossessed : LossKind.MisplacedPass);
    }

    private void TrackEntries(MatchEvent e)
    {
        TrackAttackingAction(e);
        if (e.Type is not (EventType.Pass or EventType.Carry) || e.Outcome != EventOutcome.Complete) return;
        if (e.StartX is null || e.EndX is null || e.StartY is null || e.EndY is null) return;
        if (e.Qualifier == "KickOff") return;

        if (!Pitch.IsInAttackingThird(e.StartX.Value) && Pitch.IsInAttackingThird(e.EndX.Value))
            _entries.Add(new ThirdEntry(e.Id, e.MatchSecond, e.TeamId, e.PossessionId, Pitch.ChannelOf(e.EndY.Value),
                e.EndX.Value, e.EndY.Value, e.PlayerId));

        if (!Pitch.IsInPenaltyArea(e.StartX.Value, e.StartY.Value) && Pitch.IsInPenaltyArea(e.EndX.Value, e.EndY.Value))
            _boxEntries.Add(new BoxEntry(e.Id, e.MatchSecond, e.TeamId, e.PossessionId, e.EndX.Value, e.EndY.Value, e.PlayerId));
    }

    /// <summary>
    /// On-ball actions in the attack zone (beyond <see cref="Pitch.AttackZoneStartX"/>), located by channel and
    /// grouped into attacks. Completed passes/carries are located where the ball ends up, shots where they
    /// are taken. Corner kicks are excluded so set pieces do not distort the open-play picture.
    /// </summary>
    private void TrackAttackingAction(MatchEvent e)
    {
        if (e.PossessionTeamId != e.TeamId) return;

        // A new attack starts with a new possession, or after the team has recycled the ball
        // back into its own half (x below RecycleX) and comes forward again.
        if (e.PossessionId != _attackPossessionId)
        {
            _attackPossessionId = e.PossessionId;
            _ballRecycled = true;
        }

        double? x = null, y = null;
        if (e.Type is EventType.Pass or EventType.Carry && e.Outcome == EventOutcome.Complete)
        {
            if (e.StartX >= 99 || e.Qualifier == "KickOff") return;
            x = e.EndX;
            y = e.EndY;
        }
        else if (e.Type == EventType.Shot)
        {
            x = e.StartX;
            y = e.StartY;
        }
        if (x is null || y is null) return;

        if (!Pitch.IsInAttackZone(x.Value))
        {
            if (x.Value < RecycleX) _ballRecycled = true;
            return;
        }

        if (_ballRecycled)
        {
            _attackSequence++;
            _ballRecycled = false;
        }
        _attackingActions.Add(new AttackingAction(e.Id, e.MatchSecond, e.TeamId, e.PossessionId, _attackSequence,
            Pitch.ChannelOf(y.Value), x.Value, y.Value));
    }

    /// <summary>Ball moved back below this line ends the current attack (a later push forward is a new attack).</summary>
    public const double RecycleX = 45;

    private void TrackShot(MatchEvent e)
    {
        if (e.Type != EventType.Shot || e.StartX is null || e.StartY is null) return;
        var header = e.Qualifier == "Header";
        var quality = ChanceQuality.Estimate(e.StartX.Value, e.StartY.Value, header);
        var onTarget = e.Outcome is EventOutcome.OnTarget or EventOutcome.Goal;
        _shots.Add(new ShotRecord(e.Id, e.MatchSecond, e.TeamId, e.PlayerId, e.PossessionId, e.StartX.Value, e.StartY.Value,
            quality, onTarget, e.Outcome == EventOutcome.Goal, header));
    }
}
