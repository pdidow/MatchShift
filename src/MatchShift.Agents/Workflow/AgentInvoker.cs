using System.Diagnostics;
using Microsoft.Agents.AI;

namespace MatchShift.Agents.Workflow;

/// <summary>Bounds on one workflow execution. Enforced in code, not in prompts.</summary>
public sealed record WorkflowLimits
{
    public TimeSpan AgentCallTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan WorkflowTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>Attempts per agent stage, including the first (retries apply to transient failures only).</summary>
    public int MaxAttemptsPerAgent { get; init; } = 2;

    /// <summary>Model calls across the whole execution, retries and repair included.</summary>
    public int MaxModelCalls { get; init; } = 8;

    /// <summary>Longest back-off honoured for throttling; longer server cool-downs fail fast to the fallback.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>One constrained repair after a REJECTED verdict.</summary>
    public bool AllowRepair { get; init; } = true;

    public static WorkflowLimits Default { get; } = new();
}

/// <summary>Shared per-execution counter of model calls.</summary>
public sealed class ModelCallBudget(int limit)
{
    private int _used;
    public int Used => Volatile.Read(ref _used);
    public int Limit => limit;

    public bool TryReserve()
    {
        while (true)
        {
            var current = Volatile.Read(ref _used);
            if (current >= limit) return false;
            if (Interlocked.CompareExchange(ref _used, current + 1, current) == current) return true;
        }
    }
}

public sealed record AgentCallOutcome<T>(
    T? Result, int Attempts, AgentFailureKind? Failure, IReadOnlyList<string> AttemptFailures, long InputTokens, long OutputTokens, long DurationMs)
    where T : class
{
    public bool Succeeded => Result is not null;
    public int Retries => Math.Max(0, Attempts - 1);
}

/// <summary>
/// Runs one Agent Framework agent with structured output, a per-call timeout, bounded retry with back-off
/// for transient failures (timeout, throttling, provider errors, malformed output) and a shared call budget.
/// Records only failure codes: never prompts, responses, headers or credentials.
/// </summary>
public sealed class AgentInvoker(IInsightAgentFactory factory, WorkflowLimits limits, ModelCallBudget budget, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<AgentCallOutcome<T>> RunAsync<T>(AIAgent agent, string prompt, Func<T, T> normalize, Func<T, bool> wellFormed,
        CancellationToken cancellationToken) where T : class
    {
        var sw = Stopwatch.StartNew();
        var failures = new List<string>();
        long inputTokens = 0, outputTokens = 0;
        AgentFailureKind? last = null;
        var attempts = 0;
        for (var attempt = 0; attempt < limits.MaxAttemptsPerAgent; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!budget.TryReserve())
            {
                failures.Add(nameof(AgentFailureKind.BudgetExhausted));
                last = AgentFailureKind.BudgetExhausted;
                break;
            }
            attempts++;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(limits.AgentCallTimeout);
            TimeSpan? retryAfter = null;
            string? detail = null;
            try
            {
                var response = await agent.RunAsync<T>(prompt, session: null, serializerOptions: AgentJson.Options, cancellationToken: timeout.Token);
                inputTokens += response.Usage?.InputTokenCount ?? 0;
                outputTokens += response.Usage?.OutputTokenCount ?? 0;
                var result = response.Result is null ? null : normalize(response.Result);
                if (result is not null && wellFormed(result))
                    return new(result, attempts, null, failures, inputTokens, outputTokens, sw.ElapsedMilliseconds);
                last = AgentFailureKind.MalformedOutput;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                last = AgentFailureKind.Timeout;
            }
            catch (Exception ex)
            {
                (last, retryAfter) = factory.Classify(ex);
                detail = factory.FailureDetail(ex);
            }
            failures.Add(detail is null ? last.Value.ToString() : $"{last.Value} ({detail})");

            var retryable = last is AgentFailureKind.Timeout or AgentFailureKind.Throttled or AgentFailureKind.ProviderError or AgentFailureKind.MalformedOutput;
            if (!retryable || attempt + 1 >= limits.MaxAttemptsPerAgent) break;
            var delay = retryAfter ?? TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt));
            if (last == AgentFailureKind.Throttled && delay > limits.MaxBackoff) break;
            if (last is AgentFailureKind.Throttled or AgentFailureKind.ProviderError)
                await Task.Delay(delay < limits.MaxBackoff ? delay : limits.MaxBackoff, _time, cancellationToken);
        }
        return new(null, attempts, last ?? AgentFailureKind.Unknown, failures, inputTokens, outputTokens, sw.ElapsedMilliseconds);
    }
}
