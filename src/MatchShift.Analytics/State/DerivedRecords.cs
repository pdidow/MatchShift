using MatchShift.Core.Domain;

namespace MatchShift.Analytics.State;

public enum PossessionStartReason
{
    Restart,
    KickOff,
    Regain,
    SetPiece
}

public enum PossessionEndReason
{
    Open,
    Continued,
    Shot,
    Lost,
    Offside,
    PeriodEnd,
    Other
}

public enum LossKind
{
    MisplacedPass,
    Dispossessed
}

/// <summary>A possession chain derived from <see cref="MatchEvent.PossessionId"/>.</summary>
public sealed class Possession(int id, string teamId, int startSecond, PossessionStartReason startReason)
{
    public int Id { get; } = id;
    public string TeamId { get; } = teamId;
    public int StartSecond { get; } = startSecond;
    public PossessionStartReason StartReason { get; } = startReason;
    public int LastSecond { get; internal set; } = startSecond;
    public PossessionEndReason EndReason { get; internal set; } = PossessionEndReason.Open;
    public List<string> EventIds { get; } = [];
    public double? StartX { get; internal set; }
    public int TeamEventCount { get; internal set; }
    public int ActionCount { get; internal set; }
    public int PassCount { get; internal set; }
    public int ShotCount { get; internal set; }
    public int? FirstShotSecond { get; internal set; }
    public int? FirstFinalThirdSecond { get; internal set; }

    public int DurationSeconds => Math.Max(0, LastSecond - StartSecond);

    /// <summary>
    /// A fast transition: the ball is won back in open play and carried into the
    /// attacking third within <paramref name="withinSeconds"/>.
    /// </summary>
    public bool IsFastTransition(int withinSeconds = 15) =>
        StartReason == PossessionStartReason.Regain &&
        FirstFinalThirdSecond is not null &&
        FirstFinalThirdSecond.Value - StartSecond <= withinSeconds &&
        (StartX ?? 0) < Pitch.AttackingThirdStart;

    /// <summary>A counter-attack: a fast transition that produces a shot within <paramref name="shotWithinSeconds"/>.</summary>
    public bool IsCounterAttackWithShot(int shotWithinSeconds = 25) =>
        IsFastTransition() && FirstShotSecond is not null && FirstShotSecond.Value - StartSecond <= shotWithinSeconds;
}

/// <summary>A completed pass or carry that moved the ball into the attacking third.</summary>
public sealed record ThirdEntry(
    string EventId, int Second, string TeamId, int PossessionId, AttackChannel Channel, double X, double Y, string? PlayerId);

/// <summary>An on-ball action located in the attacking third (see <see cref="MatchState.AttackingActions"/>).</summary>
public sealed record AttackingAction(
    string EventId, int Second, string TeamId, int PossessionId, AttackChannel Channel, double X, double Y);

public sealed record BoxEntry(string EventId, int Second, string TeamId, int PossessionId, double X, double Y, string? PlayerId);

/// <summary>Possession lost in open play. Location is in the losing team's frame.</summary>
public sealed record PossessionLoss(
    string EventId, int Second, string TeamId, string? PlayerId, int PossessionId, double X, double Y, LossKind Kind)
{
    public bool InOwnDefensiveThird => Pitch.IsInDefensiveThird(X);
    public bool InOwnHalf => X < 50;
}

public sealed record ShotRecord(
    string EventId,
    int Second,
    string TeamId,
    string? PlayerId,
    int PossessionId,
    double X,
    double Y,
    double Quality,
    bool OnTarget,
    bool IsGoal,
    bool IsHeader);

public sealed record SubstitutionRecord(string EventId, int Second, string TeamId, string PlayerOffId, string PlayerOnId);
