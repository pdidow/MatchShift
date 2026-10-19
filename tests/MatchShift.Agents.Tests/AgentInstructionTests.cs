using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Analytics;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;

namespace MatchShift.Agents.Tests;

/// <summary>
/// The grounding rules every agent receives. Checks key phrases, not whole prompts, so wording can be tuned
/// without breaking tests while the rules themselves cannot silently disappear.
/// </summary>
public class AgentInstructionTests
{
    public static TheoryData<AgentRole> Roles => [AgentRole.TacticalAnalyst, AgentRole.AudienceNarrative, AgentRole.AccessibilityNarrator, AgentRole.Verification];

    private static bool Has(string text, string phrase) => text.Contains(phrase, StringComparison.OrdinalIgnoreCase);

    [Theory]
    [MemberData(nameof(Roles))]
    public void Every_agent_is_told_not_to_invent_or_infer_player_counts(AgentRole role)
    {
        var i = MatchShiftAgents.For(role).Instructions;
        Assert.True(Has(i, "PLAYER COUNTS"));
        Assert.True(Has(i, "never state or imply how many players"));
        Assert.True(Has(i, "8 key players"));
        Assert.True(Has(i, "do not count players"));
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void Every_agent_is_told_to_use_only_match_times_present_in_the_context(AgentRole role)
    {
        var i = MatchShiftAgents.For(role).Instructions;
        Assert.True(Has(i, "MATCH TIMES"));
        Assert.True(Has(i, "use only times that appear in insightContext"));
        Assert.True(Has(i, "never estimate, round or approximate a minute"));
    }

    [Fact]
    public void The_rules_are_one_shared_block_not_four_copies()
    {
        foreach (var d in MatchShiftAgents.All)
            Assert.Equal(1, CountOf(d.Instructions, MatchShiftAgents.GroundingRules));
        Assert.True(MatchShiftAgents.GroundingRules.Length < 900, "grounding rules should stay compact");
    }

    [Fact]
    public void Accessibility_spoken_and_replay_text_are_covered_explicitly()
    {
        var i = MatchShiftAgents.AccessibilityNarrator.Instructions;
        Assert.True(Has(i, "Spoken and replay text follow the same PLAYER COUNTS and MATCH TIMES rules"));
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void Every_agent_writes_in_the_viewer_language_including_fallback_phrases(AgentRole role)
    {
        var i = MatchShiftAgents.For(role).Instructions;
        Assert.True(Has(i, "Write all viewer-facing text in the language code insightContext.language"));
        Assert.True(Has(i, "'in the recent sequence', written in insightContext.language"));
    }

    [Fact]
    public void Accessibility_fallback_text_follows_the_selected_language()
    {
        var match = SyntheticMatchGenerator.GenerateFlagship().Match;
        var insight = LiveMatchAnalyzer.AnalyzeFull(match).Insights.First(i => i.Type == InsightType.AttackChannelShift);
        string Spoken(string lang) => TemplateOutputs.Accessibility(
            InsightContextBuilder.Build(insight, match, new ViewerProfile(ViewerMode.Analyst, lang), AccessibilityRequirements.All, "wf")).SpokenSummary;
        var (en, fr, es) = (Spoken("en"), Spoken("fr"), Spoken("es"));
        Assert.NotEqual(en, fr);
        Assert.NotEqual(en, es);
        Assert.NotEqual(fr, es);
        Assert.Equal("fr", InsightContextBuilder.Build(insight, match, new ViewerProfile(ViewerMode.Analyst, "fr"), AccessibilityRequirements.All, "wf").Language);
    }

    [Fact]
    public void Verifier_treats_player_counts_and_ungrounded_times_as_unsupported()
    {
        var i = MatchShiftAgents.Verifier.Instructions;
        Assert.True(Has(i, "Treat as unsupported, however plausible: any player count"));
        Assert.True(Has(i, "exact, approximate or inferred"));
        Assert.True(Has(i, "match time or minute not present in insightContext"));
        Assert.True(Has(i, "rounded or approximate times"));
    }

    [Fact]
    public void Live_agent_construction_sends_the_grounding_rules_to_the_model()
    {
        // Options are what FoundryAgentFactory passes to AIProjectClient.AsAIAgent in LIVE_FOUNDRY.
        foreach (var d in MatchShiftAgents.All)
            Assert.Contains(MatchShiftAgents.GroundingRules, MatchShiftAgents.Options(d, "gpt-4.1-mini").ChatOptions!.Instructions);
    }

    [Fact]
    public async Task Every_agent_call_in_a_live_mode_workflow_carries_the_grounding_rules()
    {
        var match = SyntheticMatchGenerator.GenerateFlagship().Match;
        var insight = LiveMatchAnalyzer.AnalyzeFull(match).Insights.First(i => i.Type == InsightType.AttackChannelShift);
        var context = InsightContextBuilder.Build(insight, match, new ViewerProfile(), AccessibilityRequirements.All, "wf-test");
        var model = new ScriptedChatClient();
        var runner = new InsightWorkflowRunner(new ChatClientAgentFactory(model, ExecutionMode.LiveFoundry, "microsoft-foundry", "gpt-test"));
        await runner.RunAsync(context, runner.CreateRecorder(context), CancellationToken.None);

        Assert.Equal(4, model.Calls.Count);
        Assert.All(model.Calls, c => Assert.Contains(MatchShiftAgents.GroundingRules, c.Instructions));
    }

    private static int CountOf(string text, string part)
    {
        var n = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}
