using System.Text.Json;
using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Analytics;
using MatchShift.Core.Insights;
using MatchShift.Infrastructure;
using MatchShift.Infrastructure.Foundry;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Clubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MatchShift.Cli;

/// <summary>
/// `agent-workflow`: runs the Microsoft Agent Framework insight workflow for selected flagship insights and prints the trace.
/// With FOUNDRY_PROJECT_ENDPOINT and AZURE_AI_MODEL_DEPLOYMENT_NAME set this makes REAL, paid Foundry calls (about four per insight);
/// pass --offline to force OFFLINE_TEMPLATE. --out writes the executions (safe trace data only) as JSON.
/// </summary>
public static class AgentWorkflowCli
{
    public static async Task Run(Dictionary<string, string> options)
    {
        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentWorkflow:Mode"] = options.ContainsKey("offline") ? "OfflineTemplate" : options.GetValueOrDefault("mode-exec", "Auto"),
                ["AgentWorkflow:Credential"] = options.GetValueOrDefault("credential", "AzureCli"),
            })
            .Build();
        var services = new ServiceCollection().AddMatchShiftAgentWorkflow(config).BuildServiceProvider();
        var runner = services.GetRequiredService<InsightWorkflowRunner>();
        var runtime = services.GetRequiredService<AgentRuntimeInfo>();
        Console.WriteLine($"Execution mode: {runtime.ExecutionMode}  provider: {runtime.Provider}  deployment: {runtime.ModelDeployment ?? "-"}  " +
                          $"endpoint host: {runtime.ProjectEndpointHost ?? "-"}  credential: {runtime.Credential ?? "-"}");
        if (runtime.ConfigurationNote is not null) Console.WriteLine($"Note: {runtime.ConfigurationNote}");

        var match = SyntheticMatchGenerator.GenerateFlagship().Match;
        var insights = LiveMatchAnalyzer.AnalyzeFull(match).Insights;
        var count = int.Parse(options.GetValueOrDefault("count", "1"));
        var selected = options.TryGetValue("insight", out var id)
            ? insights.Where(i => i.Id == id).ToList()
            : options.TryGetValue("type", out var type)
                ? insights.Where(i => i.Type.Code() == type.ToUpperInvariant()).Take(count).ToList()
                : insights.OrderByDescending(i => i.Importance).Take(count).ToList();
        if (selected.Count == 0) throw new ArgumentException("No matching insight.");

        var mode = Enum.Parse<ViewerMode>(options.GetValueOrDefault("audience", "Analyst"), true);
        var profile = new ViewerProfile(mode, options.GetValueOrDefault("lang", "en"));
        var accessibility = options.GetValueOrDefault("a11y", "true") != "false";
        var foreign = ClubCatalog.DatasetClubs.SelectMany(c => new[] { c.Name, c.ShortName });
        var executions = new List<WorkflowExecution>();
        foreach (var insight in selected)
        {
            var context = InsightContextBuilder.Build(insight, match, profile,
                accessibility ? AccessibilityRequirements.All : AccessibilityRequirements.None, $"wf-cli-{Guid.NewGuid():N}"[..20], foreign);
            var execution = await runner.RunAsync(context, runner.CreateRecorder(context), CancellationToken.None);
            executions.Add(execution);
            Print(execution);
        }

        if (options.TryGetValue("out", out var path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(executions, new JsonSerializerOptions(AgentJson.Options) { WriteIndented = true }));
            Console.WriteLine($"Wrote {executions.Count} execution(s) to {path}");
        }

        // Static-demo recording: the web client relabels every entry RECORDED_DEMO and keeps the original mode in recordedFrom.
        if (options.TryGetValue("recording", out var recording))
        {
            var completed = executions.Where(x => x.Status == ExecutionStatus.Completed).ToList();
            var file = new
            {
                recordedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"),
                note = $"Recorded from {runtime.ExecutionMode} ({runtime.ModelDeployment ?? "no model"}); audience {mode}, language {profile.Language}. Replays only; nothing runs live in the static demo.",
                executions = completed.ToDictionary(x => x.InsightId, x => x with { RecordedFrom = $"{x.ExecutionMode} via {runtime.Provider}" }),
            };
            await File.WriteAllTextAsync(recording, JsonSerializer.Serialize(file, AgentJson.Options));
            Console.WriteLine($"Wrote {completed.Count} recording(s) to {recording}");
        }
    }

    private static void Print(WorkflowExecution x)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {x.WorkflowExecutionId}  insight {x.InsightId}  {x.ExecutionMode}  status {x.Status}  outcome {x.Outcome}  verified {x.Verified}");
        Console.WriteLine($"    orchestrator: {x.Orchestrator}");
        Console.WriteLine($"    framework supersteps {x.FrameworkSupersteps}, executor invocations {x.FrameworkExecutorInvocations}, model calls {x.ModelCalls}/{x.ModelCallLimit}, " +
                          $"tokens in {x.InputTokens} out {x.OutputTokens}, {x.DurationMs} ms");
        foreach (var s in x.Steps)
        {
            Console.WriteLine($"  [{s.Sequence}] step {s.Superstep?.ToString() ?? "?"}  {s.Stage,-25} {s.ExecutionType,-24} {s.Status,-9} {s.DurationMs,6} ms  calls {s.ModelCalls} retries {s.RetryCount}" +
                              $"  {s.StartedAt:HH:mm:ss.fff}-{s.CompletedAt:HH:mm:ss.fff}");
            Console.WriteLine($"        {s.OutputSummary}");
            if (s.VerificationDecision is not null) Console.WriteLine($"        decision: {s.VerificationDecision}");
            foreach (var f in s.Findings) Console.WriteLine($"        - {f}");
            if (s.RecoveryAction is not null) Console.WriteLine($"        recovery: {s.RecoveryAction}");
        }
        if (x.Concurrency is { } c) Console.WriteLine($"    concurrency: same superstep {c.SameSuperstep}, overlapped {c.Overlapped} ({c.OverlapMs} ms)");
        Console.WriteLine($"    handoffs: {string.Join("  ", x.Handoffs.Select(h => $"{h.From}->{string.Join("+", h.To)}"))}");
        Console.WriteLine($"    authoritative facts unchanged: {x.AuthoritativeFactsUnchanged} ({x.AuthoritativeFingerprintBefore} / {x.AuthoritativeFingerprintAfter})");
        if (x.Published is { } p)
        {
            Console.WriteLine($"    published (verified={p.Verified}): {p.Notice}");
            if (p.Verified)
            {
                Console.WriteLine($"      headline: {p.Headline}");
                Console.WriteLine($"      summary: {p.Summary}");
                Console.WriteLine($"      inference: {p.Inference}");
                Console.WriteLine($"      possible explanation: {p.PossibleExplanation}");
                Console.WriteLine($"      spoken: {p.SpokenSummary}");
                Console.WriteLine($"      visual: {p.VisualDescription}");
                Console.WriteLine($"      evidence: {string.Join(", ", p.EvidenceIds)}");
            }
        }
        if (x.Error is not null) Console.WriteLine($"    error: {x.Error}");
    }
}
