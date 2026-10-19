using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace MatchShift.Agents.Workflow;

// ---------- Messages exchanged along the workflow edges (the handoffs). ----------

public sealed record GroundedInsight(string InsightId, string Fingerprint, int EvidenceCount);

/// <summary>Tactical Analyst → (fan-out) Audience Narrative and Accessibility Narrator.</summary>
public sealed record TacticalHandoff(TacticalAnalystResult Result, bool UpstreamFallback);

/// <summary>Either branch → fan-in barrier.</summary>
public sealed record BranchOutput(AgentRole Role, AudienceNarrativeResult? Audience, AccessibilityNarrativeResult? Accessibility, StageStatus Status,
    bool UpstreamFallback = false);

/// <summary>Fan-in (or constrained repair) → deterministic validation.</summary>
public sealed record NarrativeBundle(
    TacticalAnalystResult Tactical, AudienceNarrativeResult Audience, AccessibilityNarrativeResult? Accessibility,
    bool UpstreamFallback, int RepairAttempt, bool RepairFailed = false);

public sealed record ValidatedCandidate(ValidationReport Report, NarrativeBundle Bundle);

public sealed record VerifiedCandidate(ValidatedCandidate Validated, VerificationAgentResult? Verification, bool VerifierRan);

public sealed record RepairRequest(ValidatedCandidate Validated, IReadOnlyList<string> Problems);

/// <summary>The workflow's output.</summary>
public sealed record InsightWorkflowOutcome(WorkflowOutcome Outcome, PublishedInsight Published, string? VerificationDecision);

/// <summary>Keys for Agent Framework shared workflow state.</summary>
public static class StateKeys
{
    public const string Scope = "matchshift";

    /// <summary>The authoritative <see cref="InsightContext"/>, written once by the grounding executor.</summary>
    public const string Context = "insight-context";

    /// <summary>The Tactical Analyst's interpretation object (separate from, and never merged into, the context).</summary>
    public const string Tactical = "tactical-result";
}

/// <summary>Dependencies shared by the executors of one execution.</summary>
public sealed class WorkflowServices(IInsightAgentFactory factory, IReadOnlyDictionary<AgentRole, AIAgent?> agents, AgentInvoker invoker,
    WorkflowTraceRecorder recorder, WorkflowLimits limits)
{
    public IInsightAgentFactory Factory => factory;
    public AgentInvoker Invoker => invoker;
    public WorkflowTraceRecorder Recorder => recorder;
    public WorkflowLimits Limits => limits;
    public AIAgent? Agent(AgentRole role) => agents.TryGetValue(role, out var a) ? a : null;

    public static async ValueTask<InsightContext> ContextAsync(IWorkflowContext ctx, CancellationToken ct) =>
        await ctx.ReadStateAsync<InsightContext>(StateKeys.Context, StateKeys.Scope, ct)
        ?? throw new InvalidOperationException("InsightContext missing from workflow state.");

    public static string Payload(object value) => JsonSerializer.Serialize(value, AgentJson.Options);

    public static TraceStep Deterministic(TraceStep s) => s with { ExecutionType = ExecutionType.Deterministic, Provider = "matchshift-deterministic", ModelDeployment = null };

    public static TraceStep FromCall<T>(TraceStep s, AgentCallOutcome<T> call) where T : class => s with
    {
        ModelCalls = call.Attempts, RetryCount = call.Retries, AttemptFailures = call.AttemptFailures,
        InputTokens = call.InputTokens, OutputTokens = call.OutputTokens,
    };
}

