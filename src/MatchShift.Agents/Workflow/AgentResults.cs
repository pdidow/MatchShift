using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MatchShift.Agents.Workflow;

/// <summary>How a workflow execution produced its agent stages. Never conflated in traces or UI.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExecutionMode>))]
public enum ExecutionMode
{
    /// <summary>Microsoft Agent Framework agents calling the configured Microsoft Foundry model deployment.</summary>
    [JsonStringEnumMemberName("LIVE_FOUNDRY")] LiveFoundry,

    /// <summary>The same Agent Framework workflow graph, but every agent stage uses deterministic template output. No model call.</summary>
    [JsonStringEnumMemberName("OFFLINE_TEMPLATE")] OfflineTemplate,

    /// <summary>A trace recorded earlier and replayed by a static demo that cannot call the backend.</summary>
    [JsonStringEnumMemberName("RECORDED_DEMO")] RecordedDemo,
}

[JsonConverter(typeof(JsonStringEnumConverter<ExecutionType>))]
public enum ExecutionType
{
    [JsonStringEnumMemberName("DETERMINISTIC")] Deterministic,
    [JsonStringEnumMemberName("MICROSOFT_AGENT_FRAMEWORK")] MicrosoftAgentFramework,
}

[JsonConverter(typeof(JsonStringEnumConverter<AgentRole>))]
public enum AgentRole
{
    [JsonStringEnumMemberName("TACTICAL_ANALYST")] TacticalAnalyst,
    [JsonStringEnumMemberName("AUDIENCE_NARRATIVE")] AudienceNarrative,
    [JsonStringEnumMemberName("ACCESSIBILITY_NARRATOR")] AccessibilityNarrator,
    [JsonStringEnumMemberName("VERIFICATION")] Verification,
}

[JsonConverter(typeof(JsonStringEnumConverter<StageStatus>))]
public enum StageStatus
{
    [JsonStringEnumMemberName("RUNNING")] Running,
    [JsonStringEnumMemberName("COMPLETED")] Completed,
    [JsonStringEnumMemberName("TEMPLATE")] Template,
    [JsonStringEnumMemberName("SKIPPED")] Skipped,
    [JsonStringEnumMemberName("DEGRADED")] Degraded,
    [JsonStringEnumMemberName("FALLBACK")] Fallback,
    [JsonStringEnumMemberName("FAILED")] Failed,
    [JsonStringEnumMemberName("CANCELLED")] Cancelled,
}

/// <summary>The decision labels a verifier may return. Anything else is malformed output.</summary>
public static class VerifierDecisions
{
    public const string Approved = "APPROVED";
    public const string ApprovedWithChanges = "APPROVED_WITH_CHANGES";
    public const string Rejected = "REJECTED";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Approved, ApprovedWithChanges, Rejected };
}

/// <summary>Final result of one workflow execution.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkflowOutcome>))]
public enum WorkflowOutcome
{
    /// <summary>Verifier approved and final deterministic validation passed.</summary>
    [JsonStringEnumMemberName("APPROVED")] Approved,

    /// <summary>Verifier corrections were applied and passed final deterministic validation.</summary>
    [JsonStringEnumMemberName("APPROVED_WITH_CHANGES")] ApprovedWithChanges,

    /// <summary>AI output rejected (by the verifier or by final validation). The deterministic insight is kept.</summary>
    [JsonStringEnumMemberName("REJECTED")] Rejected,

    /// <summary>An agent or the verifier was unavailable; AI output is NOT verified. The deterministic insight is kept.</summary>
    [JsonStringEnumMemberName("FALLBACK_UNVERIFIED")] FallbackUnverified,

    /// <summary>OFFLINE_TEMPLATE: no model ran; the deterministic template insight passed deterministic validation only.</summary>
    [JsonStringEnumMemberName("DETERMINISTIC_ONLY")] DeterministicOnly,

    [JsonStringEnumMemberName("CANCELLED")] Cancelled,
}

// ---------- Structured agent outputs (JSON schema is generated from these types). ----------

[Description("Tactical interpretation of the deterministic evidence. Facts, inference and possible explanation are kept separate.")]
public sealed record TacticalAnalystResult(
    [property: Description("Facts restated from insightContext only, with values exactly as given. No interpretation, no causes.")] IReadOnlyList<string> FactsUsed,
    [property: Description("What the facts suggest tactically. One or two sentences.")] string Inference,
    [property: Description("A hedged hypothesis (may/might/could) for why it happened, or empty if none is supported.")] string? PossibleExplanation,
    [property: Description("Why this matters for the match situation, without inventing facts.")] string? TacticalSignificance,
    [property: Description("How the deterministic confidence and sample sizes limit the reading.")] string ConfidenceQualification,
    [property: Description("IDs from insightContext.sourceEvents that support the inference.")] IReadOnlyList<string> EvidenceIds,
    [property: Description("What the evidence cannot show.")] IReadOnlyList<string> Limitations);

