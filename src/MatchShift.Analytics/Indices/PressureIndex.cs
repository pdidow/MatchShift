using MatchShift.Analytics.Metrics;

namespace MatchShift.Analytics.Indices;

/// <summary>
/// MatchShift Pressure Index (0-100): how much attacking pressure one team is applying
/// over a window. A MatchShift-derived metric - not an official Premier League statistic.
///
/// Each component is a per-minute rate (or position) scaled linearly between a floor and a
/// ceiling, then weighted:
///
/// | Component                 | Floor | Ceiling | Weight |
/// |---------------------------|-------|---------|--------|
/// | Attacking-third entries/min | 0.3 | 2.2     | 0.25   |
/// | Penalty-area entries/min  | 0.0   | 0.8     | 0.15   |
/// | Shots/min                 | 0.0   | 0.5     | 0.20   |
/// | High recoveries/min       | 0.0   | 0.6     | 0.15   |
/// | Corners/min               | 0.0   | 0.3     | 0.05   |
/// | Average on-ball X         | 38    | 68      | 0.20   |
/// </summary>
public static class PressureIndex
{
    public static IReadOnlyList<ComponentScale> Scales { get; } =
    [
        new("entries", "Attacking-third entries per minute", 0.3, 2.2, 0.25),
        new("boxEntries", "Penalty-area entries per minute", 0.0, 0.8, 0.15),
        new("shots", "Shots per minute", 0.0, 0.5, 0.20),
        new("highRecoveries", "High recoveries per minute", 0.0, 0.6, 0.15),
        new("corners", "Corners per minute", 0.0, 0.3, 0.05),
        new("territory", "Average on-ball position (0-100)", 38, 68, 0.20),
    ];

    public static IndexResult Compute(TeamWindowMetrics m)
    {
        var raws = new[]
        {
            m.PerMinute(m.FinalThirdEntries),
            m.PerMinute(m.BoxEntries),
            m.PerMinute(m.Shots),
            m.PerMinute(m.HighRecoveries),
            m.PerMinute(m.Corners),
            m.AverageX,
        };
        var components = Scales.Select((s, i) => s.Score(raws[i])).ToList();
        return new IndexResult("pressure", m.TeamId, m.Window, Math.Round(components.Sum(c => c.Contribution), 1), components);
    }
}

public sealed record IndexResult(
    string Index,
    string? TeamId,
    AnalysisWindow Window,
    double Value,
    IReadOnlyList<IndexComponent> Components);
