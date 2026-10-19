using MatchShift.Agents.Narrative;
using MatchShift.Agents.Tests;
using MatchShift.Agents.Workflow;
using MatchShift.Api.Playback;
using MatchShift.Core.Insights;
using MatchShift.Infrastructure.Foundry;
using Microsoft.Extensions.Logging.Abstractions;

namespace MatchShift.Api.Tests;

/// <summary>Live-agent cost controls. Clocks are manual, so every window reset is deterministic. No network.</summary>
public class AgentQuotaTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // ---------------------------------------------------------------- AgentRunQuota

    [Fact]
    public void Hourly_cap_refuses_then_resets_after_a_rolling_hour()
    {
        var time = new ManualTime();
        var quota = new AgentRunQuota(maxPerHour: 3, maxPerDay: 100, maxPerClientPerHour: 100, time);
        for (var i = 0; i < 3; i++) Assert.Equal(QuotaRefusal.None, quota.TryReserve($"c{i}"));
        Assert.Equal(QuotaRefusal.Hourly, quota.TryReserve("c9"));
        Assert.True(quota.Status().Exhausted);

        time.Now += TimeSpan.FromMinutes(59);
        Assert.Equal(QuotaRefusal.Hourly, quota.TryReserve("c9"));
        time.Now += TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1);
        Assert.Equal(QuotaRefusal.None, quota.TryReserve("c9"));
        Assert.Equal(4, quota.Status().RunsLast24Hours);
    }

    [Fact]
    public void Daily_cap_refuses_across_hours_then_resets_after_24_hours()
    {
        var time = new ManualTime();
        var quota = new AgentRunQuota(maxPerHour: 2, maxPerDay: 4, maxPerClientPerHour: 100, time);
        Assert.Equal(QuotaRefusal.None, quota.TryReserve("a"));
        Assert.Equal(QuotaRefusal.None, quota.TryReserve("a"));
        time.Now += TimeSpan.FromHours(2);
        Assert.Equal(QuotaRefusal.None, quota.TryReserve("a"));
        Assert.Equal(QuotaRefusal.None, quota.TryReserve("a"));
        time.Now += TimeSpan.FromHours(2);
        Assert.Equal(QuotaRefusal.Daily, quota.TryReserve("a"));
        Assert.Equal(QuotaRefusal.Daily, quota.TryReserve("b"));
        time.Now += TimeSpan.FromHours(20) + TimeSpan.FromSeconds(1);
        Assert.Equal(QuotaRefusal.None, quota.TryReserve("a"));
    }

    [Fact]
    public void Per_client_cap_isolates_one_client_from_others()
    {
        var quota = new AgentRunQuota(maxPerHour: 100, maxPerDay: 100, maxPerClientPerHour: 2, new ManualTime());
        Assert.Equal(QuotaRefusal.None, quota.TryReserve("203.0.113.5"));
        Assert.Equal(QuotaRefusal.None, quota.TryReserve("203.0.113.5"));
        Assert.Equal(QuotaRefusal.Client, quota.TryReserve("203.0.113.5"));
        Assert.Equal(QuotaRefusal.None, quota.TryReserve("198.51.100.7"));
    }

    [Fact]
    public void Global_caps_persist_across_a_restart_when_a_state_path_is_configured()
    {
        var path = Path.Combine(Path.GetTempPath(), $"matchshift-quota-{Guid.NewGuid():N}.json");
        try
        {
            var time = new ManualTime();
            var before = new AgentRunQuota(10, 3, 10, time, path);
            for (var i = 0; i < 3; i++) Assert.Equal(QuotaRefusal.None, before.TryReserve("x"));

            var afterRestart = new AgentRunQuota(10, 3, 10, time, path);
            Assert.Equal(3, afterRestart.Status().RunsLast24Hours);
            Assert.True(afterRestart.Status().Durable);
            Assert.Equal(QuotaRefusal.Daily, afterRestart.TryReserve("new-client"));
            Assert.DoesNotContain("x", File.ReadAllText(path)); // client keys are never written
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Unreadable_state_fails_closed_for_the_current_hour()
    {
        var path = Path.Combine(Path.GetTempPath(), $"matchshift-quota-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var quota = new AgentRunQuota(5, 50, 5, new ManualTime(), path);
            Assert.Equal(QuotaRefusal.Hourly, quota.TryReserve("a"));
        }
        finally { File.Delete(path); }
    }

    // ---------------------------------------------------------------- AgentWorkflowService with the quota

    private static (AgentWorkflowService Service, ScriptedChatClient Model, DemoSession Session, Insight Insight, AgentRunQuota Quota) Live(
        int perHour = 10, int perDay = 30, int perClient = 4, ManualTime? time = null)
    {
        var model = new ScriptedChatClient();
        var runner = new InsightWorkflowRunner(new ChatClientAgentFactory(model, ExecutionMode.LiveFoundry, "microsoft-foundry", "gpt-test"));
        var quota = new AgentRunQuota(perHour, perDay, perClient, time);
        var service = new AgentWorkflowService(runner, new AgentWorkflowOptions(), quota, NullLogger<AgentWorkflowService>.Instance, time);
        var session = new DemoSession(new MatchCatalog());
        session.Seek(66 * 60);
        var insight = session.Read((a, _) => a.Insights.First(i => i.Type == InsightType.AttackChannelShift));
        return (service, model, session, insight, quota);
    }

    [Fact]
    public async Task Duplicate_requests_and_polling_reuse_one_cached_execution_and_one_reservation()
    {
        var (service, model, session, insight, quota) = Live();
        var profile = new ViewerProfile();
        var first = service.GetOrStart(session, insight, profile, true, WorkflowTrigger.OnDemand).Handle!;
        var duplicate = service.GetOrStart(session, insight, profile, true, WorkflowTrigger.OnDemand).Handle!;
        Assert.Same(first, duplicate);
        var done = await first.Completion;
        Assert.Equal(WorkflowOutcome.Approved, done.Outcome);

        // Read-only polling (the endpoint's run=false path) and further identical requests start nothing.
        for (var i = 0; i < 5; i++)
        {
            Assert.Same(first, service.Find(session.Match, insight, profile, true));
            Assert.Same(first, service.GetOrStart(session, insight, profile, true, WorkflowTrigger.OnDemand).Handle);
        }
        Assert.Equal(1, quota.Status().RunsLast24Hours);
        Assert.Equal(4, model.Calls.Count);
        Assert.True(service.Published(session.Match, insight, profile, true)!.Verified);
    }

    [Fact]
    public async Task Exhausted_quota_gives_offline_template_never_live_or_verified_and_calls_no_model()
    {
        var (service, model, session, insight, _) = Live(perHour: 0);
        var x = await service.GetOrStart(session, insight, new ViewerProfile(), true, WorkflowTrigger.OnDemand).Handle!.Completion;

        Assert.Empty(model.Calls);
        Assert.Equal(ExecutionMode.OfflineTemplate, x.ExecutionMode);
        Assert.Equal("offline-template", x.Provider);
        Assert.All(x.Steps, s => Assert.Equal(ExecutionType.Deterministic, s.ExecutionType));
        Assert.Equal(WorkflowOutcome.DeterministicOnly, x.Outcome);
        Assert.False(x.Verified);
        Assert.False(x.Published!.Verified);
        Assert.Equal(ExecutionMode.OfflineTemplate, x.Published.ExecutionMode);
        Assert.Equal(AgentWorkflowService.QuotaMessage, x.Published.Notice);
        Assert.Contains(x.RecoveryActions, r => r.Contains("quota"));
        Assert.DoesNotContain("LIVE_FOUNDRY", System.Text.Json.JsonSerializer.Serialize(x, AgentJson.Options));
    }

    [Fact]
    public async Task Exhausted_quota_never_starts_automatic_runs()
    {
        var (service, model, session, insight, _) = Live(perHour: 0);
        session.Play();
        var refused = service.GetOrStart(session, insight, new ViewerProfile(), false, WorkflowTrigger.AutoMeaningful);
        Assert.Null(refused.Handle);
        Assert.Equal(AgentWorkflowService.QuotaMessage, refused.Refusal);
        Assert.Equal(0, session.AgentAutoRunsUsed);
        await Task.Delay(50);
        Assert.Empty(model.Calls);
    }

    [Fact]
    public async Task Daily_cap_reached_mid_session_switches_new_requests_to_offline_and_keeps_cached_live_results()
    {
        var (service, model, session, insight, _) = Live(perHour: 10, perDay: 1);
        var live = await service.GetOrStart(session, insight, new ViewerProfile(ViewerMode.Analyst, "en"), false, WorkflowTrigger.OnDemand).Handle!.Completion;
        Assert.Equal(ExecutionMode.LiveFoundry, live.ExecutionMode);

        var other = await service.GetOrStart(session, insight, new ViewerProfile(ViewerMode.TeachMe, "fr"), false, WorkflowTrigger.OnDemand).Handle!.Completion;
        Assert.Equal(ExecutionMode.OfflineTemplate, other.ExecutionMode);
        Assert.False(other.Verified);

        // The earlier live, verified result is still served from cache.
        Assert.True(service.Published(session.Match, insight, new ViewerProfile(ViewerMode.Analyst, "en"), false)!.Verified);
        Assert.Equal(3, model.Calls.Count); // accessibility not requested: that branch makes no model call
    }

    [Fact]
    public async Task Quota_fallback_is_retried_live_only_after_the_cooldown_and_only_on_a_new_start_request()
    {
        var time = new ManualTime { Now = DateTimeOffset.UtcNow };
        var (service, model, session, insight, _) = Live(perHour: 1, time: time);
        var profile = new ViewerProfile();
        await service.GetOrStart(session, insight, new ViewerProfile(ViewerMode.Studio, "es"), false, WorkflowTrigger.OnDemand).Handle!.Completion; // uses the 1 run
        var offline = await service.GetOrStart(session, insight, profile, false, WorkflowTrigger.OnDemand).Handle!.Completion;
        Assert.Equal(ExecutionMode.OfflineTemplate, offline.ExecutionMode);

        // Within the cool-down, requests keep returning the same offline result.
        Assert.Equal(offline.WorkflowExecutionId, service.GetOrStart(session, insight, profile, false, WorkflowTrigger.OnDemand).Handle!.Snapshot().WorkflowExecutionId);

        // After the hour has rolled (and the cool-down passed), a new start request runs live again.
        time.Now += TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1);
        var again = await service.GetOrStart(session, insight, profile, false, WorkflowTrigger.OnDemand).Handle!.Completion;
        Assert.Equal(ExecutionMode.LiveFoundry, again.ExecutionMode);
        Assert.Equal(6, model.Calls.Count); // two live runs of 3 calls (accessibility branch skipped)
    }
}
