using MatchShift.Evaluation;
using MatchShift.Infrastructure.Foundry;

namespace MatchShift.Cli;

/// <summary>
/// `foundry-eval`: developer-triggered quality evaluation. Without --confirm true it only prints the cost estimate.
/// --phase generate | evaluate | all | fetch | render (default all). fetch --eval ID --run ID re-reads a completed Foundry run
/// (GET only, no model call); render rebuilds the reports from latest.json without any call. generate makes LIVE_FOUNDRY workflow runs and writes the dataset;
/// evaluate submits the dataset to Microsoft Foundry cloud evaluation and writes the report.
/// Uses Microsoft Entra ID (Azure CLI credential by default); never an API key.
/// </summary>
public static class FoundryEvalCli
{
    public static async Task Run(Dictionary<string, string> options)
    {
        var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT");
        var deployment = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME") ?? "gpt-4.1-mini";
        var outDir = options.GetValueOrDefault("out", "artifacts/evaluations");
        var docs = options.GetValueOrDefault("docs", "docs/evaluations");
        if (options.GetValueOrDefault("phase") == "render")
        {
            // Re-renders the reports from the saved latest.json. No model or Azure call.
            var saved = System.Text.Json.JsonSerializer.Deserialize<EvaluationReport>(await File.ReadAllTextAsync(Path.Combine(outDir, "latest.json")), EvalJson.Pretty)!;
            saved = saved with { Foundry = saved.Foundry is { } f ? f with { ReportUrl = Redact.ReportUrl(f.ReportUrl), ResultCounts = Redact.Counts(f.ResultCounts) } : null };
            await WriteAsync(saved, outDir, docs);
            return;
        }
        Console.WriteLine(EvaluationRunner.EstimateBlock(deployment));
        if (options.GetValueOrDefault("confirm") != "true")
        {
            Console.WriteLine("Estimate only. Re-run with --confirm true to make the model calls.");
            return;
        }
        if (string.IsNullOrWhiteSpace(endpoint)) throw new InvalidOperationException("Set FOUNDRY_PROJECT_ENDPOINT (and optionally AZURE_AI_MODEL_DEPLOYMENT_NAME).");

        var project = new Uri(endpoint);
        var credential = FoundryAgentFactory.CreateCredential(options.GetValueOrDefault("credential", "AzureCli"));
        var phase = options.GetValueOrDefault("phase", "all");
        var dataset = Path.Combine(outDir, "dataset.jsonl");
        var runner = new EvaluationRunner(project, deployment, credential, Console.WriteLine);

        var cases = phase is "all" or "generate"
            ? await runner.GenerateAsync(dataset, CancellationToken.None)
            : EvalJson.FromJsonl(await File.ReadAllTextAsync(dataset));
        if (phase == "generate") return;

        var (run, scores) = phase == "fetch"
            ? await runner.FetchAsync(options["eval"], options["run"], cases.Count, CancellationToken.None)
            : await runner.EvaluateAsync(cases, CancellationToken.None);
        Console.WriteLine($"Foundry run {run.RunId}: {run.Status}; {scores.Count} scores; report: {run.ReportUrl ?? "-"}");
        var callsFile = Path.ChangeExtension(dataset, ".calls");
        var calls = File.Exists(callsFile) ? int.Parse(await File.ReadAllTextAsync(callsFile)) : 0;
        var report = EvaluationReport.Build(cases, scores, run, deployment, project.Host, calls, EvaluationRunner.EstimateText(), DateTimeOffset.UtcNow);
        await WriteAsync(report, outDir, docs);
    }

    private static async Task WriteAsync(EvaluationReport report, string outDir, string docs)
    {
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(docs);
        await File.WriteAllTextAsync(Path.Combine(outDir, "latest.json"), ReportWriter.Json(report));
        await File.WriteAllTextAsync(Path.Combine(docs, "latest.md"), ReportWriter.Markdown(report));
        Console.WriteLine($"Wrote {Path.Combine(outDir, "latest.json")} and {Path.Combine(docs, "latest.md")}");
    }
}
