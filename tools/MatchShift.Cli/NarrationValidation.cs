using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Identity;
using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
using MatchShift.Analytics;
using MatchShift.Analytics.Story;
using MatchShift.Core.Insights;
using MatchShift.Infrastructure.Foundry;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Scenarios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MatchShift.Cli;

public static class NarrationValidation
{
    public static async Task Run(string command, Dictionary<string, string> options)
    {
        var provider = options.GetValueOrDefault("provider", "mock");
        if (provider is not ("mock" or "foundry")) throw new ArgumentException("provider must be mock or foundry");
        using var http = new HttpClient();
        var settings = new ConfigurationBuilder().AddEnvironmentVariables().Build().GetSection("Foundry").Get<FoundryOptions>() ?? new();
        if (provider == "foundry" && !settings.IsConfigured)
            throw new InvalidOperationException("Foundry is not configured. Use --provider mock for credential-free validation. See docs/FOUNDRY.md.");
        IChatCompletionClient Inner() => provider == "mock" ? new MockChatCompletionClient() :
            new FoundryChatClient(http, Options.Create(settings), string.IsNullOrWhiteSpace(settings.ApiKey) ? new DefaultAzureCredential() : null);
        var source = provider == "mock" ? "mock" : "microsoft-foundry";
        object result;
        if (command == "verify-ai")
        {
            var match = SyntheticMatchGenerator.GenerateFlagship().Match;
            var analysis = LiveMatchAnalyzer.AnalyzeFull(match);
            var insight = analysis.Insights.First(i => i.Type == InsightType.AttackChannelShift);
            var context = new NarrativeContext(new MatchRoster(match), ViewerProfile.Default);
            var client = new MeteredClient(Inner(), false);
            var narration = new NarrationService(new FoundryInsightProvider(client, source));
            var sw = Stopwatch.StartNew();
            var narrative = await narration.InsightAsync(insight, context);
            var recap = await narration.RecapAsync(RecapBuilder.Build(analysis, RecapPeriod.FullTime), RecapStyle.Casual, context, analysis.Insights);
            result = new { provider, liveModel = provider == "foundry", promptVersion = FoundryInsightProvider.PromptVersion,
                elapsedMs = sw.ElapsedMilliseconds, client.Calls, client.InputCharacters, client.OutputCharacters,
                insightSource = narrative.Source, narrative.VerificationNote, recapSource = recap.Source,
                recapVerification = recap.VerificationNote, passed = narrative.Source == source && recap.Source == source };
        }
        else
        {
            var count = Math.Clamp(int.Parse(options.GetValueOrDefault("matches", "8")), 1, 100);
            var seed = long.Parse(options.GetValueOrDefault("seed", "81000"));
            var cases = new List<object>();
            for (var n = 0; n < count; n++)
            {
                var match = SyntheticMatchGenerator.GenerateScenario(ScenarioKind.AttackChannelShift, seed + n).Match;
                var insight = LiveMatchAnalyzer.AnalyzeFull(match).Insights.First(i => i.Evidence.Count > 0 && i.TeamId is not null);
                var context = new NarrativeContext(new MatchRoster(match), ViewerProfile.Default);
                EvidenceGate.Require(insight, match); // Warm the shared deterministic gate equally for both workflows.
                foreach (var inject in new[] { false, true })
                foreach (var collaborative in new[] { false, true })
                {
                    var client = new MeteredClient(Inner(), inject);
                    var service = new NarrationService(new FoundryInsightProvider(client, source, collaborative));
                    var sw = Stopwatch.StartNew();
                    var narrative = await service.InsightAsync(insight, context);
                    cases.Add(new { seed = seed + n, insight.Id, injectedFault = inject ? "wrong_team" : "none",
                        workflow = collaborative ? "collaborative" : "single", elapsedMs = sw.ElapsedMilliseconds,
                        client.Calls, client.InputCharacters, client.OutputCharacters,
                        acceptedEditorialSelection = narrative.Source == source,
                        templateFallback = narrative.Source == "template",
                        unsupportedPublished = narrative.Fact != TemplateInsightProvider.Narrate(insight, context).Fact ||
                            narrative.Selection is { } selection && !selection.Matches(insight),
                        explanationRetained = narrative.Explanation is not null,
                        revisions = narrative.Collaboration.Count(s => s.Agent == "Interpreter revision") });
                }
            }
            result = new { provider, liveModel = provider == "foundry", promptVersion = FoundryInsightProvider.PromptVersion,
                seed, matches = count, billableCostUsd = provider == "mock" ? (double?)0 : null,
                tokenUsage = (int?)null, humanUsefulnessRating = (double?)null,
                limitations = "Mock timing includes simulated delays; characters are not tokens. Explanation retention is a proxy, not a human quality score. No model-superiority claim.", cases };
        }
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (options.TryGetValue("out", out var path))
        { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); await File.WriteAllTextAsync(path, json); Console.WriteLine($"Wrote {path}"); }
        else Console.WriteLine(json);
    }

    private sealed class MeteredClient(IChatCompletionClient inner, bool inject) : IChatCompletionClient
    {
        public int Calls { get; private set; }
        public long InputCharacters { get; private set; }
        public long OutputCharacters { get; private set; }
        public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            Calls++; InputCharacters += systemPrompt.Length + userPrompt.Length;
            var result = await inner.CompleteJsonAsync(systemPrompt, userPrompt, cancellationToken);
            if (inject && systemPrompt.StartsWith("MatchShift Interpreter.", StringComparison.Ordinal))
            {
                var node = JsonNode.Parse(result)!;
                if (node["selection"] is { } selection) selection["teamId"] = "WRONG-TEAM";
                result = node.ToJsonString();
            }
            OutputCharacters += result.Length;
            return result;
        }
    }
}
