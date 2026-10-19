using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.Infrastructure.Foundry;
using MatchShift.SyntheticData.Clubs;

namespace MatchShift.Api.Playback;

public enum WorkflowTrigger { OnDemand, AutoMeaningful }

/// <summary>
/// Owns agent workflow executions for the API. Cost and safety controls live here, in code:
/// only insight candidates raised by the deterministic engine are eligible (never raw events); identical
/// executions are de-duplicated and cached; concurrency, hourly volume and per-session automatic runs are
/// capped; and seeking, resetting or changing match cancels in-flight work for that session.
/// </summary>
public sealed class AgentWorkflowService(
    InsightWorkflowRunner runner, AgentWorkflowOptions options, ILogger<AgentWorkflowService> logger, TimeProvider? time = null) : IHostedService
{
    public const int MaxCachedExecutions = 512;
    private static readonly string[] ForeignClubNames = [.. ClubCatalog.DatasetClubs.SelectMany(c => new[] { c.Name, c.ShortName })];

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Handle> _handles = new();
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.MaxConcurrentWorkflows));
    private readonly Queue<DateTimeOffset> _recentStarts = new();
    private readonly object _budgetGate = new();
    private readonly CancellationTokenSource _stopping = new();

    public sealed class Handle(string key, WorkflowTraceRecorder recorder, CancellationTokenSource cancellation, WorkflowTrigger trigger)
    {
        public string Key => key;
        public WorkflowTraceRecorder Recorder => recorder;
        public CancellationTokenSource Cancellation => cancellation;
        public WorkflowTrigger Trigger => trigger;
        public Task<WorkflowExecution> Completion { get; internal set; } = Task.FromResult<WorkflowExecution>(null!);
        public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;
        public WorkflowExecution Snapshot() => Completion.IsCompletedSuccessfully && Completion.Result is { } done ? done : recorder.Snapshot();
    }

    public sealed record StartResult(Handle? Handle, string? Refusal);

    public string WarmUp { get; private set; } = "not required";
    public ExecutionMode Mode => runner.Factory.Mode;

    public int RunsLastHour
    {
        get { lock (_budgetGate) { Prune(); return _recentStarts.Count; } }
    }

    public static string KeyFor(ExecutionMode mode, Match match, Insight insight, ViewerProfile profile, bool accessibility) =>
        string.Join('|', mode, match.Id, insight.Id, Hash(insight), profile.Mode, profile.Language, profile.FocusTeamId, profile.FocusPlayerId, accessibility);

    public Handle? Find(Match match, Insight insight, ViewerProfile profile, bool accessibility) =>
        _handles.TryGetValue(KeyFor(Mode, match, insight, profile, accessibility), out var h) ? h : null;

    /// <summary>The finished, published outcome for a card, if one exists. Never returns in-progress or cancelled work.</summary>
    public PublishedInsight? Published(Match match, Insight insight, ViewerProfile profile, bool accessibility)
    {
        var h = Find(match, insight, profile, accessibility) ?? (accessibility ? null : Find(match, insight, profile, true));
        return h?.Completion is { IsCompletedSuccessfully: true, Result: { Status: ExecutionStatus.Completed, Published: { } p } } ? p : null;
    }

    public StartResult GetOrStart(DemoSession session, Insight insight, ViewerProfile profile, bool accessibility, WorkflowTrigger trigger)
    {
        var match = session.Match;
        var key = KeyFor(Mode, match, insight, profile, accessibility);
        if (_handles.TryGetValue(key, out var existing)) return new(existing, null);
        if (!EvidenceGate.Check(insight, match).Approved) return new(null, "Evidence withheld: the insight does not match deterministic replay.");

        lock (_budgetGate)
        {
            Prune();
            if (Mode == ExecutionMode.LiveFoundry && _recentStarts.Count >= options.MaxRunsPerHour)
                return new(null, $"Agent workflow budget reached ({options.MaxRunsPerHour} executions per hour). The deterministic insight remains available.");
            if (trigger == WorkflowTrigger.AutoMeaningful && !session.TryUseAgentAutoRun(options.SessionAutoRunBudget))
                return new(null, "Automatic agent runs for this session are used up; open the Agents tab to run one on demand.");
            if (Mode == ExecutionMode.LiveFoundry) _recentStarts.Enqueue(_time.GetUtcNow());
        }

        var (epochToken, epoch) = session.EpochScope();
        var context = InsightContextBuilder.Build(insight, match, profile,
            accessibility ? AccessibilityRequirements.All : AccessibilityRequirements.None, $"wf-{Guid.NewGuid():N}"[..19], ForeignClubNames);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(epochToken, _stopping.Token);
        var handle = new Handle(key, runner.CreateRecorder(context), cancellation, trigger);
        if (!_handles.TryAdd(key, handle))
        {
            cancellation.Dispose();
            return new(_handles[key], null);
        }
        handle.Completion = RunAsync(handle, context, session, epoch);
        Evict();
        return new(handle, null);
    }

    /// <summary>Auto-run policy: fresh, meaningful candidates while playing. Bounded by the session and hourly budgets.</summary>
    public void ConsiderAutoRun(DemoSession session, IEnumerable<Insight> visible, ViewerProfile profile, bool accessibility, int clock)
    {
        if (!options.AutoRunMeaningful || Mode != ExecutionMode.LiveFoundry || !session.IsPlaying) return;
        foreach (var insight in visible.Where(i => IsMeaningful(i, clock)))
        {
            if (Find(session.Match, insight, profile, accessibility) is not null) continue;
            if (GetOrStart(session, insight, profile, accessibility, WorkflowTrigger.AutoMeaningful).Refusal is not null) return;
        }
    }

    public bool IsMeaningful(Insight i, int clock) =>
        clock - i.DetectedAtSecond <= 120 &&
        (i.Type is InsightType.Goal or InsightType.BigChance ||
         (i.Importance >= options.MeaningfulImportance && i.Confidence != ConfidenceLevel.Low && i.Evidence.All(e => e.SampleRequirementMet)));

    private async Task<WorkflowExecution> RunAsync(Handle handle, InsightContext context, DemoSession session, long epoch)
    {
        await Task.Yield();
        var acquired = false;
        try
        {
            await _slots.WaitAsync(handle.Cancellation.Token);
            acquired = true;
            var result = await runner.RunAsync(context, handle.Recorder, handle.Cancellation.Token);
            if (result.Status == ExecutionStatus.Cancelled) _handles.TryRemove(handle.Key, out _);
            logger.LogInformation("Agent workflow {Id} for {Insight}: {Status} {Outcome} in {Ms} ms, {Calls} model call(s)",
                result.WorkflowExecutionId, result.InsightId, result.Status, result.Outcome, result.DurationMs, result.ModelCalls);
            return result;
        }
        catch (OperationCanceledException)
        {
            handle.Recorder.Finish(null, ExecutionStatus.Cancelled, "Playback changed before the workflow started; discarded.");
            _handles.TryRemove(handle.Key, out _);
            return handle.Recorder.Snapshot();
        }
        catch (Exception ex)
        {
            logger.LogWarning("Agent workflow failed ({FailureType}); deterministic insight retained", ex.GetType().Name);
            handle.Recorder.Finish(null, ExecutionStatus.Failed, $"Workflow failed ({ex.GetType().Name}); deterministic insight retained.");
            return handle.Recorder.Snapshot();
        }
        finally
        {
            if (acquired) _slots.Release();
            session.NotifyAgentResult(epoch);
        }
    }

    private void Evict()
    {
        if (_handles.Count <= MaxCachedExecutions) return;
        foreach (var old in _handles.Values.Where(h => h.Completion.IsCompleted).OrderBy(h => h.Created).Take(_handles.Count - MaxCachedExecutions))
            _handles.TryRemove(old.Key, out _);
    }

    private void Prune()
    {
        var cutoff = _time.GetUtcNow().AddHours(-1);
        while (_recentStarts.Count > 0 && _recentStarts.Peek() < cutoff) _recentStarts.Dequeue();
    }

    private static string Hash(Insight insight) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(insight)))[..12];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (runner.Factory is not FoundryAgentFactory foundry) return Task.CompletedTask;
        WarmUp = "acquiring Entra ID token";
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                await foundry.WarmUpAsync(timeout.Token);
                WarmUp = "ready (Entra ID token acquired; no model call made)";
            }
            catch (Exception ex)
            {
                WarmUp = $"credential unavailable ({ex.GetType().Name}); agent stages will fall back safely";
                logger.LogWarning("Foundry credential warm-up failed ({FailureType})", ex.GetType().Name);
            }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        return Task.CompletedTask;
    }
}
