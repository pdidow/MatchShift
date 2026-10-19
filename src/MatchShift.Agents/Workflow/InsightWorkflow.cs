using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace MatchShift.Agents.Workflow;

/// <summary>
/// The MatchShift insight workflow, built with Microsoft Agent Framework's <see cref="WorkflowBuilder"/>:
///
///   deterministic-grounding ─▶ tactical-analyst ─┬─▶ audience-narrative ─────┐  (fan-out edge: concurrent superstep)
///                                                └─▶ accessibility-narrator ─┴─▶ fan-in  (fan-in barrier edge)
///   fan-in ─▶ deterministic-validation ─▶ verification-agent ─▶ final-validation ─▶ output
///                         ▲                                            │ (REJECTED, once)
///                         └────────────── constrained-repair ◀────────┘
///
/// The authoritative <see cref="InsightContext"/> lives in Agent Framework shared workflow state (written once,
/// read by every agent). Agents return separate typed interpretation objects along the edges.
/// </summary>
public sealed class InsightWorkflowRunner
{
    private readonly IInsightAgentFactory _factory;
    private readonly WorkflowLimits _limits;
    private readonly TimeProvider _time;
    private readonly Lazy<IReadOnlyDictionary<AgentRole, AIAgent?>> _agents;

    public InsightWorkflowRunner(IInsightAgentFactory factory, WorkflowLimits? limits = null, TimeProvider? time = null)
    {
        _factory = factory;
        _limits = limits ?? WorkflowLimits.Default;
        _time = time ?? TimeProvider.System;
        // Agents are stateless between runs (no session is kept), so one instance per role is reused.
        _agents = new(() => MatchShiftAgents.All.ToDictionary(d => d.Role, d => factory.Create(d)));
    }

    public IInsightAgentFactory Factory => _factory;
    public WorkflowLimits Limits => _limits;

    public WorkflowTraceRecorder CreateRecorder(InsightContext context) =>
        new(context.WorkflowExecutionId, context.InsightId, context.MatchId, _factory, _time);

    /// <summary>Builds the graph for one execution. Executors are per-execution, so the fan-in barrier state is never shared.</summary>
    public Microsoft.Agents.AI.Workflows.Workflow Build(WorkflowServices services)
    {
        var grounding = new GroundingExecutor(services);
        var tactical = new TacticalAnalystExecutor(services);
        var audience = new AudienceNarrativeExecutor(services);
        var accessibility = new AccessibilityNarratorExecutor(services);
        var fanIn = new FanInExecutor(services);
        var validation = new DeterministicValidationExecutor(services);
        var verifier = new VerificationExecutor(services);
        var final = new FinalValidationExecutor(services);
        var repair = new RepairExecutor(services);

        return new WorkflowBuilder(grounding)
            .WithName("matchshift-insight-workflow")
            .WithDescription("Deterministic grounding, tactical analyst, concurrent audience and accessibility narrators, deterministic validation, verification agent, final validation.")
            .AddEdge(grounding, tactical)
            .AddFanOutEdge(tactical, [audience, accessibility])
            .AddFanInBarrierEdge([audience, accessibility], fanIn)
            .AddEdge(fanIn, validation)
            .AddEdge(validation, verifier)
            .AddEdge(verifier, final)
            .AddEdge(final, repair)
            .AddEdge(repair, validation)
            .WithOutputFrom(final)
            .Build();
    }

    /// <summary>Runs the workflow to completion, feeding Agent Framework events into the recorder (which callers may poll meanwhile).</summary>
    public async Task<WorkflowExecution> RunAsync(InsightContext context, WorkflowTraceRecorder recorder, CancellationToken cancellationToken)
    {
        var budget = new ModelCallBudget(_limits.MaxModelCalls);
        recorder.ModelCallLimit(_limits.MaxModelCalls);
        var services = new WorkflowServices(_factory, _agents.Value, new AgentInvoker(_factory, _limits, budget, _time), recorder, _limits);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_limits.WorkflowTimeout);

        InsightWorkflowOutcome? outcome = null;
        try
        {
            var workflow = Build(services);
            await using var run = await InProcessExecution.RunStreamingAsync(workflow, context, context.WorkflowExecutionId, deadline.Token);
            await foreach (var evt in run.WatchStreamAsync(deadline.Token))
            {
                recorder.OnFrameworkEvent(evt);
                switch (evt)
                {
                    case WorkflowOutputEvent output when output.Is<InsightWorkflowOutcome>(out var o):
                        outcome = o;
                        break;
                    case ExecutorFailedEvent failed:
                        recorder.Recovery($"executor {failed.ExecutorId} failed ({(failed.Data as Exception)?.GetType().Name ?? "error"}); deterministic insight retained");
                        break;
                    case WorkflowErrorEvent:
                        recorder.Recovery("workflow error; deterministic insight retained");
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            recorder.Finish(null, ExecutionStatus.Cancelled, "Playback changed or the request was cancelled; the execution was discarded.");
            return recorder.Snapshot();
        }
        catch (OperationCanceledException)
        {
            recorder.Finish(null, ExecutionStatus.Failed, $"Workflow exceeded its {_limits.WorkflowTimeout.TotalSeconds:0} s limit; deterministic insight retained.");
            return recorder.Snapshot();
        }

        // The framework may surface cancellation as an executor failure rather than an exception: classify by our own tokens.
        if (outcome is null && cancellationToken.IsCancellationRequested)
            recorder.Finish(null, ExecutionStatus.Cancelled, "Playback changed or the request was cancelled; the execution was discarded.");
        else if (outcome is null && deadline.IsCancellationRequested)
            recorder.Finish(null, ExecutionStatus.Failed, $"Workflow exceeded its {_limits.WorkflowTimeout.TotalSeconds:0} s limit; deterministic insight retained.");
        else
            recorder.Finish(outcome, outcome is null ? ExecutionStatus.Failed : ExecutionStatus.Completed,
                outcome is null ? "The workflow produced no outcome; deterministic insight retained." : null);
        return recorder.Snapshot();
    }
}
