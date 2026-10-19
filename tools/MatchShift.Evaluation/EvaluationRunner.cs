using System.Text.Json;
using Azure.Core;

namespace MatchShift.Evaluation;

public static partial class Redact
{
    /// <summary>
    /// The portal report link encodes the subscription in its first path segment after /r/ (before the resource group).
    /// It is replaced so exported reports never carry a subscription identifier; the rest of the path still locates the run.
    /// </summary>
    public static string? ReportUrl(string? url) => url is null ? null : SubscriptionSegment().Replace(url, "/r/<subscription>,");

    /// <summary>"total 39, passed 35, ..." from a result_counts object, whether given as JSON text or already compact.</summary>
    public static string? Counts(string? counts)
    {
        if (counts is null || !counts.TrimStart().StartsWith('{')) return counts;
        using var doc = JsonDocument.Parse(counts);
        return string.Join(", ", doc.RootElement.EnumerateObject().Select(p => $"{p.Name} {p.Value}"));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"/r/[^,/]+,")]
    private static partial System.Text.RegularExpressions.Regex SubscriptionSegment();
}

/// <summary>
/// The developer-triggered evaluation pipeline: generate the dataset from live workflow runs, then score it with
/// Microsoft Foundry cloud evaluation. Never scheduled, never run on user traffic.
/// </summary>
public sealed class EvaluationRunner(Uri projectEndpoint, string deployment, TokenCredential credential, Action<string> log)
{
    public static string EstimateText()
    {
        var e = DatasetGenerator.Estimate();
        return $"{e.FoundryRows} rows; {e.FoundryJudgeCalls} Foundry judge calls; about {e.Total} model calls in total (gpt-4.1-mini)";
    }

    public static string EstimateBlock(string deployment)
    {
        var e = DatasetGenerator.Estimate();
        return $"""
            DATASET SIZE: {e.FoundryRows} rows ({DatasetGenerator.GoodSpecs.Count * 2} good from {e.Workflows} live workflow runs, 11 adversarial)
            EVALUATORS: Foundry builtin.groundedness, builtin.relevance, builtin.coherence (judge: {deployment}); MatchShift deterministic validator (no model); Verification Agent (independent)
            ESTIMATED EVALUATOR CALLS: {e.FoundryJudgeCalls} Foundry judge calls + {e.VerifierCalls} independent verifier calls
            ESTIMATED TOTAL MODEL CALLS: about {e.Total} (up to {e.Workflows} x {DatasetGenerator.EstimatedCallsPerWorkflow} workflow agent calls + {e.VerifierCalls} + {e.FoundryJudgeCalls})
            EXPECTED SCALE: small, one-off; roughly 0.5-1M gpt-4.1-mini tokens, well under 1 USD at pay-per-token rates
            """;
    }

    public async Task<IReadOnlyList<EvalCase>> GenerateAsync(string datasetPath, CancellationToken ct)
    {
        var generator = new DatasetGenerator(projectEndpoint, deployment, credential, log);
        var cases = await generator.GenerateAsync(ct);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(datasetPath))!);
        await File.WriteAllTextAsync(datasetPath, EvalJson.ToJsonl(cases), ct);
        await File.WriteAllTextAsync(Path.ChangeExtension(datasetPath, ".calls"), generator.ModelCalls.ToString(), ct);
        log($"Dataset: {cases.Count} rows written to {datasetPath}; generation model calls: {generator.ModelCalls}");
        return cases;
    }

    /// <summary>Re-reads a completed run's results (GET only: no evaluation is created and no model is called).</summary>
    public async Task<(FoundryRunInfo Run, IReadOnlyList<FoundryScore> Scores)> FetchAsync(string evalId, string runId, int rows, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        var client = new FoundryEvaluationClient(http, projectEndpoint, credential);
        var run = await client.WaitForRunAsync(evalId, runId, TimeSpan.FromMinutes(1), null, ct);
        return await ReadAsync(client, evalId, runId, run, rows, ct);
    }

    public async Task<(FoundryRunInfo Run, IReadOnlyList<FoundryScore> Scores)> EvaluateAsync(IReadOnlyList<EvalCase> cases, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        var client = new FoundryEvaluationClient(http, projectEndpoint, credential);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmm");
        var evalId = await client.CreateEvalAsync(FoundryEvaluationClient.EvalDefinition($"matchshift-quality-{stamp}", deployment), ct);
        log($"Foundry eval created: {evalId}");
        var runId = await client.CreateRunAsync(evalId, FoundryEvaluationClient.RunDefinition($"matchshift-run-{stamp}", cases), ct);
        log($"Foundry run created: {runId}");
        var run = await client.WaitForRunAsync(evalId, runId, TimeSpan.FromMinutes(30), s => log($"  run status: {s}"), ct);
        return await ReadAsync(client, evalId, runId, run, cases.Count, ct);
    }

    private async Task<(FoundryRunInfo Run, IReadOnlyList<FoundryScore> Scores)> ReadAsync(FoundryEvaluationClient client, string evalId, string runId,
        JsonElement run, int rows, CancellationToken ct)
    {
        var status = run.GetProperty("status").GetString() ?? "?";
        var reportUrl = run.TryGetProperty("report_url", out var u) && u.ValueKind == JsonValueKind.String ? Redact.ReportUrl(u.GetString()) : null;
        var counts = run.TryGetProperty("result_counts", out var rc) && rc.ValueKind == JsonValueKind.Object
            ? string.Join(", ", rc.EnumerateObject().Select(p => $"{p.Name} {p.Value}")) : null;
        var error = run.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object ? err.GetRawText() : null;
        var scores = status == "completed" ? FoundryEvaluationClient.Scores(await client.OutputItemsAsync(evalId, runId, ct)) : [];
        return (new FoundryRunInfo(evalId, runId, status, reportUrl, deployment, FoundryEvaluationClient.Evaluators, rows, counts, error), scores);
    }
}
