using System.Text.Json.Serialization;

namespace MatchShift.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<EventType>))]
public enum EventType
{
    PeriodStart,
    PeriodEnd,
    Pass,
    Carry,
    Shot,
    Goal,
    Save,
    BallRecovery,
    Turnover,
    Tackle,
    Interception,
    Clearance,
    Foul,
    Card,
    Substitution,
    Corner,
    Offside
}

[JsonConverter(typeof(JsonStringEnumConverter<EventOutcome>))]
public enum EventOutcome
{
    None,
    Complete,
    Incomplete,
    Won,
    Lost,
    OnTarget,
    OffTarget,
    Blocked,
    Goal,
    Yellow,
    Red
}

/// <summary>
/// The single normalized event record used by every MatchShift component.
///
/// Coordinates follow <see cref="Pitch"/>: team-relative, 0-100, the acting team
/// (<see cref="TeamId"/>) attacks towards X = 100.
///
/// <see cref="PossessionId"/> / <see cref="PossessionTeamId"/> describe which
/// team had the ball when the event happened, so defensive actions (tackles,
/// interceptions) can be attributed to the correct possession chain.
/// </summary>
public sealed record MatchEvent
{
    public required string Id { get; init; }
    public required string MatchId { get; init; }
    public required int SequenceNumber { get; init; }

    /// <summary>Continuous match clock in seconds. Second half starts at 2700.</summary>
    public required int MatchSecond { get; init; }

    public required int Period { get; init; }
    public required EventType Type { get; init; }
    public EventOutcome Outcome { get; init; } = EventOutcome.None;

    public required string TeamId { get; init; }
    public string? PlayerId { get; init; }

    /// <summary>Pass recipient, substitute coming on, or fouled player.</summary>
    public string? RelatedPlayerId { get; init; }

    public int PossessionId { get; init; }
    public string? PossessionTeamId { get; init; }

    public double? StartX { get; init; }
    public double? StartY { get; init; }
    public double? EndX { get; init; }
    public double? EndY { get; init; }

    /// <summary>For shots: set-piece origin, header, etc. Free-text but stable vocabulary.</summary>
    public string? Qualifier { get; init; }

    [JsonIgnore]
    public int MatchMinute => MatchSecond / 60;

    [JsonIgnore]
    public bool HasLocation => StartX.HasValue && StartY.HasValue;

    [JsonIgnore]
    public bool HasEndLocation => EndX.HasValue && EndY.HasValue;

    public override string ToString() =>
        $"#{SequenceNumber} {MatchSecond / 60:00}:{MatchSecond % 60:00} {Type}/{Outcome} {TeamId} {PlayerId}";
}

public static class MatchClock
{
    public const int HalfLengthSeconds = 45 * 60;
    public const int SecondHalfStartSecond = HalfLengthSeconds;
    public const int FullTimeSecond = 90 * 60;

    public static int PeriodOf(int matchSecond) => matchSecond < SecondHalfStartSecond ? 1 : 2;

    public static string Format(int matchSecond) => $"{matchSecond / 60:00}:{matchSecond % 60:00}";

    /// <summary>Broadcast-style minute label, e.g. 63'. Minute N covers seconds (N-1)*60 .. N*60-1.</summary>
    public static int DisplayMinute(int matchSecond) => Math.Min(90, matchSecond / 60 + 1);
}
