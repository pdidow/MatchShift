using System.Text.Json.Serialization;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Replay;

/// <summary>
/// Where a reconstructed position comes from. The 3D replay is a reconstruction: only some positions are
/// recorded in the synthetic event data, and the renderer must be able to say which.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PositionProvenance>))]
public enum PositionProvenance
{
    /// <summary>The position is recorded in an event (actor location, pass end, interception point...).</summary>
    [JsonStringEnumMemberName("ACTUAL_EVENT_POSITION")] ActualEventPosition,

    /// <summary>Between two known positions (or blending towards one) - animation only.</summary>
    [JsonStringEnumMemberName("INTERPOLATED_POSITION")] InterpolatedPosition,

    /// <summary>No nearby event: formation role shifted with the ball. Illustrative, not observed.</summary>
    [JsonStringEnumMemberName("ESTIMATED_POSITION")] EstimatedPosition,
}

[JsonConverter(typeof(JsonStringEnumConverter<BallMotion>))]
public enum BallMotion
{
    /// <summary>At a player's feet; the renderer attaches the ball to <see cref="BallSegment.HolderId"/>'s track.</summary>
    Held,
    Pass,
    Carry,
    Shot,
    /// <summary>Not under control (after a block or a misplaced pass) - drifts between two points.</summary>
    Loose,
}

public sealed record ReplayPlayer(
    string Id, string TeamId, bool IsHome, int ShirtNumber, string Name, string ShortName, string Position, bool IsGoalkeeper);

/// <summary>An event placed on the replay clock. Coordinates are ABSOLUTE (home attacks towards X = 100), 0-100.</summary>
public sealed record ReplayEvent(
    string EventId,
    int Sequence,
    double Time,
    double EndTime,
    int MatchSecond,
    string Clock,
    string Type,
    string Outcome,
    string Label,
    string TeamId,
    string? PlayerId,
    string? RelatedPlayerId,
    double? X,
    double? Y,
    double? EndX,
    double? EndY,
    string? Qualifier,
    bool IsEvidence);

public sealed record TrackSample(double T, double X, double Y, PositionProvenance Provenance);

public sealed record PlayerTrack(string PlayerId, IReadOnlyList<TrackSample> Samples);

/// <summary>
/// One continuous piece of ball movement. Pass/carry/shot endpoints are event positions; the path between
/// them is interpolated. <see cref="ArcHeight"/> is a visual aid only - the data contains no ball height.
/// </summary>
public sealed record BallSegment(
    double T0, double T1, BallMotion Motion,
    double X0, double Y0, double X1, double Y1, double Z0, double Z1, double ArcHeight,
    string? HolderId, string? EventId, PositionProvenance Provenance);

/// <summary>Tactical overlay drawn on the 3D pitch. Kinds: Channel, Zone, Marker, PressingPlayers, PassPath.</summary>
public sealed record ReplayOverlay(
    string Kind,
    string? TeamId,
    string? Label,
    double? X0, double? Y0, double? X1, double? Y1,
    IReadOnlyList<string> PlayerIds,
    IReadOnlyList<string> EventIds,
    double FromTime,
    double ToTime);

/// <summary>Camera hints computed from the clip: who attacks which way, whom to follow, where play is.</summary>
public sealed record ReplayCamera(string AttackingTeamId, int AttackDirection, string? FocusPlayerId, double CentroidX, double CentroidY);

public sealed record ReplayClip(
    string Key,
    string Label,
    int StartSecond,
    int EndSecond,
    double Duration,
    Score ScoreAtStart,
    IReadOnlyList<ReplayEvent> Events,
    IReadOnlyList<PlayerTrack> Tracks,
    IReadOnlyList<BallSegment> Ball,
    IReadOnlyList<ReplayOverlay> Overlays,
    ReplayCamera Camera);

/// <summary>A 3D replay reconstructed for one insight: one clip, or a BEFORE / AFTER pair for tactical shifts.</summary>
public sealed record ReplaySequence(
    string MatchId,
    string InsightId,
    string InsightType,
    string? TeamId,
    string HomeTeamId,
    string AwayTeamId,
    TimeSpanRef InsightWindow,
    IReadOnlyList<string> EvidenceEventIds,
    IReadOnlyList<string> RelevantPlayerIds,
    IReadOnlyList<ReplayPlayer> Players,
    IReadOnlyList<ReplayClip> Clips,
    string Notice)
{
    public double TotalDuration => Clips.Sum(c => c.Duration);
}
