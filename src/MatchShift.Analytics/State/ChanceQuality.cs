using MatchShift.Core.Domain;

namespace MatchShift.Analytics.State;

/// <summary>
/// MatchShift Chance Quality (MSCQ): a transparent, location-based estimate of the
/// probability that a shot is scored. It is a MatchShift-derived metric, NOT an
/// official expected-goals model or Premier League statistic.
///
///   distance d = metres from the centre of the goal (pitch scaled to 105 x 68)
///   angle    a = visible goal-mouth angle in radians (7.32 m goal)
///   MSCQ       = clamp(0.06·e^(-d/30) + 0.9·e^(-d/9)·sqrt(0.4 + 0.6·a/(π/2)), 0.01, 0.85)
///   headers   x 0.65
/// </summary>
public static class ChanceQuality
{
    public const double BigChanceThreshold = 0.22;

    public static double Estimate(double x, double y, bool header = false)
    {
        var distance = Pitch.DistanceToGoal(x, y);
        var angle = GoalAngle(x, y);
        var q = 0.06 * Math.Exp(-distance / 30) + 0.9 * Math.Exp(-distance / 9) * Math.Sqrt(0.4 + 0.6 * angle / (Math.PI / 2));
        if (header) q *= 0.65;
        return Math.Round(Math.Clamp(q, 0.01, 0.85), 3);
    }

    /// <summary>Angle subtended by the goal mouth from the shot location.</summary>
    public static double GoalAngle(double x, double y)
    {
        var dx = Math.Max(0.5, (100 - x) * 1.05);
        var dy = (y - 50) * 0.68;
        const double halfGoal = 3.66;
        var a1 = Math.Atan2(dy - halfGoal, dx);
        var a2 = Math.Atan2(dy + halfGoal, dx);
        return Math.Abs(a2 - a1);
    }
}
