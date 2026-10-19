using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Analytics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;
using Microsoft.Agents.AI;

namespace MatchShift.Agents.Tests;

/// <summary>
/// The Microsoft Agent Framework insight workflow, run with real Agent Framework agents (ChatClientAgent) and the real
/// workflow runtime (WorkflowBuilder + InProcessExecution) over a scripted model. No network: these are not live results.
/// </summary>
public class AgentWorkflowTests
{
    private static readonly Match Flagship = SyntheticMatchGenerator.GenerateFlagship().Match;
    private static readonly IReadOnlyList<Insight> Insights = LiveMatchAnalyzer.AnalyzeFull(Flagship).Insights;
    private static Insight Goal => Insights.First(i => i.Type == InsightType.Goal);
    private static Insight Shift => Insights.First(i => i.Type == InsightType.AttackChannelShift);

    private static InsightContext Context(Insight? insight = null, bool accessibility = true, ViewerMode mode = ViewerMode.Analyst) =>
        InsightContextBuilder.Build(insight ?? Shift, Flagship, new ViewerProfile(mode, "en"),
            accessibility ? AccessibilityRequirements.All : AccessibilityRequirements.None, "wf-test", ["Ravensmoor Rovers"]);

    private static (InsightWorkflowRunner Runner, ScriptedChatClient Model) Live(WorkflowLimits? limits = null)
    {
        var model = new ScriptedChatClient();
        return (new InsightWorkflowRunner(new ChatClientAgentFactory(model, ExecutionMode.LiveFoundry, "microsoft-foundry", "gpt-test"), limits), model);
    }

    private static async Task<WorkflowExecution> Run(InsightWorkflowRunner runner, InsightContext context, CancellationToken ct = default) =>
        await runner.RunAsync(context, runner.CreateRecorder(context), ct);

    private static string Json(object value) => JsonSerializer.Serialize(value, AgentJson.Options);

    private static TraceStep Step(WorkflowExecution x, string stage) => x.Steps.First(s => s.Stage == stage);

    // ----- Happy path, shared state, handoffs, concurrency -----

    [Fact]
    public async Task Live_workflow_runs_four_agents_through_the_agent_framework_graph_and_publishes_verified_text()
    {
        var (runner, model) = Live();
        var x = await Run(runner, Context());

        Assert.Equal(ExecutionStatus.Completed, x.Status);
        Assert.Equal(WorkflowOutcome.Approved, x.Outcome);
        Assert.True(x.Verified);
        Assert.Equal(4, model.Calls.Count);
        Assert.Equal(4, x.ModelCalls);
        Assert.Equal(7, x.FrameworkSupersteps);
        Assert.Equal(9, x.FrameworkExecutorInvocations);
        foreach (var stage in new[] { Stages.TacticalAnalyst, Stages.AudienceNarrative, Stages.AccessibilityNarrator, Stages.Verification })
        {
            var s = Step(x, stage);
            Assert.Equal(ExecutionType.MicrosoftAgentFramework, s.ExecutionType);
            Assert.Equal("gpt-test", s.ModelDeployment);
            Assert.Equal(1, s.ModelCalls);
        }
        foreach (var stage in new[] { Stages.Grounding, Stages.FanIn, Stages.DeterministicValidation, Stages.FinalValidation })
            Assert.Equal(ExecutionType.Deterministic, Step(x, stage).ExecutionType);
        Assert.Equal(VerifierDecisions.Approved, x.VerificationDecision);
        Assert.NotNull(x.Published!.Headline);
    }

    [Fact]
    public async Task Tactical_analyst_receives_the_grounded_insight_context_and_no_validation_only_data()
    {
        var (runner, model) = Live();
        var context = Context();
        await Run(runner, context);

        var prompt = JsonNode.Parse(model.For(AgentRole.TacticalAnalyst).Single().Prompt)!["insightContext"]!;
        Assert.Equal(Shift.Id, prompt["insightId"]!.GetValue<string>());
        Assert.Equal(Shift.DetectedAtSecond, prompt["matchSecond"]!.GetValue<int>());
        Assert.Equal(Shift.Evidence.Count, prompt["deterministicMetrics"]!.AsArray().Count);
        Assert.Equal(Shift.Evidence[0].RecentValue, prompt["deterministicMetrics"]![0]!["current"]!.GetValue<double>(), 3);
        Assert.Equal(context.Score.ToString(), $"{prompt["score"]!["home"]}-{prompt["score"]!["away"]}");
        Assert.All(prompt["supportingEventIds"]!.AsArray(), id => Assert.Contains(id!.GetValue<string>(), Shift.SupportingEventIds));
        Assert.True(prompt["sourceEvents"]!.AsArray().Count <= InsightContextBuilder.MaxSourceEvents);
        Assert.Null(prompt["knownEventSeconds"]);
        Assert.Null(prompt["allMatchPlayers"]);
    }

