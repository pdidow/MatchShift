namespace MatchShift.Core.Domain;

/// <summary>
/// Pitch conventions used everywhere in MatchShift.
///
/// Coordinates are normalized to 0-100 on both axes and are ALWAYS expressed
/// relative to the team performing the event (the "acting team"):
///
///   X = 0   : the acting team's own goal line
///   X = 100 : the opponent's goal line (the acting team attacks towards X = 100)
///   Y = 0   : the acting team's LEFT touchline (when facing the opponent's goal)
///   Y = 100 : the acting team's RIGHT touchline
///
/// This means "right-side attacks" always mean the attacking team's right,
/// regardless of which end they are physically attacking. Renderers that draw a
/// single fixed pitch convert with <see cref="ToAbsolute"/>.
/// </summary>
public static class Pitch
{
    public const double DefensiveThirdEnd = 100.0 / 3.0;
    public const double AttackingThirdStart = 200.0 / 3.0;
    public const double PenaltyAreaStartX = 83.0;

    /// <summary>
    /// Start of the "attack zone" used to classify attack sides: possessions whose play reaches
    /// beyond this line (into the opponent's half) count as attacks.
    /// </summary>
    public const double AttackZoneStartX = 55.0;
    public const double PenaltyAreaMinY = 21.0;
    public const double PenaltyAreaMaxY = 79.0;
    public const double LeftChannelEnd = 100.0 / 3.0;
    public const double RightChannelStart = 200.0 / 3.0;

    public static AttackChannel ChannelOf(double y) =>
        y < LeftChannelEnd ? AttackChannel.Left :
        y >= RightChannelStart ? AttackChannel.Right :
        AttackChannel.Centre;

    public static bool IsInAttackingThird(double x) => x >= AttackingThirdStart;

    public static bool IsInDefensiveThird(double x) => x < DefensiveThirdEnd;

    public static bool IsInAttackZone(double x) => x >= AttackZoneStartX;

    public static bool IsInPenaltyArea(double x, double y) =>
        x >= PenaltyAreaStartX && y >= PenaltyAreaMinY && y <= PenaltyAreaMaxY;

    /// <summary>Flip a point into the opponent's frame of reference.</summary>
    public static (double X, double Y) Flip(double x, double y) => (100 - x, 100 - y);

    /// <summary>
    /// Convert team-relative coordinates to an absolute frame where the HOME team
    /// attacks towards X = 100 (left-to-right on screen) in both halves.
    /// </summary>
    public static (double X, double Y) ToAbsolute(double x, double y, bool isHomeTeam) =>
        isHomeTeam ? (x, y) : Flip(x, y);

    public static double DistanceToGoal(double x, double y)
    {
        // Pitch is not square in reality (105m x 68m); scale to metres for geometry.
        var dx = (100 - x) * 1.05;
        var dy = (50 - y) * 0.68;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public enum AttackChannel
{
    Left,
    Centre,
    Right
}