/// <summary>DETERMINISTIC. Checks the context's integrity, fingerprints it and writes it into shared workflow state.</summary>
public sealed class GroundingExecutor(WorkflowServices s) : Executor<InsightContext, GroundedInsight>(Stages.Grounding)
{
    public override async ValueTask<GroundedInsight> HandleAsync(InsightContext context, IWorkflowContext ctx, CancellationToken ct)
    {
        var step = s.Recorder.Begin(Stages.Grounding, null, "Deterministic Match Analytics & Grounding",
            "MatchShift's deterministic engine detected this insight from raw synthetic events; this stage validates and freezes those facts as shared state.",
            $"{context.InsightType} at {context.MatchTime}: {context.DeterministicMetrics.Length} metrics, {context.SupportingEventIds.Length} supporting events", [], context.SupportingEventIds.Length);

        var problems = GroundingValidator.CheckEvidenceIds(context, "Grounding", "supportingEventIds", context.SupportingEventIds);
        if (problems.Count > 0) throw new InvalidDataException($"Insight evidence failed grounding ({problems[0].Code}).");

        var fingerprint = context.Fingerprint();
        s.Recorder.Fingerprints(fingerprint, null);
        await ctx.QueueStateUpdateAsync(StateKeys.Context, context, StateKeys.Scope, ct);
        s.Recorder.Complete(step, x => x with
        {
            Status = StageStatus.Completed,
            HandoffTo = [Stages.TacticalAnalyst],
            EvidenceIds = context.SupportingEventIds,
            OutputSummary = $"InsightContext frozen (fingerprint {fingerprint}); score {context.Score} at {context.MatchTime}; confidence {context.ConfidenceInputs.Level} " +
                            $"({context.ConfidenceInputs.Score:0.00}); samples {(context.ConfidenceInputs.AllSampleRequirementsMet ? "met" : "NOT all met")}",
        });
        s.Recorder.Handoff(Stages.Grounding, [Stages.TacticalAnalyst], nameof(GroundedInsight));
        return new GroundedInsight(context.InsightId, fingerprint, context.SupportingEventIds.Length);
    }
}

/// <summary>AGENT 1. Interprets the evidence. On failure the deterministic insight is preserved and downstream agents are not called.</summary>
public sealed class TacticalAnalystExecutor(WorkflowServices s) : Executor<GroundedInsight, TacticalHandoff>(Stages.TacticalAnalyst)
{
    public override async ValueTask<TacticalHandoff> HandleAsync(GroundedInsight message, IWorkflowContext ctx, CancellationToken ct)
    {
        var context = await WorkflowServices.ContextAsync(ctx, ct);
        var def = MatchShiftAgents.TacticalAnalyst;
        var agent = s.Agent(AgentRole.TacticalAnalyst);
        var step = s.Recorder.Begin(Stages.TacticalAnalyst, def, def.Name, def.Responsibility,
            $"InsightContext (read from shared state): {context.DeterministicMetrics.Length} metrics, {context.SourceEvents.Length} source events", [Stages.Grounding], context.SourceEvents.Length);

        TacticalAnalystResult result;
        var fallback = false;
        if (agent is null)
        {
            result = TemplateOutputs.Tactical(context);
            s.Recorder.Complete(step, x => WorkflowServices.Deterministic(x) with { Status = StageStatus.Template, OutputSummary = "OFFLINE_TEMPLATE: deterministic inference; no model call", EvidenceIds = result.EvidenceIds });
        }
        else
        {
            var call = await s.Invoker.RunAsync<TacticalAnalystResult>(agent, WorkflowServices.Payload(new { insightContext = context }),
                TemplateOutputs.Normalize, r => !string.IsNullOrWhiteSpace(r.Inference), ct);
            if (call.Result is { } r)
            {
                result = r;
                s.Recorder.Complete(step, x => WorkflowServices.FromCall(x, call) with
                {
                    Status = StageStatus.Completed, EvidenceIds = r.EvidenceIds, EvidenceCount = r.EvidenceIds.Count,
                    OutputSummary = $"{r.FactsUsed.Count} facts used, inference, {(r.PossibleExplanation is null ? "no" : "a")} possible explanation, {r.Limitations.Count} limitation(s), {r.EvidenceIds.Count} evidence IDs",
                    RecoveryAction = call.Retries > 0 ? $"recovered after retry ({string.Join(", ", call.AttemptFailures)})" : null,
                });
            }
            else
            {
                result = TemplateOutputs.Tactical(context);
                fallback = true;
                s.Recorder.Complete(step, x => WorkflowServices.FromCall(x, call) with
                {
                    Status = StageStatus.Fallback,
                    OutputSummary = $"Failed: {call.Failure} after {call.Attempts} attempt(s)",
                    RecoveryAction = "deterministic insight preserved; downstream agents and verifier not called (no unverified AI text can be published)",
                });
            }
        }
        await ctx.QueueStateUpdateAsync(StateKeys.Tactical, result, StateKeys.Scope, ct);
        s.Recorder.Handoff(Stages.TacticalAnalyst, [Stages.AudienceNarrative, Stages.AccessibilityNarrator], $"{nameof(TacticalHandoff)} (fan-out)");
        return new TacticalHandoff(result, fallback);
    }
}

