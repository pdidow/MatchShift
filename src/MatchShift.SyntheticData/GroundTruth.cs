using MatchShift.Core.Insights;

namespace MatchShift.SyntheticData;

/// <summary>
/// The pattern a scripted scenario was DESIGNED to contain.
///
/// Ground truth is kept strictly outside the event stream: the analytics engine
/// only receives <see cref="Core.Domain.Match.Events"/>. Tests and the dataset
/// evaluator compare detector output against these labels to measure precision
/// and recall.
/// </summary>
public sealed record ExpectedPattern(
    InsightType Pattern,
    string TeamId,
    int WindowStartMinute,
    int WindowEndMinute,
    string Note)
{
    /// <summary>True when a detection at <paramref name="minute"/> falls inside the window (with tolerance).</summary>
    public bool Matches(InsightType type, string? teamId, double minute, int toleranceMinutes = 2) =>
        type == Pattern &&
        (teamId is null || TeamId == "*" || teamId == TeamId) &&
        minute >= WindowStartMinute - toleranceMinutes &&
        minute <= WindowEndMinute + toleranceMinutes;
}

public sealed record GroundTruth(
    string MatchId,
    string ScenarioName,
    long Seed,
    IReadOnlyList<ExpectedPattern> ExpectedPatterns,
    /// <summary>Patterns that must NOT be detected for this scenario (negative controls).</summary>
    IReadOnlyList<InsightType> ForbiddenPatterns);

public sealed record LabelledMatch(Core.Domain.Match Match, GroundTruth GroundTruth);
