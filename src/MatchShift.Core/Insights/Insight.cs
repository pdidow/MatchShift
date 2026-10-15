using System.Text.Json.Serialization;
using MatchShift.Core.Domain;

namespace MatchShift.Core.Insights;

[JsonConverter(typeof(JsonStringEnumConverter<Severity>))]
public enum Severity
{
    Low,
    Medium,
    High
}

[JsonConverter(typeof(JsonStringEnumConverter<ConfidenceLevel>))]
public enum ConfidenceLevel
{
    Low,
    Medium,
    High
}

/// <summary>How a value should be formatted by renderers (never changes the value itself).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MetricUnit>))]
public enum MetricUnit
{
    /// <summary>0-1 proportion rendered as a percentage.</summary>
    Share,
    Count,
    PerMinute,

    /// <summary>0-100 MatchShift index.</summary>
    Index,
    Seconds,
    Quality,

    /// <summary>A test statistic such as a z-score.</summary>
    Statistic
}

/// <summary>A span of match time in seconds, [FromSecond, ToSecond).</summary>
public sealed record TimeSpanRef(int FromSecond, int ToSecond)
{
    public int FromMinute => FromSecond / 60;
    public int ToMinute => (ToSecond + 59) / 60;
    public string Label => $"{FromMinute}'-{ToMinute}'";
}

/// <summary>
/// One piece of structured evidence. Evidence is produced by deterministic code
/// BEFORE any narrative exists; narratives may only cite values found here.
/// </summary>
public sealed record EvidenceItem
{
    /// <summary>Stable metric key, e.g. "RightAttackShare". Renderers localise by key.</summary>
    public required string Metric { get; init; }

    public required MetricUnit Unit { get; init; }
    public double? BaselineValue { get; init; }
    public required double RecentValue { get; init; }
    public TimeSpanRef? BaselineWindow { get; init; }
    public TimeSpanRef? RecentWindow { get; init; }
    public double? Difference { get; init; }
    public double? Threshold { get; init; }

    /// <summary>Independent observations behind <see cref="RecentValue"/> (e.g. possessions).</summary>
    public int SampleSize { get; init; }

    public int? BaselineSampleSize { get; init; }
    public int? MinimumSampleSize { get; init; }
    public bool SampleRequirementMet { get; init; } = true;

    /// <summary>Name of the comparison, e.g. "last 10 vs previous 15".</summary>
    public string? Comparison { get; init; }

    /// <summary>True for the single item the insight's headline is about.</summary>
    public bool IsPrimary { get; init; }
}

/// <summary>What SHOW ME should draw for an insight.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VisualStyle>))]
public enum VisualStyle
{
    /// <summary>Arrows from start to end location.</summary>
    Routes,

    /// <summary>Markers at the event location.</summary>
    Points,

    /// <summary>Shot markers sized by chance quality.</summary>
    Shots,

    /// <summary>Zone shading by channel.</summary>
    Channels
}

public sealed record VisualLayer(string Key, VisualStyle Style, IReadOnlyList<string> EventIds, bool Emphasised = true);

/// <summary>
/// A language-neutral, verified finding. It stores semantic content (type, facts,
/// evidence, interpretation codes) - never rendered prose. Narratives in any
/// language and for any viewer are rendered from this record.
/// </summary>
public sealed record Insight
{
    public required string Id { get; init; }
    public required string MatchId { get; init; }
    public required InsightType Type { get; init; }

    /// <summary>Match second at which the insight was detected (live).</summary>
    public required int DetectedAtSecond { get; init; }

    /// <summary>The span of play the insight describes.</summary>
    public required TimeSpanRef Span { get; init; }

    public string? TeamId { get; init; }

    /// <summary>Most involved player, if any (used for "My Player" and narrative colour).</summary>
    public string? KeyPlayerId { get; init; }

    public IReadOnlyList<string> RelatedPlayerIds { get; init; } = [];

    public required Severity Severity { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public required double ConfidenceScore { get; init; }

    /// <summary>
    /// Structured facts, e.g. { "channel": "Right" }. Values are invariant strings;
    /// numbers belong in <see cref="Evidence"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Facts { get; init; } = new Dictionary<string, string>();

    public required IReadOnlyList<EvidenceItem> Evidence { get; init; }
    public required IReadOnlyList<string> SupportingEventIds { get; init; }
    public IReadOnlyList<VisualLayer> Visuals { get; init; } = [];

    /// <summary>Code of the cautious interpretation the narrative layer may offer (never a claim of intent).</summary>
    public string? InterpretationCode { get; init; }

    /// <summary>0-100 importance used for highlight ranking and "just tell me what matters".</summary>
    public double Importance { get; init; }

    public int DisplayMinute => MatchClock.DisplayMinute(DetectedAtSecond);

    public EvidenceItem? PrimaryEvidence => Evidence.FirstOrDefault(e => e.IsPrimary) ?? Evidence.FirstOrDefault();

    public string? Fact(string key) => Facts.TryGetValue(key, out var v) ? v : null;
}
