using MatchShift.Analytics.State;
using MatchShift.Core.Domain;

namespace MatchShift.Analytics.Tests;

/// <summary>
/// Tiny fluent builder for hand-written event sequences in tests.
/// Coordinates are in the acting team's frame, exactly like real events.
/// </summary>
public sealed class EventScript
{
    public const string Home = "H";
    public const string Away = "A";
    public static readonly MatchContext Context = new("TEST", Home, Away);

    private readonly List<MatchEvent> _events = [];
    private int _possessionId;
    private string _possessionTeam = Home;

    public int Second { get; private set; }

    public IReadOnlyList<MatchEvent> Events => _events;

    public MatchState ToState() => MatchState.FromEvents(Context, _events);

    public EventScript At(int second)
    {
        Second = second;
        return this;
    }

    public EventScript AtMinute(double minute) => At((int)(minute * 60));

    public EventScript Possession(string team)
    {
        _possessionId++;
        _possessionTeam = team;
        return this;
    }

    public EventScript Pass(double x1, double y1, double x2, double y2, bool complete = true, string? qualifier = null, int advance = 3) =>
        Add(EventType.Pass, _possessionTeam, x1, y1, x2, y2, complete ? EventOutcome.Complete : EventOutcome.Incomplete, qualifier, advance);

    public EventScript Carry(double x1, double y1, double x2, double y2, int advance = 3) =>
        Add(EventType.Carry, _possessionTeam, x1, y1, x2, y2, EventOutcome.Complete, null, advance);

    public EventScript Shot(double x, double y, EventOutcome outcome = EventOutcome.OffTarget, int advance = 2) =>
        Add(EventType.Shot, _possessionTeam, x, y, 100, 50, outcome, null, advance);

    public EventScript Corner(int advance = 2) => Add(EventType.Corner, _possessionTeam, 99.5, 0.5, null, null, EventOutcome.None, null, advance);

    /// <summary>Defensive win by the team NOT in possession, recorded against the current possession.</summary>
    public EventScript Recovery(string team, double x, double y, EventType type = EventType.Interception, int advance = 1) =>
        Add(type, team, x, y, null, null, EventOutcome.Won, null, advance);

    public EventScript Dispossessed(double x, double y, int advance = 1) =>
        Add(EventType.Turnover, _possessionTeam, x, y, null, null, EventOutcome.Lost, "Dispossessed", advance);

    public EventScript Substitution(string team, string off, string on, int advance = 30)
    {
        _events.Add(Make(EventType.Substitution, team, null, null, null, null, EventOutcome.None, null) with
        {
            PlayerId = off,
            RelatedPlayerId = on,
        });
        Second += advance;
        return this;
    }

    private EventScript Add(EventType type, string team, double? x1, double? y1, double? x2, double? y2, EventOutcome outcome,
        string? qualifier, int advance)
    {
        _events.Add(Make(type, team, x1, y1, x2, y2, outcome, qualifier));
        Second += advance;
        return this;
    }

    private MatchEvent Make(EventType type, string team, double? x1, double? y1, double? x2, double? y2, EventOutcome outcome, string? qualifier)
    {
        var seq = _events.Count + 1;
        return new MatchEvent
        {
            Id = $"T-{seq:D5}",
            MatchId = "TEST",
            SequenceNumber = seq,
            MatchSecond = Second,
            Period = MatchClock.PeriodOf(Second),
            Type = type,
            Outcome = outcome,
            TeamId = team,
            PlayerId = $"{team}-P",
            PossessionId = _possessionId,
            PossessionTeamId = _possessionTeam,
            StartX = x1,
            StartY = y1,
            EndX = x2,
            EndY = y2,
            Qualifier = qualifier,
        };
    }

    /// <summary>
    /// Generate a block of attacking possessions for <paramref name="team"/> between two minutes, with
    /// the given share of attacking-third actions on the right. Deterministic: no randomness.
    /// </summary>
    public EventScript AttackingSpell(string team, double fromMinute, double toMinute, double rightShare, double leftShare = -1)
    {
        if (leftShare < 0) leftShare = (1 - rightShare) / 2;
        At((int)(fromMinute * 60));
        var end = (int)(toMinute * 60);
        var i = 0;
        while (Second < end - 20)
        {
            // Bresenham-style deterministic channel assignment hitting the requested shares exactly over time.
            var fraction = (i % 20 + 0.5) / 20.0;
            var y = fraction < rightShare ? 85 : fraction < rightShare + leftShare ? 15 : 50;
            Possession(team)
                .Pass(40, 50, 60, y, advance: 4)
                .Pass(60, y, 75, y, advance: 4)
                .Pass(75, y, 80, y, complete: false, advance: 2)
                .Possession(team == Home ? Away : Home)
                .Pass(20, 100 - y, 45, 50, advance: 6);
            i++;
        }
        return this;
    }
}
