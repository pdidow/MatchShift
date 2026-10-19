using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MatchShift.Agents.Workflow;
using MatchShift.Analytics;
using MatchShift.Api.Playback;
using MatchShift.Core.Insights;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MatchShift.Api.Tests;

/// <summary>The agent workflow through the API. The test host is forced to OFFLINE_TEMPLATE (see OfflineTestMode).</summary>
public class AgentWorkflowApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<(HttpClient Client, string Id)> SeekToShift()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/demo/reset", new { matchId = "MS-FLAGSHIP-001" });
        await client.PostAsJsonAsync("/api/demo/seek", new { minute = 66 });
        var insights = await client.GetFromJsonAsync<JsonElement>("/api/demo/insights");
        var id = insights.EnumerateArray().First(i => i.GetProperty("type").GetString() == "ATTACK_CHANNEL_SHIFT").GetProperty("id").GetString()!;
        return (client, id);
    }

    private static async Task<JsonElement> Completed(HttpClient client, string url)
    {
        for (var i = 0; i < 100; i++)
        {
            var x = await client.GetFromJsonAsync<JsonElement>(url);
            if (x.GetProperty("status").GetString() != "RUNNING") return x;
            await Task.Delay(50);
        }
        throw new TimeoutException("workflow did not finish");
    }

    [Fact]
    public async Task Status_reports_offline_template_mode_honestly_and_without_secrets()
    {
        using var client = factory.CreateClient();
        var raw = await client.GetStringAsync("/api/agents/status");
        var status = JsonDocument.Parse(raw).RootElement;
        var runtime = status.GetProperty("runtime");
        Assert.Equal("OFFLINE_TEMPLATE", runtime.GetProperty("executionMode").GetString());
        Assert.Equal("offline-template", runtime.GetProperty("provider").GetString());
        Assert.True(!runtime.TryGetProperty("modelDeployment", out var model) || model.ValueKind == JsonValueKind.Null);
        Assert.Contains("Microsoft Agent Framework", runtime.GetProperty("orchestrator").GetString());
        Assert.StartsWith("1.", runtime.GetProperty("packages").GetProperty("Microsoft.Agents.AI.Workflows").GetString());
        Assert.Equal(4, status.GetProperty("agents").GetArrayLength());
        foreach (var d in MatchShiftAgents.All) Assert.DoesNotContain(d.Instructions[..60], raw);
        Assert.DoesNotContain("ROLE:", raw);
        Assert.DoesNotContain("api-key", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", raw, StringComparison.OrdinalIgnoreCase);

        var meta = await client.GetFromJsonAsync<JsonElement>("/api/meta");
        Assert.Equal("OFFLINE_TEMPLATE", meta.GetProperty("agentWorkflow").GetProperty("executionMode").GetString());
    }

    [Fact]
    public async Task Agents_endpoint_runs_the_workflow_once_and_reuses_the_execution()
    {
        var (client, id) = await SeekToShift();
        var url = $"/api/demo/insights/{id}/agents?a11y=true";
        var x = await Completed(client, url);
        Assert.Equal("COMPLETED", x.GetProperty("status").GetString());
        Assert.Equal("DETERMINISTIC_ONLY", x.GetProperty("outcome").GetString());
        Assert.False(x.GetProperty("verified").GetBoolean());
        Assert.True(x.GetProperty("authoritativeFactsUnchanged").GetBoolean());
        Assert.Equal(8, x.GetProperty("steps").GetArrayLength());
        Assert.All(x.GetProperty("steps").EnumerateArray(), s => Assert.Equal("DETERMINISTIC", s.GetProperty("executionType").GetString()));
        Assert.Equal(7, x.GetProperty("frameworkSupersteps").GetInt32());

        var again = await client.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal(x.GetProperty("workflowExecutionId").GetString(), again.GetProperty("workflowExecutionId").GetString());

        var raw = await client.GetStringAsync(url);
        Assert.DoesNotContain("ROLE:", raw);
        Assert.DoesNotContain("insightContext", raw);
    }

    [Fact]
    public async Task Read_only_request_does_not_start_a_workflow()
    {
        var (client, id) = await SeekToShift();
        var res = await client.GetAsync($"/api/demo/insights/{id}/agents?run=false&lang=fr");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/demo/insights/NOPE/agents")).StatusCode);
    }

    [Fact]
    public async Task Completed_workflow_outcome_is_attached_to_the_insight_card_and_why()
    {
        var (client, id) = await SeekToShift();
        await Completed(client, $"/api/demo/insights/{id}/agents");
        var insights = await client.GetFromJsonAsync<JsonElement>("/api/demo/insights");
        var card = insights.EnumerateArray().First(i => i.GetProperty("id").GetString() == id);
        var agent = card.GetProperty("agent");
        Assert.Equal("DETERMINISTIC_ONLY", agent.GetProperty("outcome").GetString());
        Assert.False(agent.GetProperty("verified").GetBoolean());
        Assert.True(!agent.TryGetProperty("headline", out var h) || h.ValueKind == JsonValueKind.Null);

        // WHY? still serves the deterministic evidence, now with the workflow outcome alongside.
        var why = await client.GetFromJsonAsync<JsonElement>($"/api/demo/insights/{id}/evidence");
        Assert.True(why.GetProperty("rawEvidence").GetArrayLength() > 0);
        Assert.Equal("DETERMINISTIC_ONLY", why.GetProperty("insight").GetProperty("agent").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Show_me_and_replay_still_resolve_the_insights_supporting_events()
    {
        var (client, id) = await SeekToShift();
        var match = new MatchCatalog().Default;
        var insight = LiveMatchAnalyzer.AnalyzeFull(match).Insights.First(i => i.Id == id);
        var allowed = insight.SupportingEventIds.Concat(insight.Visuals.SelectMany(v => v.EventIds)).ToHashSet();

        var events = await client.GetFromJsonAsync<JsonElement>($"/api/demo/insights/{id}/events");
        var shown = events.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();
        Assert.NotEmpty(shown);
        Assert.All(shown, e => Assert.Contains(e, allowed));
        Assert.All(shown, e => Assert.True(match.Events.First(m => m.Id == e).MatchSecond <= insight.DetectedAtSecond));

        var replay = await client.GetFromJsonAsync<JsonElement>($"/api/demo/insights/{id}/replay");
        Assert.Equal(id, replay.GetProperty("insightId").GetString());
        var replayEvents = replay.GetProperty("clips").EnumerateArray().SelectMany(c => c.GetProperty("events").EnumerateArray())
            .Where(e => e.TryGetProperty("eventId", out var v) && v.ValueKind == JsonValueKind.String).Select(e => e.GetProperty("eventId").GetString()!).ToList();
        Assert.All(replayEvents, e => Assert.True(match.Events.First(m => m.Id == e).MatchSecond <= insight.DetectedAtSecond));
    }

    [Fact]
    public void Meaningful_policy_only_admits_fresh_high_value_candidates()
    {
        var service = new AgentWorkflowService(new InsightWorkflowRunner(new OfflineTemplateAgentFactory()),
            new MatchShift.Infrastructure.Foundry.AgentWorkflowOptions(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentWorkflowService>.Instance);
        var insights = LiveMatchAnalyzer.AnalyzeFull(new MatchCatalog().Default).Insights;
        var goal = insights.First(i => i.Type == InsightType.Goal);
        Assert.True(service.IsMeaningful(goal, goal.DetectedAtSecond + 30));
        Assert.False(service.IsMeaningful(goal, goal.DetectedAtSecond + 600));
        var weak = insights.FirstOrDefault(i => i.Importance < 50 && !i.Type.IsMoment());
        if (weak is not null) Assert.False(service.IsMeaningful(weak, weak.DetectedAtSecond));
    }
}
