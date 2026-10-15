using System.Text.Json.Serialization;

namespace MatchShift.Core.Insights;

[JsonConverter(typeof(JsonStringEnumConverter<HighlightKind>))]
public enum HighlightKind
{
    Goal,
    BigChance,
    DangerousTurnover,
    TacticalShift,
    PressurePeak,
    ControlChange,
    SubstitutionImpact,
    CounterAttack,
    ChaoticSpell
}

/// <summary>
/// A ranked moment for the highlight timeline. Like <see cref="Insight"/>, it is semantic:
/// headline and "why it matters" text are rendered later from <see cref="WhyItMattersCode"/>.
/// </summary>
public sealed record Highlight
{
    public required string Id { get; init; }
    public required int Second { get; init; }
    public required HighlightKind Kind { get; init; }
    public string? TeamId { get; init; }
    public string? InsightId { get; init; }
    public InsightType? InsightType { get; init; }
    public required double ImportanceScore { get; init; }
    public required string WhyItMattersCode { get; init; }
    public IReadOnlyDictionary<string, string> Facts { get; init; } = new Dictionary<string, string>();
    public required IReadOnlyList<EvidenceItem> Evidence { get; init; }
    public required IReadOnlyList<string> RelevantEventIds { get; init; }

    public int DisplayMinute => Domain.MatchClock.DisplayMinute(Second);
}

[JsonConverter(typeof(JsonStringEnumConverter<RecapPeriod>))]
public enum RecapPeriod
{
    HalfTime,
    FullTime
}

public sealed record GoalSummary(int Second, string TeamId, string? ScorerId, string? AssistId, string InsightId, string? InterpretationCode,
    int HomeScore, int AwayScore)
{
    public int DisplayMinute => Domain.MatchClock.DisplayMinute(Second);
}

/// <summary>A spell in which one team held Match Control above the dominance threshold.</summary>
public sealed record MomentumPeriod(int FromMinute, int ToMinute, string TeamId, double AverageControl, double PeakPressure);

public sealed record PressurePeak(string TeamId, int Minute, double Value);

public sealed record TeamRecapStats(
    string TeamId,
    int Goals,
    double PossessionShare,
    int Shots,
    int ShotsOnTarget,
    double ChanceQuality,
    int BigChances,
    int Attacks,
    double LeftAttackShare,
    double CentreAttackShare,
    double RightAttackShare,
    int Turnovers,
    int DangerousTurnovers,
    int HighRecoveries,
    double AveragePressure,
    double PeakPressure);

public sealed record PlayerContribution(
    string PlayerId,
    string TeamId,
    int Goals,
    int Assists,
    int Shots,
    int KeyPasses,
    int Recoveries,
    double ChanceQuality,
    int InsightMentions,
    double Score);

/// <summary>An ordered beat of the match story, rendered into prose by the narrative layer.</summary>
public sealed record StoryBeat(int Minute, string Code, string? TeamId, string? InsightId, IReadOnlyDictionary<string, string> Facts);

/// <summary>
/// Everything a recap may say, computed deterministically. Recap renderers (template or LLM)
/// may only use what is in here; the verification agent checks their output against it.
/// </summary>
public sealed record RecapFacts
{
    public required string MatchId { get; init; }
    public required RecapPeriod Period { get; init; }
    public required int UpToSecond { get; init; }
    public required string HomeTeamId { get; init; }
    public required string AwayTeamId { get; init; }
    public required int HomeScore { get; init; }
    public required int AwayScore { get; init; }
    public required IReadOnlyList<GoalSummary> Goals { get; init; }
    public required IReadOnlyList<MomentumPeriod> MomentumPeriods { get; init; }
    public required IReadOnlyList<Insight> KeyShifts { get; init; }
    public required IReadOnlyList<Insight> ControlChanges { get; init; }
    public required IReadOnlyList<PressurePeak> PressurePeaks { get; init; }
    public required IReadOnlyList<Highlight> TopHighlights { get; init; }
    public required TeamRecapStats Home { get; init; }
    public required TeamRecapStats Away { get; init; }
    public required IReadOnlyList<PlayerContribution> KeyPlayers { get; init; }
    public required IReadOnlyList<StoryBeat> Story { get; init; }
    public required double ChaoticMinuteShare { get; init; }

    public string? Winner => HomeScore > AwayScore ? HomeTeamId : AwayScore > HomeScore ? AwayTeamId : null;

    public TeamRecapStats StatsFor(string teamId) => teamId == HomeTeamId ? Home : Away;
}