    [Fact]
    public async Task Audience_and_accessibility_branches_receive_the_same_grounded_state_and_tactical_result()
    {
        var (runner, model) = Live();
        await Run(runner, Context());
        var audience = JsonNode.Parse(model.For(AgentRole.AudienceNarrative).Single().Prompt)!;
        var accessible = JsonNode.Parse(model.For(AgentRole.AccessibilityNarrator).Single().Prompt)!;
        Assert.Equal(audience["insightContext"]!.ToJsonString(), accessible["insightContext"]!.ToJsonString());
        Assert.Equal(audience["tacticalAnalystResult"]!.ToJsonString(), accessible["tacticalAnalystResult"]!.ToJsonString());
    }

    [Fact]
    public async Task Audience_and_accessibility_run_concurrently_in_one_superstep_and_the_trace_shows_it()
    {
        var (runner, model) = Live();
        model.Respond = async (role, prompt, _, ct) =>
        {
            if (role is AgentRole.AudienceNarrative or AgentRole.AccessibilityNarrator) await Task.Delay(400, ct);
            return ScriptedChatClient.Good(role, prompt);
        };
        var x = await Run(runner, Context());

        var a = model.For(AgentRole.AudienceNarrative).Single();
        var b = model.For(AgentRole.AccessibilityNarrator).Single();
        Assert.True(a.Start < b.End && b.Start < a.End, "model calls did not overlap");
        Assert.NotNull(x.Concurrency);
        Assert.True(x.Concurrency!.SameSuperstep);
        Assert.True(x.Concurrency.Overlapped);
        Assert.True(x.Concurrency.OverlapMs >= 200);
        Assert.Equal(Step(x, Stages.AudienceNarrative).Superstep, Step(x, Stages.AccessibilityNarrator).Superstep);
    }

    [Fact]
    public async Task Handoffs_are_recorded_including_the_fan_out_and_fan_in()
    {
        var (runner, _) = Live();
        var x = await Run(runner, Context());
        Assert.Contains(x.Handoffs, h => h.From == Stages.Grounding && h.To.SequenceEqual([Stages.TacticalAnalyst]));
        Assert.Contains(x.Handoffs, h => h.From == Stages.TacticalAnalyst && h.To.SequenceEqual([Stages.AudienceNarrative, Stages.AccessibilityNarrator]));
        Assert.Contains(x.Handoffs, h => h.From == Stages.AudienceNarrative && h.To.SequenceEqual([Stages.FanIn]));
        Assert.Contains(x.Handoffs, h => h.From == Stages.AccessibilityNarrator && h.To.SequenceEqual([Stages.FanIn]));
        Assert.Contains(x.Handoffs, h => h.From == Stages.Verification && h.To.SequenceEqual([Stages.FinalValidation]));
        Assert.Equal([Stages.TacticalAnalyst], Step(x, Stages.AudienceNarrative).HandoffFrom);
    }

    [Fact]
    public async Task Verifier_receives_every_upstream_result_and_the_deterministic_findings()
    {
        var (runner, model) = Live();
        await Run(runner, Context());
        var prompt = JsonNode.Parse(model.For(AgentRole.Verification).Single().Prompt)!;
        Assert.NotNull(prompt["insightContext"]);
        Assert.NotNull(prompt["deterministicFindings"]);
        Assert.NotNull(prompt["tacticalAnalystResult"]);
        Assert.NotNull(prompt["audienceNarrativeResult"]);
        Assert.NotNull(prompt["accessibilityNarrativeResult"]);
    }

    [Fact]
    public async Task Accessibility_branch_is_skipped_without_a_model_call_when_not_required()
    {
        var (runner, model) = Live();
        var x = await Run(runner, Context(accessibility: false));
        Assert.Empty(model.For(AgentRole.AccessibilityNarrator));
        var step = Step(x, Stages.AccessibilityNarrator);
        Assert.Equal(StageStatus.Skipped, step.Status);
        Assert.Equal(ExecutionType.Deterministic, step.ExecutionType);
        Assert.Equal(WorkflowOutcome.Approved, x.Outcome);
        Assert.Null(x.Published!.SpokenSummary);
    }

