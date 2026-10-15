using MatchShift.Analytics;
using MatchShift.Analytics.Detection;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;

namespace MatchShift.Infrastructure.Evaluation;

/// <summary>
/// Measures the MatchShiftDetector against labelled synthetic scenarios.
///
/// The analytics engine only ever receives events. Afterwards its insights are compared with
/// each scenario's <see cref="GroundTruth"/>:
///
/// - RECALL: an expected pattern is recalled if an insight of that type and team is raised between
///   <c>WindowStart - tolerance</c> and <c>WindowEnd + latency</c> (a live detector needs a few minutes
///   of evidence, so detections trail the behaviour change).
/// - FALSE ALARMS: for <see cref="StrictTypes"/> - patterns the generator never produces by chance -
///   every detection in a match whose ground truth does not contain that pattern is a false alarm.
///   The rate on negative-control matches (no expected patterns of that type) is the headline noise figure.
/// - PRECISION (strict types): recalled detections / (recalled + false alarms). Detections for the
///   labelled team after the labelled window ends ("aftermath", e.g. play reverting) are excluded.
/// - Non-strict types (pressure, control, chaos...) happen naturally in football, so unlabelled detections
///   are reported as a per-match background rate, not as errors.
/// </summary>
public static class DetectorEvaluator
{
    public const int DefaultToleranceMinutes = 2;
    public const int DefaultLatencyMinutes = 8;

    public static readonly IReadOnlySet<InsightType> StrictTypes = new HashSet<InsightType>
    {
        InsightType.AttackChannelShift,
        InsightType.PostSubstitutionChange,
    };

    public static EvaluationReport Evaluate(
        IEnumerable<LabelledMatch> matches,
        DetectionOptions? options = null,
        int toleranceMinutes = DefaultToleranceMinutes,
        IReadOnlyList<IShiftDetector>? detectors = null,
        int latencyMinutes = DefaultLatencyMinutes)
    {
        var rows = new Dictionary<InsightType, TypeStats>();
        TypeStats Row(InsightType t) => rows.TryGetValue(t, out var r) ? r : rows[t] = new TypeStats(t);
        var matchCount = 0;
        var forbiddenViolations = 0;
        var perScenario = new Dictionary<string, (int Expected, int Recalled)>();

        foreach (var labelled in matches)
        {
            matchCount++;
            var insights = LiveMatchAnalyzer.AnalyzeFull(labelled.Match, options, detectors).Insights;
            var truth = labelled.GroundTruth;

            bool InWindow(ExpectedPattern p, Insight i) =>
                p.Pattern == i.Type &&
                (p.TeamId == "*" || p.TeamId == i.TeamId) &&
                i.DetectedAtSecond / 60.0 >= p.WindowStartMinute - toleranceMinutes &&
                i.DetectedAtSecond / 60.0 <= p.WindowEndMinute + latencyMinutes;

            foreach (var expected in truth.ExpectedPatterns)
            {
                var row = Row(expected.Pattern);
                row.Expected++;
                var hit = insights.Any(i => InWindow(expected, i));
                if (hit) row.Recalled++;
                var current = perScenario.GetValueOrDefault(truth.ScenarioName);
                perScenario[truth.ScenarioName] = (current.Expected + 1, current.Recalled + (hit ? 1 : 0));
            }

            foreach (var type in StrictTypes)
            {
                if (truth.ExpectedPatterns.All(p => p.Pattern != type)) Row(type).NegativeControlMatches++;
            }

            foreach (var insight in insights)
            {
                if (insight.Type.IsMoment()) continue;
                var row = Row(insight.Type);
                row.Detections++;
                if (truth.ForbiddenPatterns.Contains(insight.Type)) forbiddenViolations++;

                var labelledOfType = truth.ExpectedPatterns.Where(p => p.Pattern == insight.Type).ToList();
                if (labelledOfType.Any(p => InWindow(p, insight)))
                {
                    row.MatchedDetections++;
                }
                else if (labelledOfType.Count == 0)
                {
                    row.FalseAlarms++;
                    row.NegativeControlFalseAlarms++;
                }
                else if (labelledOfType.Any(p => (p.TeamId == "*" || p.TeamId == insight.TeamId) &&
                                                 insight.DetectedAtSecond / 60.0 > p.WindowEndMinute + latencyMinutes))
                {
                    row.Aftermath++;
                }
                else
                {
                    row.FalseAlarms++;
                }
            }
        }

        return new EvaluationReport(
            matchCount,
            rows.Values.OrderBy(r => r.Type).Select(r => r.ToResult(matchCount)).ToList(),
            forbiddenViolations,
            perScenario.OrderBy(k => k.Key).Select(k => new ScenarioRecall(k.Key, k.Value.Expected, k.Value.Recalled)).ToList());
    }

    private sealed class TypeStats(InsightType type)
    {
        public InsightType Type { get; } = type;
        public int Expected, Recalled, Detections, MatchedDetections, FalseAlarms, Aftermath;
        public int NegativeControlMatches, NegativeControlFalseAlarms;

        public TypeEvaluation ToResult(int matches)
        {
            var strict = StrictTypes.Contains(Type);
            return new TypeEvaluation(
                Type,
                Expected,
                Recalled,
                Detections,
                MatchedDetections,
                Expected == 0 ? null : Math.Round((double)Recalled / Expected, 3),
                strict && MatchedDetections + FalseAlarms > 0 ? Math.Round((double)MatchedDetections / (MatchedDetections + FalseAlarms), 3) : null,
                strict && NegativeControlMatches > 0 ? Math.Round((double)NegativeControlFalseAlarms / NegativeControlMatches, 3) : null,
                Math.Round((double)(Detections - MatchedDetections) / Math.Max(1, matches), 2),
                Aftermath,
                strict);
        }
    }
}

public sealed record TypeEvaluation(
    InsightType Type,
    int Expected,
    int Recalled,
    int Detections,
    int MatchedDetections,
    double? Recall,
    double? Precision,
    double? FalseAlarmsPerNegativeMatch,
    double UnlabelledPerMatch,
    int Aftermath,
    bool Strict);

public sealed record ScenarioRecall(string Scenario, int Expected, int Recalled)
{
    public double Recall => Expected == 0 ? 1 : Math.Round((double)Recalled / Expected, 3);
}

public sealed record EvaluationReport(
    int Matches,
    IReadOnlyList<TypeEvaluation> Types,
    int ForbiddenViolations,
    IReadOnlyList<ScenarioRecall> Scenarios);