/// <summary>AGENT 2. Same grounded insight, communicated for the selected audience mode. Runs concurrently with AGENT 3.</summary>
public sealed class AudienceNarrativeExecutor(WorkflowServices s) : Executor<TacticalHandoff, BranchOutput>(Stages.AudienceNarrative)
{
    public override async ValueTask<BranchOutput> HandleAsync(TacticalHandoff handoff, IWorkflowContext ctx, CancellationToken ct)
    {
        var context = await WorkflowServices.ContextAsync(ctx, ct);
        var def = MatchShiftAgents.AudienceNarrative;
        var agent = s.Agent(AgentRole.AudienceNarrative);
        var step = s.Recorder.Begin(Stages.AudienceNarrative, def, def.Name, def.Responsibility,
            $"InsightContext + TacticalAnalystResult; mode {context.SelectedAudienceMode}, language {context.Language}", [Stages.TacticalAnalyst], handoff.Result.EvidenceIds.Count);
        try
        {
            if (agent is null || handoff.UpstreamFallback)
            {
                var template = TemplateOutputs.Audience(context);
                s.Recorder.Complete(step, x => WorkflowServices.Deterministic(x) with
                {
                    Status = agent is null ? StageStatus.Template : StageStatus.Skipped,
                    OutputSummary = agent is null ? "OFFLINE_TEMPLATE: lexicon wording for this mode; no model call" : "Skipped: Tactical Analyst unavailable; lexicon wording kept",
                });
                return new BranchOutput(AgentRole.AudienceNarrative, template, null, agent is null ? StageStatus.Template : StageStatus.Skipped, handoff.UpstreamFallback);
            }
            var call = await s.Invoker.RunAsync<AudienceNarrativeResult>(agent,
                WorkflowServices.Payload(new { insightContext = context, tacticalAnalystResult = handoff.Result }),
                TemplateOutputs.Normalize, r => !string.IsNullOrWhiteSpace(r.Headline) && !string.IsNullOrWhiteSpace(r.Summary), ct);
            if (call.Result is { } r)
            {
                s.Recorder.Complete(step, x => WorkflowServices.FromCall(x, call) with
                {
                    Status = StageStatus.Completed, OutputSummary = $"{context.SelectedAudienceMode} narrative: headline {r.Headline.Length} chars, summary {r.Summary.Length} chars",
                    RecoveryAction = call.Retries > 0 ? $"recovered after retry ({string.Join(", ", call.AttemptFailures)})" : null,
                });
                return new BranchOutput(AgentRole.AudienceNarrative, r, null, StageStatus.Completed);
            }
            s.Recorder.Complete(step, x => WorkflowServices.FromCall(x, call) with
            {
                Status = StageStatus.Fallback, OutputSummary = $"Failed: {call.Failure} after {call.Attempts} attempt(s)",
                RecoveryAction = "safe deterministic audience wording used",
            });
            return new BranchOutput(AgentRole.AudienceNarrative, TemplateOutputs.Audience(context), null, StageStatus.Fallback);
        }
        finally
        {
            s.Recorder.Handoff(Stages.AudienceNarrative, [Stages.FanIn], nameof(BranchOutput));
        }
    }
}

