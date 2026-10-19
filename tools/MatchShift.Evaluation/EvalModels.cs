using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MatchShift.Agents.Workflow;

namespace MatchShift.Evaluation;

/// <summary>Good: real agent output from a live workflow run. Adversarial: a real output deliberately corrupted with one known defect.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CaseKind>))]
public enum CaseKind { Good, Adversarial }

/// <summary>Which generated text a row evaluates.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Variant>))]
public enum Variant { Audience, Accessibility }

/// <summary>
/// One evaluation case: the authoritative context MatchShift computed deterministically, the text an agent generated
/// from it, and what each quality layer decided. Holds no credentials, prompts, instructions or hidden reasoning.
/// </summary>
public sealed record EvalCase
{
    public required string CaseId { get; init; }
    public required CaseKind Kind { get; init; }
    public required Variant Variant { get; init; }
    public required string InsightId { get; init; }
    public required string InsightType { get; init; }
    public required string Language { get; init; }
    public required string AudienceMode { get; init; }
    public required string MatchTime { get; init; }

    /// <summary>Readable authoritative facts (the evaluator "context"). Built only from InsightContext.</summary>
    public required string AuthoritativeFacts { get; init; }
    public required IReadOnlyList<MetricFact> DeterministicMetrics { get; init; }
    public required IReadOnlyList<string> SupportingEvidenceIds { get; init; }
    public required IReadOnlyList<SourceEventFact> SourceEvents { get; init; }

    public TacticalAnalystResult? TacticalOutput { get; init; }
    public AudienceNarrativeResult? AudienceOutput { get; init; }
    public AccessibilityNarrativeResult? AccessibilityOutput { get; init; }

    /// <summary>The text under evaluation: what the viewer is shown (published audience headline+summary, or the published spoken summary).</summary>
    public required string Response { get; init; }

    /// <summary>Good cases: the agent's own text before verification and repair, and the deterministic findings on it.</summary>
    public string? RawAgentResponse { get; init; }
    public IReadOnlyList<string> RawAgentFindings { get; init; } = [];

    /// <summary>The viewer request the response answers (the evaluator "query").</summary>
    public required string Query { get; init; }

    /// <summary>Workflow outcome for good cases (APPROVED, REJECTED, ...); null for adversarial rows.</summary>
    public string? WorkflowOutcome { get; init; }
    public string? VerifierDecision { get; init; }
    public bool? PublishedVerified { get; init; }

    /// <summary>Adversarial only: the defect injected and whether a correct system should accept the text.</summary>
    public string? Mutation { get; init; }
    public bool ShouldPass { get; init; } = true;
    public string ExpectedGroundingNotes { get; init; } = "";

    /// <summary>Deterministic validator findings on <see cref="Response"/> (code checks against InsightContext).</summary>
    public IReadOnlyList<string> DeterministicFindings { get; init; } = [];

    /// <summary>Adversarial only: findings on the mutated text that the unmutated base text did not have (the defect's own catch).</summary>
    public IReadOnlyList<string> IntroducedFindings { get; init; } = [];

    /// <summary>Independent Verification Agent verdict on this exact text (no deterministic findings given to it).</summary>
    public string? IndependentVerifierDecision { get; init; }
    public IReadOnlyList<string> IndependentVerifierClaims { get; init; } = [];
}

public static class EvalJson
{
    public static JsonSerializerOptions Options { get; } = new(AgentJson.Options) { WriteIndented = false };
    public static JsonSerializerOptions Pretty { get; } = new(AgentJson.Options) { WriteIndented = true };

    public static string ToJsonl(IEnumerable<EvalCase> cases) =>
        string.Join('\n', cases.Select(c => JsonSerializer.Serialize(c, Options))) + "\n";

    public static IReadOnlyList<EvalCase> FromJsonl(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonSerializer.Deserialize<EvalCase>(l, Options)!)];
}

/// <summary>Turns the authoritative InsightContext into the plain-text "context" a Foundry groundedness judge reads.</summary>
public static class AuthoritativeContext
{
    public static string Text(InsightContext c)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Synthetic match {c.HomeTeam.Name} v {c.AwayTeam.Name}. Insight {c.InsightType} detected at {c.MatchTime} (minute {c.DisplayMinute}); score {c.HomeTeam.ShortName} {c.Score.Home}-{c.Score.Away} {c.AwayTeam.ShortName}.");
        if (c.SubjectTeam is { } t) sb.AppendLine($"Team concerned: {t.Name}.");
        sb.AppendLine($"Deterministic statement: {c.DeterministicNarrative.Fact}");
        sb.AppendLine($"Deterministic inference: {c.DeterministicNarrative.Inference}");
        foreach (var m in c.DeterministicMetrics)
        {
            var windows = m.BaselineWindow is { } bw && m.CurrentWindow is { } cw ? $" (baseline {bw.Label}, current {cw.Label})" : m.CurrentWindow is { } w ? $" ({w.Label})" : "";
            sb.AppendLine($"Metric {m.Label}: current {m.DisplayCurrent}{(m.DisplayBaseline is null ? "" : $", baseline {m.DisplayBaseline}")}" +
                          $"{(m.DisplayChange is null ? "" : $", change {m.DisplayChange}")}{(m.DisplayThreshold is null ? "" : $", threshold {m.DisplayThreshold}")}" +
                          $"{windows}; sample {m.SampleSize}{(m.BaselineSampleSize is { } bs ? $", baseline sample {bs}" : "")}{(m.MinimumSample is { } min ? $" (minimum {min})" : "")}.");
        }
        sb.AppendLine($"Confidence: {c.ConfidenceInputs.Level} ({c.ConfidenceInputs.Score:0.00}); sample requirements {(c.ConfidenceInputs.AllSampleRequirementsMet ? "met" : "not all met")}.");
        foreach (var (k, v) in c.Facts) sb.AppendLine($"Fact {k}: {v}.");
        foreach (var e in c.SourceEvents)
            sb.AppendLine($"Event {e.Id} at {e.Clock}: {e.Type} {e.Outcome} by {e.PlayerName ?? e.TeamId}{(e.Zone is null ? "" : $" in the {e.Zone}")}.");
        sb.Append("Players involved: ").AppendJoin(", ", c.Players.Select(p => p.Name)).Append('.');
        return sb.ToString();
    }

    public static string Query(InsightContext c) =>
        $"Explain the {c.InsightType.Replace('_', ' ').ToLowerInvariant()} detected at {c.MatchTime} for a {c.SelectedAudienceMode} viewer, in language '{c.Language}', using only the match evidence.";
}
