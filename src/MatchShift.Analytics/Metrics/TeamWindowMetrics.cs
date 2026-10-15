using MatchShift.Core.Domain;

namespace MatchShift.Analytics.Metrics;

/// <summary>
/// Deterministic metrics for one team over one window. Every count that can be
/// cited in a narrative also carries the event IDs that produced it, so evidence
/// can always be traced back to source events ("SHOW ME").
/// </summary>
public sealed record TeamWindowMetrics
{
    public required string TeamId { get; init; }
    public required AnalysisWindow Window { get; init; }

    public double PossessionSeconds { get; init; }
    public double PossessionShare { get; init; }

    public int PassesAttempted { get; init; }
    public int PassesCompleted { get; init; }
    public double PassCompletion => PassesAttempted == 0 ? 0 : (double)PassesCompleted / PassesAttempted;
    public int ForwardPasses { get; init; }
    public int ProgressivePasses { get; init; }
    public int LongBalls { get; init; }
    public int FinalThirdPasses { get; init; }

    /// <summary>Completed passes/carries that moved the ball into the attacking third.</summary>
    public int FinalThirdEntries { get; init; }

    /// <summary>
    /// "Attack sides": attacks classified by the channel where most of their attack-zone play happened
    /// (left / centre / right, from the attacking team's perspective). An attack is a spell of a possession
    /// in the opponent's half; recycling the ball back into the own half and coming again starts a new attack.
    /// </summary>
    public int LeftAttacks { get; init; }
    public int CentreAttacks { get; init; }
    public int RightAttacks { get; init; }
    public int Attacks => LeftAttacks + CentreAttacks + RightAttacks;

    /// <summary>On-ball actions in the attack zone (passes/carries received there, shots).</summary>
    public int AttackingThirdActions { get; init; }

    public double LeftShare => Share(LeftAttacks);
    public double CentreShare => Share(CentreAttacks);
    public double RightShare => Share(RightAttacks);
    public int BoxEntries { get; init; }

    public int Shots { get; init; }
    public int ShotsOnTarget { get; init; }
    public int Goals { get; init; }
    public double ChanceQuality { get; init; }
    public int BigChances { get; init; }
    public int Corners { get; init; }

    public int Turnovers { get; init; }
    public int DangerousTurnovers { get; init; }
    public int Recoveries { get; init; }
    public int HighRecoveries { get; init; }
    public int Tackles { get; init; }
    public int Interceptions { get; init; }
    public int Fouls { get; init; }

    public int EventCount { get; init; }

    /// <summary>Mean X of the team's on-ball actions (team-relative, 0-100). Higher = playing further up.</summary>
    public double AverageX { get; init; }

    /// <summary>Share of all attacking-third passes in the window that belong to this team.</summary>
    public double FieldTilt { get; init; }

    public int PossessionsStarted { get; init; }
    public double AveragePassesPerPossession { get; init; }
    public int FastTransitions { get; init; }
    public int CounterAttackShots { get; init; }

    /// <summary>Passes + carries per minute of the team's own possession.</summary>
    public double Tempo { get; init; }

    public IReadOnlyList<string> EntryIds { get; init; } = [];
    public IReadOnlyList<string> LeftAttackIds { get; init; } = [];
    public IReadOnlyList<string> CentreAttackIds { get; init; } = [];
    public IReadOnlyList<string> RightAttackIds { get; init; } = [];
    public IReadOnlyList<string> BoxEntryIds { get; init; } = [];
    public IReadOnlyList<string> ShotIds { get; init; } = [];
    public IReadOnlyList<string> TurnoverIds { get; init; } = [];
    public IReadOnlyList<string> DangerousTurnoverIds { get; init; } = [];
    public IReadOnlyList<string> HighRecoveryIds { get; init; } = [];
    public IReadOnlyList<string> RecoveryIds { get; init; } = [];
    public IReadOnlyList<string> CornerIds { get; init; } = [];
    public IReadOnlyList<string> FastTransitionIds { get; init; } = [];

    public IReadOnlyList<string> AttackIds(AttackChannel channel) => channel switch
    {
        AttackChannel.Left => LeftAttackIds,
        AttackChannel.Centre => CentreAttackIds,
        _ => RightAttackIds,
    };

    public int AttacksIn(AttackChannel channel) => channel switch
    {
        AttackChannel.Left => LeftAttacks,
        AttackChannel.Centre => CentreAttacks,
        _ => RightAttacks,
    };

    public double ChannelShare(AttackChannel channel) => Share(AttacksIn(channel));

    public double PerMinute(double count) => Window.Minutes <= 0 ? 0 : count / Window.Minutes;

    private double Share(int count) => Attacks == 0 ? 0 : (double)count / Attacks;
}

/// <summary>Both teams over the same window.</summary>
public sealed record WindowMetrics(AnalysisWindow Window, TeamWindowMetrics Home, TeamWindowMetrics Away)
{
    public TeamWindowMetrics For(string teamId) => teamId == Home.TeamId ? Home : Away;
    public TeamWindowMetrics Against(string teamId) => teamId == Home.TeamId ? Away : Home;

    public int TotalEvents => Home.EventCount + Away.EventCount;
    public int TotalTurnovers => Home.Turnovers + Away.Turnovers;
    public int TotalShots => Home.Shots + Away.Shots;
    public int TotalPossessions => Home.PossessionsStarted + Away.PossessionsStarted;
    public int TotalFastTransitions => Home.FastTransitions + Away.FastTransitions;
}