/// <summary>AGENT 3. Same grounded intelligence for spoken, nonvisual and simplified use. Skipped (no model call) when not required.</summary>
public sealed class AccessibilityNarratorExecutor(WorkflowServices s) : Executor<TacticalHandoff, BranchOutput>(Stages.AccessibilityNarrator)
{
    public override async ValueTask<BranchOutput> HandleAsync(TacticalHandoff handoff, IWorkflowContext ctx, CancellationToken ct)
    {
        var context = await WorkflowServices.ContextAsync(ctx, ct);
        var def = MatchShiftAgents.AccessibilityNarrator;
        var agent = s.Agent(AgentRole.AccessibilityNarrator);
        var step = s.Recorder.Begin(Stages.AccessibilityNarrator, def, def.Name, def.Responsibility,
            $"InsightContext + TacticalAnalystResult; accessibility required: {context.AccessibilityRequirements.Required}", [Stages.TacticalAnalyst], context.SourceEvents.Length);
        try
        {
            if (!context.AccessibilityRequirements.Required)
            {
                s.Recorder.Complete(step, x => WorkflowServices.Deterministic(x) with
                {
                    Status = StageStatus.Skipped, OutputSummary = "Branch skipped: accessible output not requested for this execution (no model call)",
                });
                return new BranchOutput(AgentRole.AccessibilityNarrator, null, null, StageStatus.Skipped, handoff.UpstreamFallback);
            }
            if (agent is null || handoff.UpstreamFallback)
            {
                var template = TemplateOutputs.Accessibility(context);
                s.Recorder.Complete(step, x => WorkflowServices.Deterministic(x) with
                {
                    Status = agent is null ? StageStatus.Template : StageStatus.Skipped,
                    OutputSummary = agent is null ? "OFFLINE_TEMPLATE: spoken lexicon wording; client-side deterministic visual description" : "Skipped: Tactical Analyst unavailable",
                });
                return new BranchOutput(AgentRole.AccessibilityNarrator, null, template, agent is null ? StageStatus.Template : StageStatus.Skipped, handoff.UpstreamFallback);
            }
            var call = await s.Invoker.RunAsync<AccessibilityNarrativeResult>(agent,
                WorkflowServices.Payload(new { insightContext = context, tacticalAnalystResult = handoff.Result }),
                TemplateOutputs.Normalize, r => !string.IsNullOrWhiteSpace(r.SpokenSummary) && !string.IsNullOrWhiteSpace(r.SimplifiedExplanation), ct);
            if (call.Result is { } r)
            {
                s.Recorder.Complete(step, x => WorkflowServices.FromCall(x, call) with
                {
                    Status = StageStatus.Completed,
                    OutputSummary = $"spoken summary, simplified explanation{(r.VisualDescription is null ? "" : ", visual description")}{(r.ReplayAudioDescription is null ? "" : ", replay audio description")}",
                    RecoveryAction = call.Retries > 0 ? $"recovered after retry ({string.Join(", ", call.AttemptFailures)})" : null,
                });
                return new BranchOutput(AgentRole.AccessibilityNarrator, null, r, StageStatus.Completed);
            }
            s.Recorder.Complete(step, x => WorkflowServices.FromCall(x, call) with
            {
                Status = StageStatus.Degraded, OutputSummary = $"Failed: {call.Failure} after {call.Attempts} attempt(s)",
                RecoveryAction = "accessibility branch degraded; visual insight continues; client-side deterministic descriptions remain available",
            });
            return new BranchOutput(AgentRole.AccessibilityNarrator, null, null, StageStatus.Degraded);
        }
        finally
        {
            s.Recorder.Handoff(Stages.AccessibilityNarrator, [Stages.FanIn], nameof(BranchOutput));
        }
    }
}

/// <summary>DETERMINISTIC. Fan-in barrier target: joins both branch outputs into one candidate.</summary>
[SendsMessage(typeof(NarrativeBundle))]
public sealed class FanInExecutor(WorkflowServices s) : Executor<BranchOutput>(Stages.FanIn)
{
    private readonly List<BranchOutput> _received = [];
    private int _step = -1;

