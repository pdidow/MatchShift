using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Workflows;

namespace MatchShift.Agents.Workflow;

/// <summary>Stable executor IDs. They are the node names in the Agent Framework workflow graph and in the UI topology.</summary>
public static class Stages
{
    public const string Grounding = "deterministic-grounding";
    public const string TacticalAnalyst = "tactical-analyst";
    public const string AudienceNarrative = "audience-narrative";
    public const string AccessibilityNarrator = "accessibility-narrator";
    public const string FanIn = "fan-in";
    public const string DeterministicValidation = "deterministic-validation";
    public const string Verification = "verification-agent";
    public const string FinalValidation = "final-validation";
    public const string Repair = "constrained-repair";
}

/// <summary>One stage execution. Safe to show to judges: summaries and counts, never prompts, raw model text or credentials.</summary>
public sealed record TraceStep
{
    public required int Sequence { get; init; }
    public required string Stage { get; init; }
    public required string AgentName { get; init; }
    public required string Responsibility { get; init; }
    public required ExecutionType ExecutionType { get; init; }
    public required ExecutionMode ExecutionMode { get; init; }
    public required string Provider { get; init; }
    public string? ModelDeployment { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long? DurationMs { get; init; }

    /// <summary>Agent Framework superstep in which the executor ran (from the framework's own event stream).</summary>
    public int? Superstep { get; init; }

    public required StageStatus Status { get; init; }
    public IReadOnlyList<string> HandoffFrom { get; init; } = [];
    public IReadOnlyList<string> HandoffTo { get; init; } = [];
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
    public int EvidenceCount { get; init; }
    public string InputSummary { get; init; } = "";
    public string OutputSummary { get; init; } = "";
    public int ModelCalls { get; init; }
    public int RetryCount { get; init; }
    public IReadOnlyList<string> AttemptFailures { get; init; } = [];
    public string? RecoveryAction { get; init; }
    public string? VerificationDecision { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public IReadOnlyList<string> Findings { get; init; } = [];
}

public sealed record HandoffRecord(string From, IReadOnlyList<string> To, string Message, DateTimeOffset At);

/// <summary>Observed overlap of the two fan-out branches, measured from their actual start/end times.</summary>
public sealed record ConcurrencyRecord(string BranchA, string BranchB, bool SameSuperstep, bool Overlapped, long OverlapMs);

[JsonConverter(typeof(JsonStringEnumConverter<ExecutionStatus>))]
public enum ExecutionStatus
{
    [JsonStringEnumMemberName("RUNNING")] Running,
    [JsonStringEnumMemberName("COMPLETED")] Completed,
    [JsonStringEnumMemberName("CANCELLED")] Cancelled,
    [JsonStringEnumMemberName("FAILED")] Failed,
}

/// <summary>A judge-visible record of one workflow execution, live while it runs.</summary>
public sealed record WorkflowExecution
{
    public required string WorkflowExecutionId { get; init; }
    public required string InsightId { get; init; }
    public required string MatchId { get; init; }
    public required ExecutionMode ExecutionMode { get; init; }
    public required string Provider { get; init; }
    public string? ModelDeployment { get; init; }
    public required string Orchestrator { get; init; }
    public required ExecutionStatus Status { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long? DurationMs { get; init; }
    public required IReadOnlyList<TraceStep> Steps { get; init; }
    public required IReadOnlyList<HandoffRecord> Handoffs { get; init; }
    public ConcurrencyRecord? Concurrency { get; init; }
    public int FrameworkSupersteps { get; init; }
    public int FrameworkExecutorInvocations { get; init; }
    public int ModelCalls { get; init; }
    public int ModelCallLimit { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public WorkflowOutcome? Outcome { get; init; }
    public string? VerificationDecision { get; init; }
    public bool Verified { get; init; }
    public string? AuthoritativeFingerprintBefore { get; init; }
    public string? AuthoritativeFingerprintAfter { get; init; }
    public bool AuthoritativeFactsUnchanged => AuthoritativeFingerprintBefore is not null && AuthoritativeFingerprintBefore == AuthoritativeFingerprintAfter;
    public IReadOnlyList<string> RecoveryActions { get; init; } = [];
    public PublishedInsight? Published { get; init; }
    public string? Error { get; init; }

    /// <summary>For RECORDED_DEMO: when and in which mode the trace was originally produced.</summary>
    public string? RecordedFrom { get; init; }
}

/// <summary>Thread-safe collector for one execution. Executors record stages; the run loop feeds framework events.</summary>
public sealed class WorkflowTraceRecorder
{
    public const string OrchestratorName = "Microsoft Agent Framework Workflows (Microsoft.Agents.AI.Workflows): WorkflowBuilder graph, in-process superstep runner";

    private readonly object _gate = new();
    private readonly List<TraceStep> _steps = [];
    private readonly List<HandoffRecord> _handoffs = [];
    private readonly List<string> _recovery = [];
    private readonly Dictionary<string, List<int>> _invocations = [];
    private readonly TimeProvider _time;
    private int _superstep;
    private int _supersteps;
    private int _executorInvocations;
    private ExecutionStatus _status = ExecutionStatus.Running;
    private DateTimeOffset? _completed;
    private InsightWorkflowOutcome? _outcome;
    private string? _fingerprintBefore, _fingerprintAfter, _error;
    private int _modelCalls, _modelCallLimit;

    public WorkflowTraceRecorder(string workflowExecutionId, string insightId, string matchId, IInsightAgentFactory factory, TimeProvider? time = null)
    {
        WorkflowExecutionId = workflowExecutionId;
        InsightId = insightId;
        MatchId = matchId;
        Mode = factory.Mode;
        Provider = factory.Provider;
        ModelDeployment = factory.ModelDeployment;
        _time = time ?? TimeProvider.System;
        StartedAt = _time.GetUtcNow();
    }

    public string WorkflowExecutionId { get; }
    public string InsightId { get; }
    public string MatchId { get; }
    public ExecutionMode Mode { get; }
    public string Provider { get; }
    public string? ModelDeployment { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset Now => _time.GetUtcNow();

    public int Begin(string stage, AgentDefinition? agent, string fallbackName, string responsibility, string inputSummary,
        IReadOnlyList<string> handoffFrom, int evidenceCount)
    {
        lock (_gate)
        {
            var isAgent = agent is not null && Mode == ExecutionMode.LiveFoundry;
            _steps.Add(new TraceStep
            {
                Sequence = _steps.Count + 1,
                Stage = stage,
                AgentName = agent?.Name ?? fallbackName,
                Responsibility = agent?.Responsibility ?? responsibility,
                ExecutionType = isAgent ? ExecutionType.MicrosoftAgentFramework : ExecutionType.Deterministic,
                ExecutionMode = Mode,
                Provider = isAgent ? Provider : "matchshift-deterministic",
                ModelDeployment = isAgent ? ModelDeployment : null,
                StartedAt = Now,
                Status = StageStatus.Running,
                HandoffFrom = handoffFrom,
                InputSummary = inputSummary,
                EvidenceCount = evidenceCount,
            });
            return _steps.Count - 1;
        }
    }

    /// <summary>Marks a stage as having actually been executed by a model, or as deterministic despite having an agent (e.g. skipped).</summary>
    public void Complete(int index, Func<TraceStep, TraceStep> update)
    {
        lock (_gate)
        {
            var now = Now;
            var step = update(_steps[index]);
            _steps[index] = step with { CompletedAt = now, DurationMs = (long)(now - step.StartedAt).TotalMilliseconds };
            if (step.RecoveryAction is { } r) _recovery.Add($"{step.AgentName}: {r}");
            _modelCalls += step.ModelCalls;
        }
    }

    public void Handoff(string from, IReadOnlyList<string> to, string message)
    {
        lock (_gate) _handoffs.Add(new HandoffRecord(from, to, message, Now));
    }

    public void Recovery(string action)
    {
        lock (_gate) _recovery.Add(action);
    }

    public void Fingerprints(string? before, string? after)
    {
        lock (_gate) { _fingerprintBefore ??= before; if (after is not null) _fingerprintAfter = after; }
    }

    public void ModelCallLimit(int limit)
    {
        lock (_gate) _modelCallLimit = limit;
    }

    /// <summary>Feeds the Agent Framework event stream: supersteps and executor invocations prove the framework ran the graph.</summary>
    public void OnFrameworkEvent(WorkflowEvent evt)
    {
        lock (_gate)
        {
            switch (evt)
            {
                case SuperStepStartedEvent:
                    _superstep++;
                    _supersteps++;
                    break;
                case ExecutorInvokedEvent invoked:
                    _executorInvocations++;
                    if (!_invocations.TryGetValue(invoked.ExecutorId, out var list)) _invocations[invoked.ExecutorId] = list = [];
                    list.Add(_superstep);
                    break;
            }
        }
    }

    public void Finish(InsightWorkflowOutcome? outcome, ExecutionStatus status, string? error = null)
    {
        lock (_gate)
        {
            _outcome = outcome;
            _status = status;
            _error = error;
            _completed = Now;
            for (var i = 0; i < _steps.Count; i++)
                if (_steps[i].Status == StageStatus.Running)
                    _steps[i] = _steps[i] with { Status = status == ExecutionStatus.Cancelled ? StageStatus.Cancelled : StageStatus.Failed, CompletedAt = _completed,
                        DurationMs = (long)(_completed.Value - _steps[i].StartedAt).TotalMilliseconds };
        }
    }

    public WorkflowExecution Snapshot()
    {
        lock (_gate)
        {
            // Assign each step the superstep of its N-th invocation of that executor, as reported by the framework.
            var seen = new Dictionary<string, int>();
            var steps = _steps.Select(s =>
            {
                var n = seen.TryGetValue(s.Stage, out var c) ? c : 0;
                seen[s.Stage] = n + 1;
                return _invocations.TryGetValue(s.Stage, out var list) && n < list.Count ? s with { Superstep = list[n] } : s;
            }).ToList();

            var completed = _completed;
            return new WorkflowExecution
            {
                WorkflowExecutionId = WorkflowExecutionId,
                InsightId = InsightId,
                MatchId = MatchId,
                ExecutionMode = Mode,
                Provider = Provider,
                ModelDeployment = ModelDeployment,
                Orchestrator = OrchestratorName,
                Status = _status,
                StartedAt = StartedAt,
                CompletedAt = completed,
                DurationMs = completed is null ? (long)(Now - StartedAt).TotalMilliseconds : (long)(completed.Value - StartedAt).TotalMilliseconds,
                Steps = steps,
                Handoffs = [.. _handoffs],
                Concurrency = ConcurrencyOf(steps),
                FrameworkSupersteps = _supersteps,
                FrameworkExecutorInvocations = _executorInvocations,
                ModelCalls = _modelCalls,
                ModelCallLimit = _modelCallLimit,
                InputTokens = steps.Sum(s => s.InputTokens),
                OutputTokens = steps.Sum(s => s.OutputTokens),
                Outcome = _outcome?.Outcome,
                VerificationDecision = _outcome?.VerificationDecision,
                Verified = _outcome?.Published.Verified ?? false,
                AuthoritativeFingerprintBefore = _fingerprintBefore,
                AuthoritativeFingerprintAfter = _fingerprintAfter,
                RecoveryActions = [.. _recovery],
                Published = _outcome?.Published,
                Error = _error,
            };
        }
    }

    private static ConcurrencyRecord? ConcurrencyOf(IReadOnlyList<TraceStep> steps)
    {
        var a = steps.FirstOrDefault(s => s.Stage == Stages.AudienceNarrative);
        var b = steps.FirstOrDefault(s => s.Stage == Stages.AccessibilityNarrator);
        if (a?.CompletedAt is not { } aEnd || b?.CompletedAt is not { } bEnd) return null;
        var overlap = (Min(aEnd, bEnd) - Max(a.StartedAt, b.StartedAt)).TotalMilliseconds;
        return new ConcurrencyRecord(a.Stage, b.Stage, a.Superstep is not null && a.Superstep == b.Superstep, overlap > 0, (long)Math.Max(0, overlap));

        static DateTimeOffset Min(DateTimeOffset x, DateTimeOffset y) => x < y ? x : y;
        static DateTimeOffset Max(DateTimeOffset x, DateTimeOffset y) => x > y ? x : y;
    }
}
