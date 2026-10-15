namespace MatchShift.SyntheticData.Simulation;

/// <summary>
/// How a team behaves during a scenario phase. These are generator INPUTS only.
/// They shape the probability of events; the analytics engine never sees them and
/// must rediscover the resulting patterns from the events alone.
/// </summary>
public sealed record TeamBehaviour
{
    /// <summary>Ball retention / composure. Higher = fewer dispossessions, better passing.</summary>
    public double Control { get; init; } = 0.5;

    /// <summary>Base pass completion probability before distance/zone/pressure modifiers.</summary>
    public double PassAccuracy { get; init; } = 0.86;

    /// <summary>Forward bias per pass (0 = recycling, 1 = very vertical).</summary>
    public double Progression { get; init; } = 0.5;

    /// <summary>Pressing intensity. Raises opponent dispossession rate and recovery height.</summary>
    public double Press { get; init; } = 0.5;

    /// <summary>Relative weights for left / centre / right when entering the attacking third.</summary>
    public ChannelWeights Channels { get; init; } = ChannelWeights.Balanced;

    /// <summary>Multiplier on shooting probability once in range.</summary>
    public double ShotPropensity { get; init; } = 1.0;

    /// <summary>Multiplier on action rate (&gt;1 = quicker ball circulation).</summary>
    public double Tempo { get; init; } = 1.0;

    /// <summary>Probability that a recovery triggers a fast vertical break.</summary>
    public double CounterAttack { get; init; } = 0.15;

    /// <summary>Probability of a long forward ball instead of a normal pass.</summary>
    public double Directness { get; init; } = 0.12;

    public static TeamBehaviour Neutral { get; } = new();
}

public sealed record ChannelWeights(double Left, double Centre, double Right)
{
    public static ChannelWeights Balanced { get; } = new(0.34, 0.32, 0.34);

    public double[] AsArray() => [Left, Centre, Right];
}

/// <summary>A span of the match during which both teams follow fixed behaviour.</summary>
public sealed record ScenarioPhase(
    int StartMinute,
    int EndMinute,
    string Label,
    TeamBehaviour Home,
    TeamBehaviour Away);

public enum TeamSide
{
    Home,
    Away
}