    public override async ValueTask HandleAsync(BranchOutput output, IWorkflowContext ctx, CancellationToken ct)
    {
        if (_step < 0)
            _step = s.Recorder.Begin(Stages.FanIn, null, "Fan-in Barrier", "Waits for both concurrent branches, then joins them into one candidate.",
                "BranchOutput x2", [Stages.AudienceNarrative, Stages.AccessibilityNarrator], 0);
        _received.Add(output);
        if (_received.Count < 2) return;

        var tactical = await ctx.ReadStateAsync<TacticalAnalystResult>(StateKeys.Tactical, StateKeys.Scope, ct)
            ?? throw new InvalidOperationException("Tactical result missing from workflow state.");
        var audience = _received.First(r => r.Role == AgentRole.AudienceNarrative);
        var accessibility = _received.First(r => r.Role == AgentRole.AccessibilityNarrator);
        var upstreamFallback = _received.Any(r => r.UpstreamFallback);
        s.Recorder.Complete(_step, x => x with
        {
            Status = StageStatus.Completed, HandoffTo = [Stages.DeterministicValidation],
            OutputSummary = $"joined: audience {audience.Status}, accessibility {accessibility.Status}",
        });
        s.Recorder.Handoff(Stages.FanIn, [Stages.DeterministicValidation], nameof(NarrativeBundle));
        await ctx.SendMessageAsync(new NarrativeBundle(tactical, audience.Audience!, accessibility.Accessibility, upstreamFallback, 0), ct);
    }
}

/// <summary>DETERMINISTIC. Grounding checks on every generated field; prunes failing optional claims.</summary>
public sealed class DeterministicValidationExecutor(WorkflowServices s) : Executor<NarrativeBundle, ValidatedCandidate>(Stages.DeterministicValidation)
{
    public override async ValueTask<ValidatedCandidate> HandleAsync(NarrativeBundle bundle, IWorkflowContext ctx, CancellationToken ct)
    {
        var context = await WorkflowServices.ContextAsync(ctx, ct);
        var step = s.Recorder.Begin(Stages.DeterministicValidation, null, "Deterministic Validation",
            "Code checks evidence IDs, cut-off, every number, score, minutes, players, intent/certainty/causal wording before the verifier.",
            bundle.RepairAttempt > 0 ? "repaired candidate" : "joined candidate", [bundle.RepairAttempt > 0 ? Stages.Repair : Stages.FanIn], bundle.Tactical.EvidenceIds.Count);
        var report = CandidateSanitizer.Validate(context, new NarrativeCandidate(bundle.Tactical, bundle.Audience, bundle.Accessibility));
        s.Recorder.Complete(step, x => x with
        {
            Status = StageStatus.Completed, HandoffTo = [Stages.Verification],
            EvidenceIds = report.Candidate.Tactical.EvidenceIds, EvidenceCount = report.Candidate.Tactical.EvidenceIds.Count,
            OutputSummary = report.Clean ? "all checks passed" : $"{report.Issues.Count} finding(s); removed: {(report.Removed.Count == 0 ? "nothing" : string.Join("; ", report.Removed))}; audience text {(report.AudienceValid ? "valid" : "needs repair")}",
            Findings = [.. report.Issues.Take(12).Select(i => $"{i.Code} {i.Agent}.{i.Field}: {i.Message}")],
            RecoveryAction = report.Removed.Count > 0 ? "unsupported optional claims removed deterministically" : null,
        });
        s.Recorder.Handoff(Stages.DeterministicValidation, [Stages.Verification], nameof(ValidatedCandidate));
        return new ValidatedCandidate(report, bundle);
    }
}

