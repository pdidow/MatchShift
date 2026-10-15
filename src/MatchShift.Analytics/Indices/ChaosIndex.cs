using System.Text.Json.Serialization;
using MatchShift.Analytics.Metrics;

namespace MatchShift.Analytics.Indices;

[JsonConverter(typeof(JsonStringEnumConverter<ChaosState>))]
public enum ChaosState
{
    [JsonStringEnumMemberName("CONTROLLED")] Controlled,
    [JsonStringEnumMemberName("BALANCED")] Balanced,
    [JsonStringEnumMemberName("CHAOTIC")] Chaotic,
}

/// <summary>
/// Control-vs-Chaos classifier (0-100, both teams combined). Interpretable by design:
///
/// | Component                         | Floor | Ceiling | Weight |
/// |-----------------------------------|-------|---------|--------|
/// | Possession changes per minute     | see <see cref="Scales"/>       |
/// | Turnovers per minute              |       |         |        |
/// | Fast transitions per minute       |       |         |        |
/// | Shots per minute                  |       |         |        |
/// | Long-ball share of passes         |       |         |        |
/// | Short possessions (inverse passes/possession) |  |   |        |
///
/// Score &lt; <see cref="ControlledBelow"/> = CONTROLLED, ≥ <see cref="ChaoticFrom"/> = CHAOTIC,
/// otherwise BALANCED.
/// </summary>
public static class ChaosIndex
{
    public const double ControlledBelow = 35;
    public const double ChaoticFrom = 60;

    public static IReadOnlyList<ComponentScale> Scales { get; } =
    [
        new("possessionChanges", "Possession changes per minute", 1.6, 3.4, 0.25),
        new("turnovers", "Open-play turnovers per minute", 0.9, 2.4, 0.20),
        new("transitions", "Fast transitions per minute", 0.0, 0.6, 0.20),
        new("shots", "Shots per minute (both teams)", 0.1, 0.55, 0.10),
        new("longBalls", "Long-ball share of passes", 0.05, 0.25, 0.10),
        new("shortPossessions", "Short possessions (fewer passes per possession)", 0, 1, 0.15),
    ];

    public static ChaosResult Compute(WindowMetrics w)
    {
        var h = w.Home;
        var a = w.Away;
        var minutes = Math.Max(1, w.Window.Minutes);
        var passes = h.PassesAttempted + a.PassesAttempted;
        var possessions = Math.Max(1, w.TotalPossessions);
        var passesPerPossession = (double)passes / possessions;
        // 5+ passes per possession = 0 (patient), 1.5 or fewer = 1 (frantic).
        var shortness = Math.Clamp((5.0 - passesPerPossession) / 3.5, 0, 1);

        var raws = new[]
        {
            w.TotalPossessions / minutes,
            w.TotalTurnovers / minutes,
            w.TotalFastTransitions / minutes,
            w.TotalShots / minutes,
            passes == 0 ? 0 : (double)(h.LongBalls + a.LongBalls) / passes,
            shortness,
        };
        var components = Scales.Select((s, i) => s.Score(raws[i])).ToList();
        var value = Math.Round(components.Sum(c => c.Contribution), 1);
        return new ChaosResult(w.Window, value, Classify(value), components);
    }

    public static ChaosState Classify(double value) =>
        value < ControlledBelow ? ChaosState.Controlled :
        value >= ChaoticFrom ? ChaosState.Chaotic :
        ChaosState.Balanced;
}

public sealed record ChaosResult(AnalysisWindow Window, double Value, ChaosState State, IReadOnlyList<IndexComponent> Components);