    // ----- Authoritative facts -----

    [Fact]
    public async Task Agents_cannot_change_authoritative_metric_values()
    {
        var (runner, model) = Live();
        model.Respond = (role, prompt, _, _) => Task.FromResult(role == AgentRole.TacticalAnalyst
            ? Json(new TacticalAnalystResult(["Possession was 97% in the last window."], "Possession reached 97%.", null, null, "", [], []))
            : ScriptedChatClient.Good(role, prompt));
        var context = Context();
        var before = context.Fingerprint();
        var x = await Run(runner, context);

        Assert.True(x.AuthoritativeFactsUnchanged);
        Assert.Equal(before, x.AuthoritativeFingerprintAfter);
        Assert.Equal(before, context.Fingerprint());
        Assert.DoesNotContain("97", x.Published!.Inference ?? "");
        Assert.Equal(context.DeterministicNarrative.Inference, x.Published.Inference);
        Assert.Contains(Step(x, Stages.DeterministicValidation).Findings, f => f.StartsWith(IssueCodes.UnsupportedNumber));
    }

    [Fact]
    public void Insight_context_collections_are_immutable()
    {
        var context = Context();
        Assert.IsType<System.Collections.Immutable.ImmutableArray<MetricFact>>(context.DeterministicMetrics);
        Assert.IsType<System.Collections.Immutable.ImmutableArray<string>>(context.SupportingEventIds);
        Assert.False(context.GetType().GetProperties().Any(p => p.SetMethod is { IsPublic: true } && !p.SetMethod.ReturnParameter.GetRequiredCustomModifiers()
            .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))), "InsightContext exposes a mutable setter");
    }

    // ----- Deterministic validation -----

    [Fact]
    public void Deterministic_validation_catches_incorrect_numeric_claims_and_accepts_authoritative_ones()
    {
        var c = Context();
        var primary = c.DeterministicMetrics.First(m => m.IsPrimary);
        Assert.Empty(GroundingValidator.CheckText(c, "t", "f", $"{primary.Label} moved to {primary.DisplayCurrent}.", TextKind.Fact));
        var issues = GroundingValidator.CheckText(c, "t", "f", "They had 23 shots and 81.5% possession.", TextKind.Fact);
        Assert.Contains(issues, i => i.Code == IssueCodes.UnsupportedNumber && i.Message.Contains("23"));
        Assert.Contains(issues, i => i.Code == IssueCodes.UnsupportedNumber && i.Message.Contains("81.5"));
    }

    [Fact]
    public void Unknown_and_future_event_ids_are_rejected()
    {
        var c = Context();
        var future = Flagship.Events.First(e => e.MatchSecond > c.MatchSecond).Id;
        var earlierNonSupporting = Flagship.Events.First(e => e.MatchSecond < c.MatchSecond && !c.SupportingEventIds.Contains(e.Id)).Id;
        var issues = GroundingValidator.CheckEvidenceIds(c, "t", "evidenceIds", ["MS-FLAGSHIP-001-E99999", future, earlierNonSupporting, c.SupportingEventIds[0]]);
        Assert.Contains(issues, i => i.Code == IssueCodes.UnknownEvidenceId);
        Assert.Contains(issues, i => i.Code == IssueCodes.FutureEvidence && i.Message.Contains(future));
        Assert.Contains(issues, i => i.Code == IssueCodes.NotSupportingEvidence);
        Assert.Equal(3, issues.Count);
        Assert.Contains(GroundingValidator.CheckText(c, "t", "f", $"Then {future} sealed it.", TextKind.Narrative), i => i.Code == IssueCodes.FutureEvidence);
    }

    [Fact]
    public void Future_times_wrong_scores_and_invalid_players_are_rejected()
    {
        var c = Context(Goal);
        Assert.Contains(GroundingValidator.CheckText(c, "t", "f", $"In the {c.DisplayMinute + 5}' they scored again.", TextKind.Narrative),
            i => i.Code == IssueCodes.FutureTime);
        Assert.Contains(GroundingValidator.CheckText(c, "t", "f", "That made it 3-0.", TextKind.Narrative), i => i.Code == IssueCodes.WrongScore);
        Assert.DoesNotContain(GroundingValidator.CheckText(c, "t", "f", $"That made it {c.Score.Home}-{c.Score.Away}.", TextKind.Narrative),
            i => i.Code == IssueCodes.WrongScore);

        var outsider = c.AllMatchPlayers.First(p => c.Players.All(q => q.Id != p.Id) && c.Players.All(q => q.Name.Split(' ')[^1] != p.Name.Split(' ')[^1]));
        Assert.Contains(GroundingValidator.CheckText(c, "t", "f", $"{outsider.Name} drove it forward.", TextKind.Narrative), i => i.Code == IssueCodes.UnknownPlayer);
        Assert.DoesNotContain(GroundingValidator.CheckText(c, "t", "f", $"{c.Players[0].Name} was involved.", TextKind.Narrative), i => i.Code == IssueCodes.UnknownPlayer);
        Assert.Contains(GroundingValidator.CheckText(c, "t", "f", "Ravensmoor Rovers would approve.", TextKind.Narrative), i => i.Code == IssueCodes.ForeignTeam);
    }

    [Fact]
    public void Intent_certainty_and_causal_fact_wording_is_rejected()
    {
        var c = Context();
        Assert.Contains(GroundingValidator.CheckText(c, "t", "f", "The manager decided to switch flanks.", TextKind.Interpretation), i => i.Code == IssueCodes.IntentClaim);
        Assert.Contains(GroundingValidator.CheckText(c, "t", "f", "This definitely proves the press works.", TextKind.Interpretation), i => i.Code == IssueCodes.CertaintyClaim);
        Assert.Contains(GroundingValidator.CheckText(c, "t", "f", "They attacked right because the full-back tired.", TextKind.Fact), i => i.Code == IssueCodes.CausalFact);
        Assert.DoesNotContain(GroundingValidator.CheckText(c, "t", "f", "This might be because the full-back tired.", TextKind.Interpretation), i => i.Code == IssueCodes.CausalFact);
    }

    [Fact]
    public void Template_outputs_always_pass_deterministic_validation()
    {
        foreach (var insight in Insights)
        foreach (var mode in Enum.GetValues<ViewerMode>())
        foreach (var lang in new[] { "en", "fr", "es" })
        {
            var c = InsightContextBuilder.Build(insight, Flagship, new ViewerProfile(mode, lang), AccessibilityRequirements.All, "wf");
            var report = CandidateSanitizer.Validate(c, new NarrativeCandidate(TemplateOutputs.Tactical(c), TemplateOutputs.Audience(c), TemplateOutputs.Accessibility(c)));
            Assert.True(report.Clean, $"{insight.Id} {mode} {lang}: {string.Join("; ", report.Issues.Select(i => $"{i.Field} {i.Message}"))}");
        }
    }

    // ----- Verification and rejection -----

    [Fact]
    public async Task Verifier_rejection_triggers_one_constrained_repair_then_rejected_output_is_never_verified()
    {
        var (runner, model) = Live();
        model.Respond = (role, prompt, _, _) => Task.FromResult(role == AgentRole.Verification
            ? Json(new VerificationAgentResult("REJECTED", ["Unsupported tactical assertion."], ["The headline overstates the change."], [], [], null, []))
            : ScriptedChatClient.Good(role, prompt));
        var x = await Run(runner, Context());

        Assert.Equal(WorkflowOutcome.Rejected, x.Outcome);
        Assert.False(x.Verified);
        Assert.False(x.Published!.Verified);
        Assert.Null(x.Published.Headline);
        Assert.Null(x.Published.Inference);
        Assert.Equal(2, model.For(AgentRole.Verification).Count());
        Assert.Equal(2, model.For(AgentRole.AudienceNarrative).Count());
        var repair = JsonNode.Parse(model.For(AgentRole.AudienceNarrative).Last().Prompt)!;
        Assert.Contains("The headline overstates the change.", repair["problems"]!.ToJsonString());
        Assert.Single(x.Steps, s => s.Stage == Stages.Repair);
        Assert.Contains(x.RecoveryActions, r => r.Contains("constrained repair"));
    }

    [Fact]
    public async Task Verifier_approval_of_text_that_fails_deterministic_checks_is_overridden()
    {
        var (runner, model) = Live();
        model.Respond = (role, prompt, _, _) => Task.FromResult(role == AgentRole.AudienceNarrative
            ? Json(new AudienceNarrativeResult("Analyst", "Westhaven lead 4-1 after 23 shots", "They had 23 shots.", null, ""))
            : ScriptedChatClient.Good(role, prompt));
        var x = await Run(runner, Context());

        Assert.Equal(WorkflowOutcome.Rejected, x.Outcome);
        Assert.False(x.Published!.Verified);
        Assert.Contains("overridden", Step(x, Stages.FinalValidation).OutputSummary);
    }

    [Fact]
    public async Task Approved_with_changes_publishes_corrections_only_after_they_pass_deterministic_validation()
    {
        var (runner, model) = Live();
        var context = Context();
        model.Respond = (role, prompt, _, _) => Task.FromResult(role == AgentRole.Verification
            ? Json(new VerificationAgentResult("APPROVED_WITH_CHANGES", ["Soften the inference."], [], ["Hedge the inference."], [],
                new CorrectedNarrative("A measured shift in attack", context.DeterministicNarrative.Fact, "The evidence may suggest a shift.", null), []))
            : ScriptedChatClient.Good(role, prompt));
        var x = await Run(runner, context);
        Assert.Equal(WorkflowOutcome.ApprovedWithChanges, x.Outcome);
        Assert.True(x.Published!.Verified);
        Assert.Equal("A measured shift in attack", x.Published.Headline);

        var (runner2, model2) = Live();
        model2.Respond = (role, prompt, _, _) => Task.FromResult(role == AgentRole.Verification
            ? Json(new VerificationAgentResult("APPROVED_WITH_CHANGES", [], [], [], [], new CorrectedNarrative("Up 987 points", "987 more attacks", null, null), []))
            : ScriptedChatClient.Good(role, prompt));
        var y = await Run(runner2, Context());
        Assert.NotEqual(WorkflowOutcome.ApprovedWithChanges, y.Outcome);
        Assert.False(y.Verified);
    }

    // ----- Failure recovery -----

    [Fact]
    public async Task Tactical_timeout_is_retried_then_falls_back_to_the_deterministic_insight_without_calling_downstream_agents()
    {
        var (runner, model) = Live(new WorkflowLimits { AgentCallTimeout = TimeSpan.FromMilliseconds(150) });
        model.Respond = async (role, prompt, _, ct) =>
        {
            if (role == AgentRole.TacticalAnalyst) await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return ScriptedChatClient.Good(role, prompt);
        };
        var x = await Run(runner, Context());

        var tactical = Step(x, Stages.TacticalAnalyst);
        Assert.Equal(StageStatus.Fallback, tactical.Status);
        Assert.Equal(2, tactical.ModelCalls);
        Assert.Equal(1, tactical.RetryCount);
        Assert.All(tactical.AttemptFailures, f => Assert.Equal("Timeout", f));
        Assert.Equal(WorkflowOutcome.FallbackUnverified, x.Outcome);
        Assert.False(x.Verified);
        Assert.DoesNotContain(model.Calls, c => c.Role != AgentRole.TacticalAnalyst);
        Assert.Equal(StageStatus.Skipped, Step(x, Stages.Verification).Status);
    }

    [Fact]
    public async Task Throttling_and_malformed_output_are_retried_once_and_recovery_is_recorded()
    {
        var (runner, model) = Live();
        model.Respond = (role, prompt, attempt, _) => (role, attempt) switch
        {
            (AgentRole.TacticalAnalyst, 0) => throw new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests),
            (AgentRole.AudienceNarrative, 0) => Task.FromResult("this is not json"),
            _ => Task.FromResult(ScriptedChatClient.Good(role, prompt)),
        };
        var x = await Run(runner, Context());

        Assert.Equal(WorkflowOutcome.Approved, x.Outcome);
        Assert.Equal(["Throttled (HTTP 429)"], Step(x, Stages.TacticalAnalyst).AttemptFailures);
        Assert.Equal(1, Step(x, Stages.TacticalAnalyst).RetryCount);
        Assert.Equal(["MalformedOutput"], Step(x, Stages.AudienceNarrative).AttemptFailures);
        Assert.Contains(x.RecoveryActions, r => r.Contains("recovered after retry"));
        Assert.Equal(6, x.ModelCalls);
    }

    [Fact]
    public async Task Accessibility_failure_degrades_only_its_branch_and_does_not_corrupt_shared_state()
    {
        var (runner, model) = Live();
        model.Respond = (role, prompt, _, _) => role == AgentRole.AccessibilityNarrator
            ? throw new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable)
            : Task.FromResult(ScriptedChatClient.Good(role, prompt));
        var x = await Run(runner, Context());

        Assert.Equal(StageStatus.Degraded, Step(x, Stages.AccessibilityNarrator).Status);
        Assert.Equal(StageStatus.Completed, Step(x, Stages.AudienceNarrative).Status);
        Assert.Equal(WorkflowOutcome.Approved, x.Outcome);
        Assert.True(x.Published!.Verified);
        Assert.Null(x.Published.SpokenSummary);
        Assert.True(x.AuthoritativeFactsUnchanged);
        Assert.Null(JsonNode.Parse(model.For(AgentRole.Verification).Single().Prompt)!["accessibilityNarrativeResult"]);
    }

    [Fact]
    public async Task Verifier_failure_means_nothing_is_labelled_verified()
    {
        var (runner, model) = Live();
        model.Respond = (role, prompt, _, _) => role == AgentRole.Verification
            ? throw new HttpRequestException("auth", null, HttpStatusCode.Unauthorized)
            : Task.FromResult(ScriptedChatClient.Good(role, prompt));
        var x = await Run(runner, Context());
        Assert.Equal(WorkflowOutcome.FallbackUnverified, x.Outcome);
        Assert.False(x.Published!.Verified);
        Assert.Null(x.Published.Headline);
        Assert.Equal(["Authentication (HTTP 401)"], Step(x, Stages.Verification).AttemptFailures);
        Assert.Single(model.For(AgentRole.Verification));
    }

    [Fact]
    public async Task Model_call_budget_is_enforced_across_the_whole_execution()
    {
        var (runner, model) = Live(new WorkflowLimits { MaxModelCalls = 2 });
        var x = await Run(runner, Context());
        Assert.Equal(2, model.Calls.Count);
        Assert.False(x.Verified);
        Assert.Contains(x.Steps, s => s.AttemptFailures.Contains("BudgetExhausted"));
    }

    [Fact]
    public async Task Cancellation_discards_the_execution()
    {
        var (runner, model) = Live();
        model.Respond = async (role, prompt, _, ct) => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return ScriptedChatClient.Good(role, prompt); };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var x = await Run(runner, Context(), cts.Token);
        Assert.Equal(ExecutionStatus.Cancelled, x.Status);
        Assert.Null(x.Published);
        Assert.False(x.Verified);
    }

    // ----- Modes and safety of what is exposed -----

    [Fact]
    public async Task Offline_template_mode_is_clearly_not_foundry()
    {
        var runner = new InsightWorkflowRunner(new OfflineTemplateAgentFactory());
        var x = await Run(runner, Context());
        Assert.Equal(ExecutionMode.OfflineTemplate, x.ExecutionMode);
        Assert.Equal("offline-template", x.Provider);
        Assert.Null(x.ModelDeployment);
        Assert.Equal(0, x.ModelCalls);
        Assert.All(x.Steps, s => Assert.Equal(ExecutionType.Deterministic, s.ExecutionType));
        Assert.All(x.Steps, s => Assert.Null(s.ModelDeployment));
        Assert.Equal(WorkflowOutcome.DeterministicOnly, x.Outcome);
        Assert.False(x.Verified);
        Assert.Contains("no model was called", x.Published!.Notice);
        // The Agent Framework runtime still orchestrated the same graph.
        Assert.Equal(7, x.FrameworkSupersteps);
    }

    [Fact]
    public async Task Traces_never_expose_prompts_instructions_or_credentials()
    {
        var (runner, _) = Live();
        var json = JsonSerializer.Serialize(await Run(runner, Context()), AgentJson.Options);
        foreach (var d in MatchShiftAgents.All) Assert.DoesNotContain(d.Instructions[..60], json);
        Assert.DoesNotContain("ROLE:", json);
        Assert.DoesNotContain("insightContext", json);
        Assert.DoesNotContain("Bearer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api-key", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eyJ", json);
    }

    [Fact]
    public void Agent_framework_agents_are_real_chat_client_agents_with_distinct_roles()
    {
        var factory = new ChatClientAgentFactory(new ScriptedChatClient(), ExecutionMode.LiveFoundry, "microsoft-foundry", "gpt-test");
        var agents = MatchShiftAgents.All.Select(factory.Create).ToList();
        Assert.All(agents, a => Assert.IsType<ChatClientAgent>(a));
        Assert.Equal(4, agents.Select(a => a!.Name).Distinct().Count());
        Assert.Equal(4, MatchShiftAgents.All.Select(d => d.Responsibility).Distinct().Count());
    }
}
