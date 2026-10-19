using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;

namespace MatchShift.Evaluation;

/// <summary>
/// Microsoft Foundry cloud evaluation through the project's OpenAI-compatible Evals API
/// (POST {project}/openai/v1/evals, /evals/{id}/runs, /runs/{id}/output_items), authenticated with Microsoft Entra ID.
/// Uses the built-in AI-assisted evaluators builtin.groundedness, builtin.relevance and builtin.coherence with the
/// project's own model deployment as judge. Runs and results are stored in the Foundry project (Evaluations).
/// </summary>
public sealed class FoundryEvaluationClient(HttpClient http, Uri projectEndpoint, TokenCredential credential)
{
    public const string TokenScope = "https://ai.azure.com/.default";
    public static readonly string[] Evaluators = ["groundedness", "relevance", "coherence"];

    private string Base => projectEndpoint.ToString().TrimEnd('/') + "/openai/v1";

    /// <summary>The evaluation definition: item schema plus the three built-in judge evaluators.</summary>
    public static JsonObject EvalDefinition(string name, string judgeDeployment) => new()
    {
        ["name"] = name,
        ["data_source_config"] = new JsonObject
        {
            ["type"] = "custom",
            ["item_schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["case_id"] = new JsonObject { ["type"] = "string" },
                    ["query"] = new JsonObject { ["type"] = "string" },
                    ["context"] = new JsonObject { ["type"] = "string" },
                    ["response"] = new JsonObject { ["type"] = "string" },
                },
                ["required"] = new JsonArray("case_id", "query", "context", "response"),
            },
        },
        ["testing_criteria"] = new JsonArray(
            Criterion("groundedness", judgeDeployment, new() { ["query"] = "{{item.query}}", ["context"] = "{{item.context}}", ["response"] = "{{item.response}}" }),
            Criterion("relevance", judgeDeployment, new() { ["query"] = "{{item.query}}", ["response"] = "{{item.response}}" }),
            Criterion("coherence", judgeDeployment, new() { ["query"] = "{{item.query}}", ["response"] = "{{item.response}}" })),
    };

    private static JsonObject Criterion(string name, string deployment, JsonObject mapping) => new()
    {
        ["type"] = "azure_ai_evaluator",
        ["name"] = name,
        ["evaluator_name"] = $"builtin.{name}",
        ["initialization_parameters"] = new JsonObject { ["model"] = deployment },
        ["data_mapping"] = mapping,
    };

    /// <summary>A run over inline rows (file_content). Only the case ID, query, authoritative context and response are sent.</summary>
    public static JsonObject RunDefinition(string name, IEnumerable<EvalCase> cases) => new()
    {
        ["name"] = name,
        ["data_source"] = new JsonObject
        {
            ["type"] = "jsonl",
            ["source"] = new JsonObject
            {
                ["type"] = "file_content",
                ["content"] = new JsonArray([.. cases.Select(c => (JsonNode)new JsonObject
                {
                    ["item"] = new JsonObject
                    {
                        ["case_id"] = c.CaseId, ["query"] = c.Query, ["context"] = c.AuthoritativeFacts, ["response"] = c.Response,
                    },
                })]),
            },
        },
    };

    public async Task<string> CreateEvalAsync(JsonObject definition, CancellationToken ct) =>
        (await SendAsync(HttpMethod.Post, $"{Base}/evals", definition, ct)).GetProperty("id").GetString()!;

    public async Task<string> CreateRunAsync(string evalId, JsonObject run, CancellationToken ct) =>
        (await SendAsync(HttpMethod.Post, $"{Base}/evals/{evalId}/runs", run, ct)).GetProperty("id").GetString()!;

    public async Task<JsonElement> WaitForRunAsync(string evalId, string runId, TimeSpan timeout, Action<string>? progress, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var run = await SendAsync(HttpMethod.Get, $"{Base}/evals/{evalId}/runs/{runId}", null, ct);
            var status = run.GetProperty("status").GetString();
            progress?.Invoke(status ?? "?");
            if (status is "completed" or "failed" or "canceled") return run;
            if (DateTimeOffset.UtcNow > deadline) throw new TimeoutException($"Evaluation run {runId} still '{status}' after {timeout}.");
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    public async Task<IReadOnlyList<JsonElement>> OutputItemsAsync(string evalId, string runId, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        string? after = null;
        do
        {
            var page = await SendAsync(HttpMethod.Get, $"{Base}/evals/{evalId}/runs/{runId}/output_items?limit=100{(after is null ? "" : $"&after={after}")}", null, ct);
            items.AddRange(page.GetProperty("data").EnumerateArray().Select(e => e.Clone()));
            after = page.TryGetProperty("has_more", out var more) && more.GetBoolean() ? page.GetProperty("last_id").GetString() : null;
        }
        while (after is not null);
        return items;
    }

    /// <summary>Maps output items to per-case, per-evaluator scores. Tolerates the documented result shape variations.</summary>
    public static IReadOnlyList<FoundryScore> Scores(IEnumerable<JsonElement> outputItems)
    {
        var scores = new List<FoundryScore>();
        foreach (var item in outputItems)
        {
            var caseId = Find(item, "datasource_item", "case_id") ?? Find(item, "datasource_item", "item", "case_id") ?? "?";
            if (!item.TryGetProperty("results", out var results)) continue;
            foreach (var r in results.EnumerateArray())
            {
                var name = (Str(r, "name") ?? Str(r, "metric") ?? "?").ToLowerInvariant();
                var evaluator = Evaluators.FirstOrDefault(e => name.Contains(e, StringComparison.Ordinal)) ?? name;
                double? score = r.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : null;
                bool? passed = r.TryGetProperty("passed", out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean()
                    : Str(r, "label") is { } label ? label == "pass" : null;
                var reason = Str(r, "reason") ?? (r.TryGetProperty("sample", out var sample) ? Find(sample, "output", "reason") : null);
                var error = r.TryGetProperty("error", out var err) && err.ValueKind != JsonValueKind.Null ? err.ToString() : null;
                double? threshold = r.TryGetProperty("threshold", out var th) && th.ValueKind == JsonValueKind.Number ? th.GetDouble() : null;
                var hasSample = r.TryGetProperty("sample", out var smp) && smp.ValueKind == JsonValueKind.Object;
                var model = hasSample ? Str(smp, "model") : null;
                int? Tokens(string name) => hasSample && smp.TryGetProperty("usage", out var use) && use.ValueKind == JsonValueKind.Object
                    && use.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : null;
                scores.Add(new FoundryScore(caseId, evaluator, score, passed, reason, error, threshold, model, Tokens("prompt_tokens"), Tokens("completion_tokens")));
            }
        }
        return scores;
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string url, JsonObject? body, CancellationToken ct)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext([TokenScope]), ct);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Foundry Evals API {method} {new Uri(url).AbsolutePath} returned {(int)response.StatusCode}: {Trim(text)}", null, response.StatusCode);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Trim(string s) => s.Length <= 600 ? s : s[..600] + "...";

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Find(JsonElement e, params string[] path)
    {
        foreach (var p in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return null;
        }
        return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }
}
