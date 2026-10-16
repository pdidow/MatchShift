using System.Threading.Channels;
using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Api.Playback;

/// <summary>Two workers and a bounded queue keep provider latency off the playback/SSE thread.</summary>
public sealed class NarrationQueue(AgentPipeline pipeline, ILogger<NarrationQueue> logger) : BackgroundService
{
    private sealed record Job(Insight Insight, Match Match, NarrativeContext Context,
        CancellationToken Token, TaskCompletionSource<AgentTrace> Result, Action Completed);
    private readonly Channel<Job> _jobs = Channel.CreateBounded<Job>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait });

    public Task<AgentTrace>? Enqueue(Insight i, Match m, NarrativeContext c, CancellationToken token, Action completed)
    {
        var result = new TaskCompletionSource<AgentTrace>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _jobs.Writer.TryWrite(new Job(i, m, c, token, result, completed)) ? result.Task : null;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(Worker(stoppingToken), Worker(stoppingToken));

    private async Task Worker(CancellationToken stop)
    {
        await foreach (var job in _jobs.Reader.ReadAllAsync(stop))
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, job.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                var trace = await pipeline.RunAsync(job.Insight, job.Match, job.Context, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                job.Result.TrySetResult(trace);
                job.Completed();
            }
            catch (OperationCanceledException) { job.Result.TrySetCanceled(linked.Token); }
            catch (Exception ex)
            {
                logger.LogWarning("Narration job withheld ({FailureType})", ex.GetType().Name);
                job.Result.TrySetException(ex);
                job.Completed();
            }
        }
    }
}
