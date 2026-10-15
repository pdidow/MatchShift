using MatchShift.Analytics.Metrics;

namespace MatchShift.Analytics.Indices;

/// <summary>
/// Match Control (0-100 for the home team; the away team is 100 minus that value).
///
/// Each component is the home team's SHARE of a quantity across both teams
/// (0.5 when neither team registers any). Control = 100 x Σ(weight x share).
///
/// | Component                         | Weight |
/// |-----------------------------------|--------|
/// | Possession time                   | 0.25   |
/// | Field tilt (attacking-third passes) | 0.20 |
/// | Attacking-third entries           | 0.15   |
/// | Penalty-area entries              | 0.10   |
/// | Chance quality (MSCQ)             | 0.15   |
/// | Recoveries (turnovers forced)     | 0.15   |
///
/// No machine learning; every movement of the bar can be traced to a component.
/// </summary>
public static class MatchControl
{
    public const double BalancedBand = 7;

    public static IReadOnlyList<(string Key, string Label, double Weight)> Weights { get; } =
    [
        ("possession", "Possession time share", 0.25),
        ("fieldTilt", "Field tilt (attacking-third passes)", 0.20),
        ("entries", "Attacking-third entries share", 0.15),
        ("boxEntries", "Penalty-area entries share", 0.10),
        ("chanceQuality", "Chance quality share", 0.15),
        ("recoveries", "Recoveries share", 0.15),
    ];

    public static ControlResult Compute(WindowMetrics w)
    {
        var h = w.Home;
        var a = w.Away;
        var shares = new[]
        {
            Share(h.PossessionSeconds, a.PossessionSeconds),
            Share(h.FinalThirdPasses, a.FinalThirdPasses),
            Share(h.FinalThirdEntries, a.FinalThirdEntries),
            Share(h.BoxEntries, a.BoxEntries),
            Share(h.ChanceQuality, a.ChanceQuality),
            Share(h.Recoveries, a.Recoveries),
        };
        var components = Weights.Select((wt, i) => new IndexComponent(
            wt.Key, wt.Label, Math.Round(shares[i], 3), Math.Round(shares[i], 3), wt.Weight,
            Math.Round(shares[i] * wt.Weight * 100, 2))).ToList();
        var home = Math.Round(components.Sum(c => c.Contribution), 1);
        return new ControlResult(w.Window, h.TeamId, a.TeamId, home, components);
    }

    private static double Share(double home, double away) => home + away <= 0 ? 0.5 : home / (home + away);
}

public sealed record ControlResult(
    AnalysisWindow Window,
    string HomeTeamId,
    string AwayTeamId,
    double HomeControl,
    IReadOnlyList<IndexComponent> Components)
{
    public double AwayControl => Math.Round(100 - HomeControl, 1);

    public double ControlFor(string teamId) => teamId == HomeTeamId ? HomeControl : AwayControl;

    /// <summary>The team in control, or null when within the balanced band around 50.</summary>
    public string? Leader =>
        HomeControl >= 50 + MatchControl.BalancedBand ? HomeTeamId :
        HomeControl <= 50 - MatchControl.BalancedBand ? AwayTeamId :
        null;
}