[Description("The same grounded insight communicated for one MatchShift audience mode.")]
public sealed record AudienceNarrativeResult(
    [property: Description("The audience mode written for.")] string Mode,
    [property: Description("Headline, at most 90 characters.")] string Headline,
    [property: Description("Main text for this audience, at most 280 characters.")] string Summary,
    [property: Description("Optional extra depth for this audience, or empty.")] string? Detail,
    [property: Description("One-line takeaway for the viewer.")] string KeyTakeaway);

[Description("The same grounded insight transformed for nonvisual and accessible use.")]
public sealed record AccessibilityNarrativeResult(
    [property: Description("Natural text-to-speech summary. Short sentences, no symbols or arrows.")] string SpokenSummary,
    [property: Description("Nonvisual description of where the supporting events happened, using only the zone fields.")] string? VisualDescription,
    [property: Description("Plain-language explanation for a newcomer, at most two short sentences.")] string SimplifiedExplanation,
    [property: Description("What the Evidence Replay will show, in event order, using only listed events.")] string? ReplayAudioDescription);

[Description("A corrected version of the viewer-facing text. Only supported claims.")]
public sealed record CorrectedNarrative(
    [property: Description("At most 90 characters.")] string Headline,
    [property: Description("At most 280 characters.")] string Summary,
    [property: Description("At most 250 characters, or null if unchanged.")] string? Inference,
    [property: Description("At most 300 characters, or null if unchanged.")] string? SpokenSummary);

/// <summary>
/// The verifier's verdict. Lists are short by contract (see <see cref="VerifierLimits"/>): the verdict and every unsupported
/// claim are what the workflow acts on; long prose only risks hitting the output-token limit and truncating the JSON.
/// </summary>
[Description("Independent semantic verification of every generated claim. Be concise.")]
public sealed record VerificationAgentResult(
    [property: Description("APPROVED, APPROVED_WITH_CHANGES or REJECTED.")] string Decision,
    [property: Description("At most 3 reasons, each under 20 words.")] IReadOnlyList<string> Reasons,
    [property: Description("Every claim not supported by insightContext, each quoted in under 15 words (at most 6).")] IReadOnlyList<string> UnsupportedClaims,
    [property: Description("At most 3 corrections, each under 20 words.")] IReadOnlyList<string> Corrections,
    [property: Description("Evidence IDs you checked against insightContext (at most 12).")] IReadOnlyList<string> EvidenceIdsChecked,
    [property: Description("Required for APPROVED_WITH_CHANGES; otherwise null.")] CorrectedNarrative? CorrectedNarrative,
    [property: Description("At most 2 limitations, each under 15 words, or empty.")] IReadOnlyList<string> Limitations);

/// <summary>The size contract the verifier is asked to keep, used by tests to prove a maximal verdict fits its token limit.</summary>
public static class VerifierLimits
{
    public const int Reasons = 3, ReasonWords = 20;
    public const int UnsupportedClaims = 6, ClaimWords = 15;
    public const int Corrections = 3, CorrectionWords = 20;
    public const int EvidenceIds = 12;
    public const int Limitations = 2, LimitationWords = 15;
    public const int HeadlineChars = 90, SummaryChars = 280, InferenceChars = 250, SpokenChars = 300;
}

// ---------- Published result: what MatchShift shows viewers once the workflow finishes. ----------

/// <summary>
/// The viewer-facing outcome. AI wording is present only when <see cref="Verified"/> is true; otherwise only the
/// deterministic insight (which never left deterministic code) is shown, and <see cref="Notice"/> says why.
/// </summary>
public sealed record PublishedInsight(
    string WorkflowExecutionId,
    string InsightId,
    ExecutionMode ExecutionMode,
    WorkflowOutcome Outcome,
    bool Verified,
    string Source,
    string? Headline,
    string? Summary,
    string? Inference,
    string? PossibleExplanation,
    string? TacticalSignificance,
    string? ConfidenceQualification,
    IReadOnlyList<string> Limitations,
    string? SpokenSummary,
    string? VisualDescription,
    string? SimplifiedExplanation,
    string? ReplayAudioDescription,
    IReadOnlyList<string> EvidenceIds,
    string? ModelDeployment,
    string Notice);
