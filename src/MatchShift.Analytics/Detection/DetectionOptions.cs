namespace MatchShift.Analytics.Detection;

/// <summary>
/// Every threshold used by the detectors. Defaults were calibrated against labelled
/// synthetic datasets (see docs/MATCH_SHIFT_DETECTION.md and the evaluation CLI).
/// </summary>
public sealed record DetectionOptions
{
    /// <summary>Shift detectors stay silent before this minute (not enough baseline).</summary>
    public int WarmupMinutes { get; init; } = 18;

    /// <summary>A shift must hold on this many consecutive one-minute ticks before it is reported.</summary>
    public int RequiredConsecutiveTicks { get; init; } = 3;

    /// <summary>Same type + team + variant cannot fire again within this many minutes.</summary>
    public int CooldownMinutes { get; init; } = 12;

    public AttackChannelOptions AttackChannel { get; init; } = new();
    public PressureOptions Pressure { get; init; } = new();
    public ControlOptions Control { get; init; } = new();
    public PossessionOptions Possession { get; init; } = new();
    public TerritoryOptions Territory { get; init; } = new();
    public TempoOptions Tempo { get; init; } = new();
    public ChaosOptions Chaos { get; init; } = new();
    public ShotPressureOptions ShotPressure { get; init; } = new();
    public TurnoverSpikeOptions TurnoverSpike { get; init; } = new();
    public CounterAttackOptions CounterAttack { get; init; } = new();
    public SubstitutionOptions Substitution { get; init; } = new();
    public MomentOptions Moments { get; init; } = new();

    public static DetectionOptions Default { get; } = new();
}

public sealed record AttackChannelOptions
{
    public double RecentMinutes { get; init; } = 12;

    /// <summary>Baseline length in minutes before the recent window; 0 = everything earlier in the match.</summary>
    public double BaselineMinutes { get; init; } = 0;

    public double MinBaselineSpanMinutes { get; init; } = 12;
    public double MinShareIncrease { get; init; } = 0.25;
    public double MinRecentShare { get; init; } = 0.55;

    /// <summary>Minimum attacks (attacking possessions) in the recent window.</summary>
    public int MinRecentAttacks { get; init; } = 8;

    public int MinBaselineAttacks { get; init; } = 10;

    /// <summary>
    /// Minimum one-sided two-proportion z-score with attacks as the sample. Set high because the
    /// detector looks every minute at six team/channel combinations (multiple comparisons).
    /// </summary>
    public double MinZScore { get; init; } = 2.7;

    /// <summary>Consecutive one-minute ticks required (overrides the global setting for this detector).</summary>
    public int RequiredConsecutiveTicks { get; init; } = 2;
}

public sealed record PressureOptions
{
    public double RecentMinutes { get; init; } = 8;
    public double BaselineMinutes { get; init; } = 15;
    public double MinIncrease { get; init; } = 18;
    public double MinRecentPressure { get; init; } = 45;
    public double MinMarginOverOpponent { get; init; } = 12;
    public int MinRecentEvents { get; init; } = 25;
    public double DecreaseFromAtLeast { get; init; } = 55;
    public double DecreaseToAtMost { get; init; } = 25;
}

public sealed record ControlOptions
{
    public double RecentMinutes { get; init; } = 10;
    public double BaselineMinutes { get; init; } = 15;
    public double MinChange { get; init; } = 20;
    public double MinRecentControl { get; init; } = 62;
}

public sealed record PossessionOptions
{
    public double RecentMinutes { get; init; } = 10;
    public double BaselineMinutes { get; init; } = 15;
    public double MinShareChange { get; init; } = 0.18;
    public double MinRecentPossessionSeconds { get; init; } = 240;
}

public sealed record TerritoryOptions
{
    public double RecentMinutes { get; init; } = 10;
    public double BaselineMinutes { get; init; } = 15;
    public double MinFieldTiltChange { get; init; } = 0.28;
    public double MinRecentFieldTilt { get; init; } = 0.7;
    public int MinFinalThirdPasses { get; init; } = 15;
}

public sealed record TempoOptions
{
    public double RecentMinutes { get; init; } = 10;
    public double BaselineMinutes { get; init; } = 15;
    public double MinRelativeChange { get; init; } = 0.40;
    public int MinRecentActions { get; init; } = 30;
}

public sealed record ChaosOptions
{
    public double RecentMinutes { get; init; } = 10;
    public double BaselineMinutes { get; init; } = 15;
    public double MinIncrease { get; init; } = 15;
    public double MaxBaseline { get; init; } = 58;
}

public sealed record ShotPressureOptions
{
    public double RecentMinutes { get; init; } = 8;
    public double BaselineMinutes { get; init; } = 15;
    public int MinRecentShots { get; init; } = 5;
    public double MinRecentChanceQuality { get; init; } = 0.6;
    public double MinRateRatio { get; init; } = 2.0;
}

public sealed record TurnoverSpikeOptions
{
    public double RecentMinutes { get; init; } = 5;
    public double BaselineMinutes { get; init; } = 10;
    public int MinRecentTurnovers { get; init; } = 9;
    public double MinRateRatio { get; init; } = 2.2;
}

public sealed record CounterAttackOptions
{
    public double RecentMinutes { get; init; } = 8;
    public double BaselineMinutes { get; init; } = 15;
    public int MinRecentTransitions { get; init; } = 3;
    public int MinRecentCounterShots { get; init; } = 1;
    public double MinRateRatio { get; init; } = 2.0;
}

public sealed record SubstitutionOptions
{
    /// <summary>Evaluate between these many minutes after the substitution.</summary>
    public double MinMinutesAfter { get; init; } = 7;

    public double MaxMinutesAfter { get; init; } = 15;
    public double BeforeMinutes { get; init; } = 15;
    public double MinChannelShareIncrease { get; init; } = 0.22;
    public double MinChannelZScore { get; init; } = 2.4;
    public double MinPressureChange { get; init; } = 32;

    /// <summary>Pressure/possession aspects need at least this long after the substitution to count.</summary>
    public double MinMinutesForIndexAspects { get; init; } = 9;
    public double MinPossessionChange { get; init; } = 0.22;
}

public sealed record MomentOptions
{
    public double BigChanceQuality { get; init; } = 0.30;
    public int DangerousTurnoverConsequenceSeconds { get; init; } = 25;
    public double DangerousTurnoverMinQualityOwnThird { get; init; } = 0.12;
    public double DangerousTurnoverMinQualityOwnHalf { get; init; } = 0.25;
}