/// <summary>AGENT 4. Independent semantic verification gate.</summary>
public sealed class VerificationExecutor(WorkflowServices s) : Executor<ValidatedCandidate, VerifiedCandidate>(Stages.Verification)
{
    public override async ValueTask<VerifiedCandidate> HandleAsync(ValidatedCandidate validated, IWorkflowContext ctx, CancellationToken ct)
    {
        var context = await WorkflowServices.ContextAsync(ctx, ct);
        var def = MatchShiftAgents.Verifier;
        var agent = s.Agent(AgentRole.Verification);
        var candidate = validated.Report.Candidate;
        var step = s.Recorder.Begin(Stages.Verification, def, def.Name, def.Responsibility,
            $"InsightContext + {validated.Report.Issues.Count} deterministic finding(s) + tactical, audience{(candidate.Accessibility is null ? "" : ", accessibility")} outputs",
            [Stages.DeterministicValidation], candidate.Tactical.EvidenceIds.Count);
        try
        {
            if (agent is null || validated.Bundle.UpstreamFallback || validated.Bundle.RepairFailed)
            {
                var why = agent is null ? "OFFLINE_TEMPLATE: no model verifier; deterministic checks only"
                    : validated.Bundle.UpstreamFallback ? "Skipped: no AI output to verify (Tactical Analyst unavailable)" : "Skipped: constrained repair failed";
                s.Recorder.Complete(step, x => WorkflowServices.Deterministic(x) with { Status = StageStatus.Skipped, OutputSummary = why });
                return new VerifiedCandidate(validated, null, false);
            }
            var call = await s.Invoker.RunAsync<VerificationAgentResult>(agent, WorkflowServices.Payload(new
            {
                insightContext = context,
                deterministicFindings = validated.Report.Issues.Select(i => $"{i.Code} {i.Agent}.{i.Field}: {i.Message}"),
                removedByCode = validated.Report.Removed,
                tacticalAnalystResult = candidate.Tactical,
                audienceNarrativeResult = candidate.Audience,
                accessibilityNarrativeResult = candidate.Accessibility,
            }), TemplateOutputs.Normalize, r => VerifierDecisions.All.Contains(r.Decision), ct);
            if (call.Result is { } v)
            {
                s.Recorder.Complete(step, x => WorkflowServices.FromCall(x, call) with
                {
                    Status = StageStatus.Completed, VerificationDecision = v.Decision,
                    EvidenceIds = v.EvidenceIdsChecked, EvidenceCount = v.EvidenceIdsChecked.Count,
                    OutputSummary = $"{v.Decision}: {v.UnsupportedClaims.Count} unsupported claim(s), {v.Corrections.Count} correction(s), {v.EvidenceIdsChecked.Count} evidence IDs checked",
                    Findings = [.. v.UnsupportedClaims.Take(6).Select(c => $"unsupported: {Trim(c)}"), .. v.Reasons.Take(4).Select(r => $"reason: {Trim(r)}")],
                    RecoveryAction = call.Retries > 0 ? $"recovered after retry ({string.Join(", ", call.AttemptFailures)})" : null,
                });
                return new VerifiedCandidate(validated, v, true);
            }
            s.Recorder.Complete(step, x => WorkflowServices.FromCall(x, call) with
            {
                Status = StageStatus.Failed, OutputSummary = $"Failed: {call.Failure} after {call.Attempts} attempt(s)",
                RecoveryAction = "AI output NOT labelled verified; safe deterministic wording kept",
            });
            return new VerifiedCandidate(validated, null, false);
        }
        finally
        {
            s.Recorder.Handoff(Stages.Verification, [Stages.FinalValidation], nameof(VerifiedCandidate));
        }
    }

    private static string Trim(string s) => s.Length <= 160 ? s : s[..157] + "...";
}

