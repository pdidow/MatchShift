using MatchShift.Analytics.Indices;
using MatchShift.Analytics.Metrics;
using MatchShift.Analytics.State;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Detection;

/// <summary>Everything a detector may look at during one evaluation tick. Metrics are memoised per window.</summary>
public sealed class DetectionContext(MatchState state, int nowSecond, DetectionOptions options)
{
    private readonly Dictionary<AnalysisWindow, WindowMetrics> _cache = [];

    public MatchState State { get; } = state;
    public int Now { get; } = nowSecond;
    public DetectionOptions Options { get; } = options;
    public MatchContext Match => State.Context;

    public WindowMetrics Metrics(AnalysisWindow window)
    {
        if (!_cache.TryGetValue(window, out var m))
        {
            m = MetricsCalculator.Compute(State, window);
            _cache[window] = m;
        }
        return m;
    }

    public (WindowMetrics Baseline, WindowMetrics Recent, WindowComparison Comparison) Compare(double recentMinutes, double baselineMinutes)
    {
        var comparison = new WindowComparison(
            $"last {recentMinutes:0} vs previous {baselineMinutes:0}",
            AnalysisWindow.Before(Now, recentMinutes, baselineMinutes),
            AnalysisWindow.Last(Now, recentMinutes));
        return (Metrics(comparison.Baseline), Metrics(comparison.Recent), comparison);
    }

    /// <summary>Recent window vs everything in the match before it ("recent period vs match baseline").</summary>
    public (WindowMetrics Baseline, WindowMetrics Recent, WindowComparison Comparison) CompareWithMatch(double recentMinutes)
    {
        var comparison = WindowComparison.RecentVsMatch(Now, recentMinutes);
        return (Metrics(comparison.Baseline), Metrics(comparison.Recent), comparison);
    }

    public string? MostInvolvedPlayer(IEnumerable<string> eventIds)
    {
        return eventIds
            .Select(id => State.FindEvent(id)?.PlayerId)
            .Where(p => p is not null)
            .GroupBy(p => p!)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    public int Involvement(IEnumerable<string> eventIds, string? playerId) =>
        playerId is null ? 0 : eventIds.Count(id => State.FindEvent(id)?.PlayerId == playerId);

    public static TimeSpanRef Ref(AnalysisWindow w) => new(w.FromSecond, w.ToSecond);
}

/// <summary>A detector's raw finding before cooldown, persistence and ID assignment.</summary>
public sealed record DetectionCandidate
{
    public required InsightType Type { get; init; }
    public string? TeamId { get; init; }

    /// <summary>Distinguishes sub-kinds for cooldown purposes (e.g. the channel of a channel shift).</summary>
    public string? Variant { get; init; }

    /// <summary>For moments: the anchoring event, so the same moment is never reported twice.</summary>
    public string? AnchorEventId { get; init; }

    public required TimeSpanRef Span { get; init; }
    public required Severity Severity { get; init; }
    public required double ConfidenceScore { get; init; }
    public required IReadOnlyList<EvidenceItem> Evidence { get; init; }
    public required IReadOnlyList<string> SupportingEventIds { get; init; }
    public IReadOnlyDictionary<string, string> Facts { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<VisualLayer> Visuals { get; init; } = [];
    public string? InterpretationCode { get; init; }
    public string? KeyPlayerId { get; init; }
    public IReadOnlyList<string> RelatedPlayerIds { get; init; } = [];

    public string Key => $"{Type}|{TeamId}|{Variant}";
}

public interface IShiftDetector
{
    InsightType Type { get; }

    IEnumerable<DetectionCandidate> Evaluate(DetectionContext context);
}

internal static class EvidenceFactory
{
    public static EvidenceItem Change(
        string metric, MetricUnit unit, double baseline, double recent, WindowComparison comparison, double threshold,
        int sample, int baselineSample, int minimumSample, bool primary = false) => new()
    {
        Metric = metric,
        Unit = unit,
        BaselineValue = Round(baseline, unit),
        RecentValue = Round(recent, unit),
        Difference = Round(recent - baseline, unit),
        Threshold = threshold,
        BaselineWindow = DetectionContext.Ref(comparison.Baseline),
        RecentWindow = DetectionContext.Ref(comparison.Recent),
        SampleSize = sample,
        BaselineSampleSize = baselineSample,
        MinimumSampleSize = minimumSample,
        SampleRequirementMet = sample >= minimumSample && baselineSample >= minimumSample,
        Comparison = comparison.Name,
        IsPrimary = primary,
    };

    public static EvidenceItem Value(string metric, MetricUnit unit, double value, AnalysisWindow? window = null, int sample = 0,
        bool primary = false, double? threshold = null) => new()
    {
        Metric = metric,
        Unit = unit,
        RecentValue = Round(value, unit),
        RecentWindow = window is null ? null : DetectionContext.Ref(window.Value),
        SampleSize = sample,
        Threshold = threshold,
        IsPrimary = primary,
    };

    public static double Round(double value, MetricUnit unit) => unit switch
    {
        MetricUnit.Share => Math.Round(value, 3),
        MetricUnit.Index => Math.Round(value, 1),
        MetricUnit.PerMinute => Math.Round(value, 2),
        MetricUnit.Quality => Math.Round(value, 2),
        MetricUnit.Statistic => Math.Round(value, 2),
        _ => Math.Round(value, 0),
    };

    public static string ChannelName(AttackChannel c) => c.ToString();

    public static IndexResult Pressure(WindowMetrics m, string teamId) => PressureIndex.Compute(m.For(teamId));
}
