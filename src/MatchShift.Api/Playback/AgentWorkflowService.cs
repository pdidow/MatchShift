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
    InsightWorkflowRunner runner, AgentWorkflowOptions options, AgentRunQuota quota, ILogger<AgentWorkflowService> logger, TimeProvider? time = null,
    IHttpContextAccessor? http = null) : IHostedService
{
    public const int MaxCachedExecutions = 512;
    private static readonly string[] ForeignClubNames = [.. ClubCatalog.DatasetClubs.SelectMany(c => new[] { c.Name, c.ShortName })];

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Handle> _handles = new();
    private readonly object _startGate = new();
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.MaxConcurrentWorkflows));
    private readonly InsightWorkflowRunner _offline = new(new OfflineTemplateAgentFactory(), runner.Limits);
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

    /// <summary>Shown when the live-agent quota is used up. Deliberately says nothing about limits or spend.</summary>
    public const string QuotaMessage = "Live AI analysis is temporarily unavailable; deterministic match evidence remains available.";

    public QuotaStatus Quota => quota.Status();

    /// <summary>The caller's address (App Service forwards the client IP); used only as a hashed per-client quota key.</summary>
    private string CurrentClient() => http?.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "local";

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

    /// <summary>
    /// Returns the existing execution for this insight/profile (cached results and in-flight runs are reused and consume
    /// nothing), or starts one. A live run first reserves quota; if the quota is exhausted an on-demand request gets an
    /// OFFLINE_TEMPLATE execution (no Foundry call, never verified) and an automatic request is simply not run.
    /// </summary>
    public StartResult GetOrStart(DemoSession session, Insight insight, ViewerProfile profile, bool accessibility, WorkflowTrigger trigger,
        string? client = null)
    {
        // Atomically de-duplicate and reserve: simultaneous pollers must not consume multiple run allowances.
        lock (_startGate) return StartLocked(session, insight, profile, accessibility, trigger, client);
    }

    private StartResult StartLocked(DemoSession session, Insight insight, ViewerProfile profile, bool accessibility, WorkflowTrigger trigger,
        string? client)
    {
        client ??= CurrentClient();
        var match = session.Match;
        var key = KeyFor(Mode, match, insight, profile, accessibility);
        if (_handles.TryGetValue(key, out var existing))
        {
            // A failed or fallback result stays visible (pollers must see it, not trigger a new run), but it is not reused
            // forever: after a cool-down, a new start request replaces it, so a cleared cause (RBAC propagation, outage) recovers.
            if (!IsRetryable(existing)) return new(existing, null);
            _handles.TryRemove(new KeyValuePair<string, Handle>(key, existing));
        }
        if (!EvidenceGate.Check(insight, match).Approved) return new(null, "Evidence withheld: the insight does not match deterministic replay.");

        if (trigger == WorkflowTrigger.AutoMeaningful && session.AgentAutoRunsUsed >= options.SessionAutoRunBudget)
            return new(null, "Automatic agent runs for this session are used up; open the Agents tab to run one on demand.");

        var selected = runner;
        string? quotaNotice = null;
        if (Mode == ExecutionMode.LiveFoundry)
        {
            var refusal = quota.TryReserve(client);
            if (refusal != QuotaRefusal.None)
            {
                logger.LogInformation("Live agent quota reached ({Cap}); no Foundry call", refusal);
                if (trigger == WorkflowTrigger.AutoMeaningful) return new(null, QuotaMessage);
                selected = _offline;
                quotaNotice = QuotaMessage;
            }
        }
        if (trigger == WorkflowTrigger.AutoMeaningful) session.TryUseAgentAutoRun(options.SessionAutoRunBudget);

        var (epochToken, epoch) = session.EpochScope();
        var context = InsightContextBuilder.Build(insight, match, profile,
            accessibility ? AccessibilityRequirements.All : AccessibilityRequirements.None, $"wf-{Guid.NewGuid():N}"[..19], ForeignClubNames);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(epochToken, _stopping.Token);
        var handle = new Handle(key, selected.CreateRecorder(context), cancellation, trigger);
        if (!_handles.TryAdd(key, handle))
        {
            cancellation.Dispose();
            return new(_handles[key], null);
        }
        handle.Completion = RunAsync(handle, selected, context, session, epoch, quotaNotice);
        Evict();
        return new(handle, null);
    }

    /// <summary>Auto-run policy: fresh, meaningful candidates while playing. Bounded by the session and hourly budgets.</summary>
    public void ConsiderAutoRun(DemoSession session, IEnumerable<Insight> visible, ViewerProfile profile, bool accessibility, int clock,
        string? client = null)
    {
        if (!options.AutoRunMeaningful || Mode != ExecutionMode.LiveFoundry || !session.IsPlaying) return;
        foreach (var insight in visible.Where(i => IsMeaningful(i, clock)))
        {
            if (Find(session.Match, insight, profile, accessibility) is not null) continue;
            if (GetOrStart(session, insight, profile, accessibility, WorkflowTrigger.AutoMeaningful, client).Refusal is not null) return;
        }
    }

    public bool IsMeaningful(Insight i, int clock) =>
        clock - i.DetectedAtSecond <= 120 &&
        (i.Type is InsightType.Goal or InsightType.BigChance ||
         (i.Importance >= options.MeaningfulImportance && i.Confidence != ConfidenceLevel.Low && i.Evidence.All(e => e.SampleRequirementMet)));

    private async Task<WorkflowExecution> RunAsync(Handle handle, InsightWorkflowRunner selected, InsightContext context, DemoSession session,
        long epoch, string? quotaNotice)
    {
        await Task.Yield();
        var acquired = false;
        try
        {
            await _slots.WaitAsync(handle.Cancellation.Token);
            acquired = true;
            var result = await selected.RunAsync(context, handle.Recorder, handle.Cancellation.Token);
            if (quotaNotice is not null && result.Published is { } published)
                result = result with
                {
                    Published = published with { Notice = quotaNotice },
                    RecoveryActions = [.. result.RecoveryActions, "Live-agent quota reached: OFFLINE_TEMPLATE used, no Foundry call made."],
                };
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

    /// <summary>How long a failed or fallback result is shown before a new request may run the workflow again.</summary>
    public static readonly TimeSpan RetryCooldown = TimeSpan.FromSeconds(60);

    private bool IsRetryable(Handle h) =>
        h.Completion is { IsCompletedSuccessfully: true, Result: { } r }
        && (r.Status == ExecutionStatus.Failed || r.Outcome == WorkflowOutcome.FallbackUnverified
            || (r.ExecutionMode == ExecutionMode.OfflineTemplate && Mode == ExecutionMode.LiveFoundry))
        && _time.GetUtcNow() - (r.CompletedAt ?? h.Created) >= RetryCooldown;

    private void Evict()
    {
        if (_handles.Count <= MaxCachedExecutions) return;
        foreach (var old in _handles.Values.Where(h => h.Completion.IsCompleted).OrderBy(h => h.Created).Take(_handles.Count - MaxCachedExecutions))
            _handles.TryRemove(old.Key, out _);
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
