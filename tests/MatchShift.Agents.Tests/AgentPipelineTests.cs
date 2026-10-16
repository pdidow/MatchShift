using System.Net;
using System.Text;
using System.Text.Json;
using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
using MatchShift.Analytics;
using MatchShift.Analytics.Story;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.Infrastructure.Foundry;
using MatchShift.SyntheticData;
using Microsoft.Extensions.Options;

namespace MatchShift.Agents.Tests;

public class AgentPipelineTests
{
    private static readonly Match Flagship = SyntheticMatchGenerator.GenerateFlagship().Match;
    private static readonly LiveMatchAnalyzer Analysis = LiveMatchAnalyzer.AnalyzeFull(Flagship);
    private static readonly NarrativeContext Context = new(new MatchRoster(Flagship), new ViewerProfile(ViewerMode.Analyst, "en"));

    private static Insight Shift => Analysis.Insights.First(i => i.Type == InsightType.AttackChannelShift);

    [Fact]
    public void Event_analyst_recomputes_every_cited_metric_from_raw_events()
    {
        var checkedFacts = 0;
        foreach (var insight in Analysis.Insights)
        {
            var facts = AgentPipeline.EventAnalyst(insight, Flagship);
            Assert.All(facts, f => Assert.True(f.Matches, $"{insight.Id} {f.Metric}: cited {f.Cited}, recomputed {f.Recomputed}"));
            checkedFacts += facts.Count;
        }
        Assert.True(checkedFacts > 20, $"only {checkedFacts} facts re-derived");
    }

    [Fact]
    public async Task Template_pipeline_is_approved_end_to_end()
    {
        var pipeline = new AgentPipeline(new NarrationService(new TemplateInsightProvider()), new VerificationAgent());
        var trace = await pipeline.RunAsync(Shift, Flagship, Context);
        Assert.Equal(6, trace.Steps.Count);
        Assert.Equal(AgentStatus.Approved, trace.Steps[^1].Status);
        Assert.DoesNotContain(trace.Steps, s => s.Status == AgentStatus.Rejected);
        Assert.Equal("template", trace.Narrative.Source);
    }

    [Fact]
    public async Task Ai_text_that_invents_numbers_is_rejected_and_replaced()
    {
        var narration = new NarrationService(new LyingProvider());
        var narrative = await narration.InsightAsync(Shift, Context);
        Assert.Equal("template", narrative.Source);
        Assert.Contains("rejected", narrative.VerificationNote);
        Assert.DoesNotContain("75%", narrative.Fact);
    }

    [Fact]
    public async Task Ai_text_that_only_rephrases_verified_facts_is_accepted()
    {
        var narration = new NarrationService(new FaithfulProvider());
        var narrative = await narration.InsightAsync(Shift, Context);
        Assert.Equal("faithful", narrative.Source);
        Assert.StartsWith("approved", narrative.VerificationNote);
    }

    [Fact]
    public async Task Ai_outage_falls_back_to_the_template()
    {
        var narration = new NarrationService(new BrokenProvider());
        var narrative = await narration.InsightAsync(Shift, Context);
        Assert.Equal("template", narrative.Source);
        Assert.Contains("unavailable", narrative.VerificationNote);

        var facts = RecapBuilder.Build(Analysis, RecapPeriod.FullTime);
        var recap = await narration.RecapAsync(facts, RecapStyle.Casual, Context, Analysis.Insights);
        Assert.Equal("template", recap.Source);
        Assert.Contains("2-2", recap.Text);
    }

