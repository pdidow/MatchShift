using MatchShift.Agents.Narrative;
using MatchShift.Analytics;
using MatchShift.Analytics.Indices;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Api.Contracts;

public sealed record TeamDto(string Id, string Name, string ShortName, string Code, string PrimaryColor, string SecondaryColor, string Formation);

public sealed record PlayerDto(string Id, string Name, string ShortName, int ShirtNumber, string Position, string TeamId, bool IsStarter);

public sealed record MatchSummaryDto(string Id, string Scenario, long Seed, TeamDto Home, TeamDto Away, Score FinalScore, int EventCount, bool IsSynthetic);

public sealed record MatchDetailDto(MatchSummaryDto Summary, string Venue, string Competition, IReadOnlyList<PlayerDto> HomeSquad, IReadOnlyList<PlayerDto> AwaySquad);

/// <summary>An event in ABSOLUTE pitch coordinates: the home team attacks left-to-right (towards X = 100).</summary>
public sealed record PitchEventDto(
    string Id, int Seq, int Second, string Clock, string Type, string Outcome, string TeamId, string? PlayerId, string? PlayerName,
    double? X, double? Y, double? EndX, double? EndY, string? Qualifier);

public sealed record ClockDto(int Second, int Minute, int Period, string Label);

public sealed record TeamLiveMetricsDto(
    string TeamId, double Possession, double Pressure, int Shots, int ShotsOnTarget, double ChanceQuality,
    int Attacks, double LeftShare, double CentreShare, double RightShare, int Turnovers, int HighRecoveries);

public sealed record LiveMetricsDto(string Window, TeamLiveMetricsDto Home, TeamLiveMetricsDto Away, double HomeControl,
    double Chaos, ChaosState ChaosState, IReadOnlyList<IndexComponent> ControlComponents, IReadOnlyList<IndexComponent> ChaosComponents);

public sealed record InsightCardDto(
    string Id, string Type, int Minute, int Second, string? TeamId, Severity Severity, ConfidenceLevel Confidence, double ConfidenceScore,
    double Importance, string? KeyPlayerId, TimeSpanRef Span, IReadOnlyList<VisualLayer> Visuals, int SupportingEventCount,
    InsightNarrative Narrative);

public sealed record HighlightCardDto(string Id, HighlightKind Kind, int Minute, int Second, string? TeamId, double Importance,
    string? InsightId, string Headline, string WhyItMatters, int RelevantEventCount);

public sealed record SnapshotDto(
    string MatchId,
    bool IsPlaying,
    int Speed,
    bool Finished,
    ClockDto Clock,
    Score Score,
    TeamDto Home,
    TeamDto Away,
    IReadOnlyList<PitchEventDto> RecentEvents,
    LiveMetricsDto Metrics,
    IReadOnlyList<TimelinePoint> Timeline,
    IReadOnlyList<InsightCardDto> Insights,
    IReadOnlyList<HighlightCardDto> Highlights,
    ViewerProfile Profile,
    bool Synthetic = true);

public sealed record EvidenceResponseDto(InsightCardDto Insight, string WhyTitle, IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<EvidenceItem> RawEvidence, IReadOnlyDictionary<string, string> Facts);

public sealed record InsightEventsDto(string InsightId, IReadOnlyList<VisualLayer> Layers, IReadOnlyList<PitchEventDto> Events);

public sealed record SpeedRequest(int Speed);
public sealed record SeekRequest(double Minute);
public sealed record ResetRequest(string? MatchId);

public sealed record MetaDto(
    IReadOnlyList<LanguageDto> Languages, IReadOnlyList<string> Modes, IReadOnlyList<string> RecapStyles, IReadOnlyList<int> Speeds,
    string AiProvider, bool AiAvailable, string DataNotice);

public sealed record LanguageDto(string Code, string Name);
