using System.Net.Http.Json;
using System.Text.Json;
using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
using MatchShift.Api.Contracts;
using MatchShift.Api.Playback;
using MatchShift.Core.Insights;
using MatchShift.Infrastructure.Foundry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace MatchShift.Api.Tests;

public class UpgradeIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Independent_clients_have_independent_clocks_and_overlay_shares_only_the_named_session()
    {
        using var a = factory.CreateClient(); using var b = factory.CreateClient();
        (await a.PostAsJsonAsync("/api/demo/seek", new { minute = 63 })).EnsureSuccessStatusCode();
        var first = await a.GetFromJsonAsync<JsonElement>("/api/demo/state");
        var other = await b.GetFromJsonAsync<JsonElement>("/api/demo/state");
        Assert.Equal(0, other.GetProperty("clock").GetProperty("second").GetInt32());
        Assert.NotEqual(first.GetProperty("sessionId").GetString(), other.GetProperty("sessionId").GetString());
        var id = first.GetProperty("sessionId").GetString();
        var overlay = await b.GetFromJsonAsync<JsonElement>($"/api/demo/overlay?session={id}");
        Assert.Equal(63 * 60, overlay.GetProperty("clock").GetProperty("second").GetInt32());
        Assert.Equal("matchshift.overlay.v1", overlay.GetProperty("overlay").GetProperty("schemaVersion").GetString());
        (await a.PostAsJsonAsync("/api/demo/seek", new { minute = 0 })).EnsureSuccessStatusCode();
        var reset = await b.GetFromJsonAsync<JsonElement>($"/api/demo/overlay?session={id}");
        Assert.True(!reset.TryGetProperty("overlay", out var value) || value.ValueKind == JsonValueKind.Null);
        Assert.True(reset.GetProperty("epoch").GetInt64() > overlay.GetProperty("epoch").GetInt64());
    }

    [Fact]
    public async Task Background_work_is_reused_and_seek_cancels_inflight_work()
    {
        var client = new BlockingClient();
        var pipeline = new AgentPipeline(new NarrationService(new FoundryInsightProvider(client, "mock")), new VerificationAgent());
        using var queue = new NarrationQueue(pipeline, NullLogger<NarrationQueue>.Instance);
        await queue.StartAsync(default);
        var session = new DemoSession(new MatchCatalog(), queue);
        session.Seek(63 * 60);
        var i = session.Read((a, _) => a.Insights.First(x => x.Type == InsightType.AttackChannelShift));
        var context = new NarrativeContext(new MatchRoster(session.Match), ViewerProfile.Default);
        var task = session.Trace(i, context)!;
        Assert.Same(task, session.Trace(i, context));
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("template", session.Narrative(i, context).Source);
        session.Seek(0);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        session.Seek(63 * 60);
        client.Release.TrySetResult();
        var accepted = await session.Trace(i, context)!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("mock", accepted.Narrative.Source);
        Assert.Same(accepted.Narrative, session.Narrative(i, context));
        var calls = client.Calls;
        Assert.Same(accepted, await session.Trace(i, context)!);
        Assert.Equal(calls, client.Calls);
        session.Close();
        await queue.StopAsync(default);
    }

    [Fact]
    public async Task Queue_capacity_is_bounded()
    {
        using var queue = new NarrationQueue(new AgentPipeline(new NarrationService(new TemplateInsightProvider()), new VerificationAgent()), NullLogger<NarrationQueue>.Instance);
        var session = new DemoSession(new MatchCatalog()); session.Seek(63 * 60);
        var i = session.Read((a, _) => a.Insights.First());
        var c = new NarrativeContext(new MatchRoster(session.Match), ViewerProfile.Default);
        for (var n = 0; n < 64; n++) Assert.NotNull(queue.Enqueue(i, session.Match, c, default, () => { }));
        Assert.Null(queue.Enqueue(i, session.Match, c, default, () => { }));
        await Task.CompletedTask;
    }

    [Fact]
    public void Overlay_expires_on_the_match_clock()
    {
        var session = new DemoSession(new MatchCatalog()); session.Seek(63 * 60);
        var snap = session.Read((a, clock) => SnapshotBuilder.Build(a, clock, session.Match, ViewerProfile.Default, false, 2));
        var overlay = OverlayDto.From(snap)!;
        Assert.NotNull(overlay);
        var one = snap with { Insights = snap.Insights.Where(i => i.Id == overlay.InsightId).ToArray() };
        Assert.Null(OverlayDto.From(one with { Clock = one.Clock with { Second = overlay.ValidUntilSecond } }));
        Assert.Null(OverlayDto.From(one with { Clock = one.Clock with { Second = overlay.ValidFromSecond - 1 } }));
    }

    private sealed class BlockingClient : IChatCompletionClient
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls); Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await new MockChatCompletionClient().CompleteJsonAsync(systemPrompt, userPrompt, cancellationToken);
        }
    }
}