    [Fact]
    public async Task Foundry_provider_sends_facts_and_parses_json()
    {
        var handler = new FakeHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().Result;
            Assert.Contains("\"model\":\"matchshift-gpt\"", body);
            Assert.Contains("json_object", body);
            if (body.Contains("catalogue")) Assert.Contains("90%", body); // verified narrative is supplied to the interpreter
            Assert.Equal("secret", request.Headers.GetValues("api-key").Single());
            using var requestBody = JsonDocument.Parse(body);
            var messages = requestBody.RootElement.GetProperty("messages");
            var content = new MockChatCompletionClient().CompleteJsonAsync(
                messages[0].GetProperty("content").GetString()!, messages[1].GetProperty("content").GetString()!, default).GetAwaiter().GetResult();
            return JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } });
        });
        var options = Options.Create(new FoundryOptions { Enabled = true, Endpoint = "https://example.test/openai/v1", Deployment = "matchshift-gpt", ApiKey = "secret" });
        var provider = new FoundryInsightProvider(new FoundryChatClient(new HttpClient(handler), options));

        var narration = new NarrationService(provider);
        var narrative = await narration.InsightAsync(Shift, Context);
        Assert.Equal("microsoft-foundry", narrative.Source);
        Assert.Equal(TemplateInsightProvider.Narrate(Shift, Context).Ticker, narrative.Headline);
        Assert.StartsWith("approved", narrative.VerificationNote);
        Assert.Equal("https://example.test/openai/v1/chat/completions", handler.LastUri);
    }

    [Fact]
    public async Task Unsupported_numbers_in_recap_headings_are_rejected()
    {
        var narration = new NarrationService(new MisleadingHeadingProvider());
        var facts = RecapBuilder.Build(Analysis, RecapPeriod.FullTime);
        var recap = await narration.RecapAsync(facts, RecapStyle.Casual, Context, Analysis.Insights);

        Assert.Equal("template", recap.Source);
        Assert.Contains("AI recap rejected", recap.VerificationNote);
        Assert.DoesNotContain("9999", recap.Text);
    }

    private sealed class MisleadingHeadingProvider : IAiInsightProvider
    {
        public string Name => "misleading-heading";
        public Task<InsightNarrative> NarrateInsightAsync(Insight i, NarrativeContext c, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<RecapNarrative> NarrateRecapAsync(RecapFacts r, RecapStyle s, NarrativeContext c, IReadOnlyList<Insight> i, CancellationToken ct = default)
        {
            var template = RecapNarrator.Narrate(r, s, c, i);
            return Task.FromResult(template with
            {
                Source = Name,
                Sections = template.Sections.Select(section => section with { Heading = "9999 goals" }).ToList(),
            });
        }
    }

    private sealed class LyingProvider : IAiInsightProvider
    {
        public string Name => "liar";
        public Task<InsightNarrative> NarrateInsightAsync(Insight insight, NarrativeContext context, CancellationToken ct = default) =>
            Task.FromResult(TemplateInsightProvider.Narrate(insight, context) with
            {
                Fact = "75% of Westhaven's attacks went down the right because the manager decided to target the full-back.",
                Source = Name,
            });
        public Task<RecapNarrative> NarrateRecapAsync(RecapFacts r, RecapStyle s, NarrativeContext c, IReadOnlyList<Insight> i, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FaithfulProvider : IAiInsightProvider
    {
        public string Name => "faithful";
        public Task<InsightNarrative> NarrateInsightAsync(Insight insight, NarrativeContext context, CancellationToken ct = default)
        {
            return Task.FromResult(NarrativeSelection.For(insight, "brief").Render(insight, context, Name));
        }
        public Task<RecapNarrative> NarrateRecapAsync(RecapFacts r, RecapStyle s, NarrativeContext c, IReadOnlyList<Insight> i, CancellationToken ct = default) =>
            Task.FromResult(RecapNarrator.Narrate(r, s, c, i) with { Source = Name });
    }

    private sealed class BrokenProvider : IAiInsightProvider
    {
        public string Name => "broken";
        public Task<InsightNarrative> NarrateInsightAsync(Insight insight, NarrativeContext context, CancellationToken ct = default) =>
            throw new HttpRequestException("503");
        public Task<RecapNarrative> NarrateRecapAsync(RecapFacts r, RecapStyle s, NarrativeContext c, IReadOnlyList<Insight> i, CancellationToken ct = default) =>
            throw new HttpRequestException("503");
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public string? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json"),
            });
        }
    }
}
