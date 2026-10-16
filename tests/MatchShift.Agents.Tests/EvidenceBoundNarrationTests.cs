using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
using MatchShift.Analytics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.Infrastructure.Foundry;
using MatchShift.SyntheticData;

namespace MatchShift.Agents.Tests;

public class EvidenceBoundNarrationTests
{
    private static readonly Match Match = SyntheticMatchGenerator.GenerateFlagship().Match;
    private static readonly Insight Shift = LiveMatchAnalyzer.AnalyzeFull(Match).Insights.First(i => i.Type == InsightType.AttackChannelShift);
    private static readonly NarrativeContext Context = new(new MatchRoster(Match), ViewerProfile.Default);

    [Theory]
    [InlineData("team")]
    [InlineData("player")]
    [InlineData("swapped")]
    [InlineData("window")]
    [InlineData("missing_reference")]
    [InlineData("cause")]
    public async Task Unsupported_bindings_and_prose_cannot_reach_the_viewer(string attack)
    {
        var service = new NarrationService(new TamperedProvider(attack));
        var actual = await service.InsightAsync(Shift, Context);
        Assert.Equal("template", actual.Source);
        Assert.Equal(TemplateInsightProvider.Narrate(Shift, Context).Fact, actual.Fact);
        Assert.DoesNotContain("manager decided", actual.Sentences());
        Assert.Contains("rejected", actual.VerificationNote);
    }

    [Fact]
    public async Task Corrupt_underlying_evidence_is_withheld_before_a_provider_call()
    {
        var provider = new TamperedProvider("team");
        var corrupt = Shift with { Evidence = Shift.Evidence.Select(e => e with { RecentValue = 999 }).ToArray() };
        Assert.False(EvidenceGate.Check(corrupt, Match).Approved);
        await Assert.ThrowsAsync<InvalidDataException>(() => new NarrationService(provider).InsightAsync(corrupt, Context));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Mock_runs_the_production_protocol_but_does_not_claim_live_AI()
    {
        var service = new NarrationService(new FoundryInsightProvider(new MockChatCompletionClient(), "mock"));
        Assert.False(service.UsesAi);
        var narrative = await service.InsightAsync(Shift, Context);
        Assert.Equal("mock", narrative.Source);
        Assert.Equal(new[] { "Interpreter", "Skeptic", "Editor" }, narrative.Collaboration.Select(s => s.Agent));
        Assert.Equal(AgentStatus.Approved, narrative.Collaboration[1].Status);
        var analysis = LiveMatchAnalyzer.AnalyzeFull(Match);
        var facts = MatchShift.Analytics.Story.RecapBuilder.Build(analysis, RecapPeriod.FullTime);
        var recap = await service.RecapAsync(facts, RecapStyle.Casual, Context, analysis.Insights);
        Assert.Equal("mock", recap.Source);
        Assert.Contains("2-2", recap.Text);
    }

    [Fact]
    public async Task Timeout_preserves_verified_template_and_caller_cancellation_propagates()
    {
        var service = new NarrationService(new FoundryInsightProvider(new SlowClient(), "mock"), timeout: TimeSpan.FromMilliseconds(20));
        var narrative = await service.InsightAsync(Shift, Context);
        Assert.Equal("template", narrative.Source);
        Assert.Contains("unavailable", narrative.VerificationNote);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InsightAsync(Shift, Context, cancel.Token));
    }

    private sealed class SlowClient : IChatCompletionClient
    {
        public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        { await Task.Delay(10000, cancellationToken); return "{}"; }
    }

    [Fact]
    public async Task Repeated_skeptic_rejection_stops_after_one_repair_and_retains_the_trace()
    {
        var client = new RejectingSkeptic();
        var result = await new NarrationService(new FoundryInsightProvider(client, "mock")).InsightAsync(Shift, Context);
        Assert.Equal("template", result.Source);
        Assert.Equal(2, client.Reviews);
        Assert.Single(result.Collaboration, s => s.Agent == "Interpreter revision");
        Assert.DoesNotContain(result.Collaboration, s => s.Agent == "Editor");
        Assert.Equal(AgentStatus.Rejected, result.Collaboration[^1].Status);
    }

    private sealed class RejectingSkeptic : IChatCompletionClient
    {
        public int Reviews { get; private set; }
        public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            if (systemPrompt.StartsWith("MatchShift Skeptic.", StringComparison.Ordinal))
            { Reviews++; return Task.FromResult("{\"approved\":false,\"issues\":[\"review_rejected\"]}"); }
            return new MockChatCompletionClient().CompleteJsonAsync(systemPrompt, userPrompt, cancellationToken);
        }
    }

    private sealed class TamperedProvider(string attack) : IAiInsightProvider
    {
        public int Calls { get; private set; }
        public string Name => "tampered";
        public Task<InsightNarrative> NarrateInsightAsync(Insight i, NarrativeContext c, CancellationToken cancellationToken = default)
        {
            Calls++;
            var claim = NarrativeSelection.For(i);
            var text = claim.Render(i, c, Name);
            return Task.FromResult(attack switch
            {
                "team" => text with { Selection = claim with { TeamId = c.Roster.Match.Opponent(i.TeamId!).Id } },
                "player" => text with { Selection = claim with { PlayerId = "INVENTED-99" } },
                "swapped" => text with { Selection = claim with { Baseline = claim.Recent, Recent = claim.Baseline!.Value } },
                "window" => text with { Selection = claim with { RecentWindow = new TimeSpanRef(0, 60) } },
                "missing_reference" => text with { Selection = claim with { EventIds = [] } },
                _ => text with { Explanation = "The manager decided to target the defender." },
            });
        }
        public Task<RecapNarrative> NarrateRecapAsync(RecapFacts r, RecapStyle s, NarrativeContext c, IReadOnlyList<Insight> i, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