/// <summary>DETERMINISTIC. Decides the outcome. Nothing reaches a viewer as verified unless the verifier approved it AND code re-validated it.</summary>
[SendsMessage(typeof(RepairRequest))]
[YieldsOutput(typeof(InsightWorkflowOutcome))]
public sealed class FinalValidationExecutor(WorkflowServices s) : Executor<VerifiedCandidate>(Stages.FinalValidation)
{
    public override async ValueTask HandleAsync(VerifiedCandidate verified, IWorkflowContext ctx, CancellationToken ct)
    {
        var context = await WorkflowServices.ContextAsync(ctx, ct);
        var report = verified.Validated.Report;
        var bundle = verified.Validated.Bundle;
        var v = verified.Verification;
        var step = s.Recorder.Begin(Stages.FinalValidation, null, "Final Deterministic Validation",
            "Re-checks the verifier's decision and any corrections in code, then publishes, repairs once, or falls back.",
            v is null ? "no verifier decision" : $"verifier {v.Decision}", [Stages.Verification], report.Candidate.Tactical.EvidenceIds.Count);

        var findings = new List<string>();
        if (v is not null)
            findings.AddRange(GroundingValidator.CheckEvidenceIds(context, "Verification Agent", "evidenceIdsChecked", v.EvidenceIdsChecked)
                .Select(i => $"{i.Code}: {i.Message} (ignored)"));

        WorkflowOutcome outcome;
        PublishedInsight published;
        string summary;
        if (s.Factory.Mode == ExecutionMode.OfflineTemplate)
        {
            outcome = WorkflowOutcome.DeterministicOnly;
            published = Unverified(context, outcome, "OFFLINE_TEMPLATE: no model was called. The deterministic insight is shown; nothing is AI-verified.");
            summary = "DETERMINISTIC_ONLY";
        }
        else if (bundle.UpstreamFallback)
        {
            outcome = WorkflowOutcome.FallbackUnverified;
            published = Unverified(context, outcome, "The Tactical Analyst was unavailable. The deterministic insight is shown; no AI text was published.");
            summary = "FALLBACK_UNVERIFIED (tactical analyst unavailable)";
        }
        else if (v is null && !bundle.RepairFailed)
        {
            outcome = WorkflowOutcome.FallbackUnverified;
            published = Unverified(context, outcome, "The Verification Agent was unavailable, so AI text is not labelled verified. The deterministic insight is shown.");
            summary = "FALLBACK_UNVERIFIED (verifier unavailable)";
        }
        else
        {
            var corrections = v?.CorrectedNarrative;
            var correctionIssues = corrections is null ? [] : GroundingValidator.CheckCorrection(context, corrections);
            findings.AddRange(correctionIssues.Select(i => $"{i.Code} {i.Field}: {i.Message}"));
            if (v?.Decision == VerifierDecisions.Approved && report.AudienceValid)
            {
                outcome = WorkflowOutcome.Approved;
                published = Publish(context, report.Candidate, null);
                summary = "APPROVED and re-validated";
            }
            else if (v?.Decision == VerifierDecisions.ApprovedWithChanges && corrections is not null && correctionIssues.Count == 0)
            {
                outcome = WorkflowOutcome.ApprovedWithChanges;
                published = Publish(context, report.Candidate, corrections);
                summary = "APPROVED_WITH_CHANGES; corrections passed deterministic validation";
            }
            else
            {
                var why = v is null ? "constrained repair failed"
                    : v.Decision == VerifierDecisions.Approved ? "verifier approved text that failed deterministic validation (overridden)"
                    : v.Decision == VerifierDecisions.ApprovedWithChanges ? "verifier corrections missing or failed deterministic validation"
                    : "verifier rejected the candidate";
                if (s.Limits.AllowRepair && bundle.RepairAttempt == 0 && !bundle.RepairFailed)
                {
                    var problems = report.Issues.Where(i => i.Agent == "Audience Narrative").Select(i => i.Message)
                        .Concat(v?.UnsupportedClaims ?? []).Concat(v?.Corrections ?? []).Distinct().Take(8).ToList();
                    s.Recorder.Complete(step, x => x with
                    {
                        Status = StageStatus.Completed, HandoffTo = [Stages.Repair], VerificationDecision = v?.Decision, Findings = findings,
                        OutputSummary = $"Not publishable ({why}); one constrained repair requested", RecoveryAction = "one constrained repair",
                    });
                    s.Recorder.Handoff(Stages.FinalValidation, [Stages.Repair], nameof(RepairRequest));
                    await ctx.SendMessageAsync(new RepairRequest(verified.Validated, problems.Count > 0 ? problems : [why]), ct);
                    return;
                }
                outcome = WorkflowOutcome.Rejected;
                published = Unverified(context, outcome, $"AI output rejected ({why}). The deterministic insight is shown; no AI text was published.");
                summary = $"REJECTED ({why})";
            }
        }

        var after = (await WorkflowServices.ContextAsync(ctx, ct)).Fingerprint();
        s.Recorder.Fingerprints(null, after);
        s.Recorder.Complete(step, x => x with
        {
            Status = StageStatus.Completed, VerificationDecision = v?.Decision, Findings = findings,
            EvidenceIds = published.EvidenceIds, EvidenceCount = published.EvidenceIds.Count,
            OutputSummary = $"{summary}; published verified={published.Verified}; authoritative facts {(after == context.Fingerprint() ? "unchanged" : "CHANGED")}",
            RecoveryAction = published.Verified ? null : "deterministic insight retained",
        });
        await ctx.YieldOutputAsync(new InsightWorkflowOutcome(outcome, published, v?.Decision), ct);
    }

