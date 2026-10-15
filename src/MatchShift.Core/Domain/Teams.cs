using System.Text.Json.Serialization;

namespace MatchShift.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<Position>))]
public enum Position
{
    Goalkeeper,
    RightBack,
    CentreBack,
    LeftBack,
    DefensiveMidfielder,
    CentralMidfielder,
    AttackingMidfielder,
    RightWinger,
    LeftWinger,
    Striker
}

[JsonConverter(typeof(JsonStringEnumConverter<PreferredSide>))]
public enum PreferredSide
{
    Left,
    Centre,
    Right
}

/// <summary>
/// Simple behavioural profile used by the synthetic generator to make event
/// generation football-realistic. All values are 0-1 tendencies.
/// The analytics engine never reads these - it only sees events.
/// </summary>
public sealed record BehaviourProfile
{
    public double PassingInvolvement { get; init; } = 0.5;
    public double ForwardPassTendency { get; init; } = 0.5;
    public PreferredSide PreferredSide { get; init; } = PreferredSide.Centre;
    public double ShotFrequency { get; init; } = 0.1;
    public double PressingActivity { get; init; } = 0.5;
    public double TurnoverTendency { get; init; } = 0.3;
    public double RecoveryTendency { get; init; } = 0.5;
    public double AttackingAggressiveness { get; init; } = 0.5;

    /// <summary>Average team-relative position (0-100) used to place the player's actions.</summary>
    public double AverageX { get; init; } = 50;
    public double AverageY { get; init; } = 50;
}

public sealed record Player
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ShortName { get; init; }
    public required int ShirtNumber { get; init; }
    public required Position Position { get; init; }
    public required string TeamId { get; init; }
    public bool IsStarter { get; init; }
    public BehaviourProfile Profile { get; init; } = new();
}

public sealed record Team
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ShortName { get; init; }

    /// <summary>Three-letter code for compact UI.</summary>
    public required string Code { get; init; }

    public required string PrimaryColor { get; init; }
    public required string SecondaryColor { get; init; }
    public required string Formation { get; init; }
    public required IReadOnlyList<Player> Squad { get; init; }

    public IEnumerable<Player> Starters => Squad.Where(p => p.IsStarter);
    public IEnumerable<Player> Substitutes => Squad.Where(p => !p.IsStarter);
}