    private PublishedInsight Publish(InsightContext c, NarrativeCandidate candidate, CorrectedNarrative? corrected)
    {
        var t = candidate.Tactical;
        var a = candidate.Audience;
        var acc = candidate.Accessibility;
        return new PublishedInsight(c.WorkflowExecutionId, c.InsightId, s.Factory.Mode,
            corrected is null ? WorkflowOutcome.Approved : WorkflowOutcome.ApprovedWithChanges, Verified: true, s.Factory.Provider,
            corrected?.Headline ?? a.Headline, corrected?.Summary ?? a.Summary, corrected?.Inference ?? t.Inference,
            t.PossibleExplanation, t.TacticalSignificance, string.IsNullOrWhiteSpace(t.ConfidenceQualification) ? null : t.ConfidenceQualification,
            t.Limitations, corrected?.SpokenSummary ?? acc?.SpokenSummary, acc?.VisualDescription, acc?.SimplifiedExplanation, acc?.ReplayAudioDescription,
            t.EvidenceIds, s.Factory.ModelDeployment,
            corrected is null ? "Verified by the MatchShift Verification Agent and deterministic validation." : "Verified with corrections by the MatchShift Verification Agent; corrections re-validated in code.");
    }

    private PublishedInsight Unverified(InsightContext c, WorkflowOutcome outcome, string notice) =>
        new(c.WorkflowExecutionId, c.InsightId, s.Factory.Mode, outcome, Verified: false, s.Factory.Mode == ExecutionMode.OfflineTemplate ? s.Factory.Provider : "matchshift-deterministic",
            null, null, null, null, null, null, [], null, null, null, null, [.. c.SourceEvents.Select(e => e.Id)], null, notice);
}

/// <summary>AGENT 2 again, in constrained-repair mode: rewrites only the audience text to remove the listed problems.</summary>
public sealed class RepairExecutor(WorkflowServices s) : Executor<RepairRequest, NarrativeBundle>(Stages.Repair)
{
    public override async ValueTask<NarrativeBundle> HandleAsync(RepairRequest request, IWorkflowContext ctx, CancellationToken ct)
    {
        var context = await WorkflowServices.ContextAsync(ctx, ct);
        var def = MatchShiftAgents.AudienceNarrative;
        var agent = s.Agent(AgentRole.AudienceNarrative);
        var candidate = request.Validated.Report.Candidate;
        var step = s.Recorder.Begin(Stages.Repair, def, $"{def.Name} (constrained repair)", "Rewrites the audience text once to remove only the problems found; may not add claims.",
            $"{request.Problems.Count} problem(s) to remove", [Stages.FinalValidation], candidate.Tactical.EvidenceIds.Count);
        var bundle = request.Validated.Bundle with { Tactical = candidate.Tactical, Accessibility = candidate.Accessibility, RepairAttempt = 1 };
        try
        {
            if (agent is null)
            {
                s.Recorder.Complete(step, x => WorkflowServices.Deterministic(x) with { Status = StageStatus.Skipped, OutputSummary = "No agent available" });
                return bundle with { RepairFailed = true };
            }
            var call = await s.Invoker.RunAsync<AudienceNarrativeResult>(agent, WorkflowServices.Payload(new
            {
                task = "CONSTRAINED REPAIR: rewrite headline and summary so that every listed problem is removed. Only remove or soften claims; add nothing new.",
                problems = request.Problems,
                insightContext = context,
                tacticalAnalystResult = candidate.Tactical,
                rejectedAudienceNarrative = candidate.Audience,
            }), TemplateOutputs.Normalize, r => !string.IsNullOrWhiteSpace(r.Headline) && !string.IsNullOrWhiteSpace(r.Summary), ct);
            if (call.Result is { } r)
            {
                s.Recorder.Complete(step, x => WorkflowServices.FromCall(x, call) with { Status = StageStatus.Completed, OutputSummary = "repaired audience text; re-entering deterministic validation" });
                return bundle with { Audience = r };
            }
            s.Recorder.Complete(step, x => WorkflowServices.FromCall(x, call) with
            {
                Status = StageStatus.Failed, OutputSummary = $"Failed: {call.Failure}", RecoveryAction = "repair unavailable; candidate will be rejected",
            });
            return bundle with { RepairFailed = true };
        }
        finally
        {
            s.Recorder.Handoff(Stages.Repair, [Stages.DeterministicValidation], nameof(NarrativeBundle));
        }
    }
}
