using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure.Core;
using MatchShift.Agents.Workflow;
using MatchShift.Evaluation;
using Microsoft.Extensions.AI;

namespace MatchShift.Agents.Tests;

/// <summary>
/// The Foundry evaluation tooling, fully offline: dataset construction, adversarial labelling, MatchShift's own evaluators,
/// aggregation, the Evals API request/response shapes, and the report. No test here makes an Azure call.
/// </summary>
public class FoundryEvaluationTests
{
    private static InsightContext Ctx(string id) => DatasetGenerator.ContextFor(DatasetGenerator.GoodSpecs.First(s => s.Id == id), $"wf-test-{id}");

    private static string AudienceText(InsightContext c) { var a = TemplateOutputs.Audience(c); return $"{a.Headline}. {a.Summary}"; }

    private static EvalCase GoodCase(string id, Variant variant = Variant.Audience)
    {
        var c = Ctx(id);
        var text = variant == Variant.Audience ? AudienceText(c) : TemplateOutputs.Accessibility(c).SpokenSummary;
        return new EvalCase
        {
            CaseId = $"{id}-{variant}", Kind = CaseKind.Good, Variant = variant, InsightId = c.InsightId, InsightType = c.InsightType,
            Language = c.Language, AudienceMode = c.SelectedAudienceMode, MatchTime = c.MatchTime, AuthoritativeFacts = AuthoritativeContext.Text(c),
            DeterministicMetrics = c.DeterministicMetrics, SupportingEvidenceIds = c.SupportingEventIds, SourceEvents = c.SourceEvents,
            TacticalOutput = TemplateOutputs.Tactical(c), Response = text, RawAgentResponse = text, RawAgentFindings = DeterministicEvaluators.Findings(c, variant, text),
            Query = AuthoritativeContext.Query(c),
            DeterministicFindings = DeterministicEvaluators.Findings(c, variant, text), PublishedVerified = true, WorkflowOutcome = "APPROVED",
        };
    }

    private static IReadOnlyList<Mutation> Mutations()
    {
        var c = Ctx("G01");
        return Adversarial.For(c, AudienceText(c), TemplateOutputs.Accessibility(c).SpokenSummary, AudienceText(Ctx("G02")), AudienceText(Ctx("G04")));
    }

    private static string BaseText(string mutation) => mutation switch
    {
        "ACCESSIBILITY_CONTRADICTS" => TemplateOutputs.Accessibility(Ctx("G01")).SpokenSummary,
        "LANGUAGE_VALUE_CHANGE" => AudienceText(Ctx("G02")),
        "AUDIENCE_FACT_CHANGE" => AudienceText(Ctx("G04")),
        _ => AudienceText(Ctx("G01")),
    };

    private static EvalCase BadCase(Mutation m, string? verifier = "REJECTED")
    {
        var spec = m.Id switch { "LANGUAGE_VALUE_CHANGE" => "G02", "AUDIENCE_FACT_CHANGE" => "G04", _ => "G01" };
        var variant = m.Id == "ACCESSIBILITY_CONTRADICTS" ? Variant.Accessibility : Variant.Audience;
        return GoodCase(spec, variant) with
        {
            CaseId = $"A-{m.Id}", Kind = CaseKind.Adversarial, Mutation = m.Id, ShouldPass = false, Response = m.Text,
            DeterministicFindings = DeterministicEvaluators.Findings(Ctx(spec), variant, m.Text), IndependentVerifierDecision = verifier,
            IntroducedFindings = DeterministicEvaluators.Introduced(DeterministicEvaluators.Findings(Ctx(spec), variant, BaseText(m.Id)), DeterministicEvaluators.Findings(Ctx(spec), variant, m.Text)),
            WorkflowOutcome = null, PublishedVerified = null,
        };
    }

    [Fact]
    public void Dataset_covers_three_languages_several_modes_and_insight_types_within_the_size_bound()
    {
        var specs = DatasetGenerator.GoodSpecs;
        Assert.InRange(specs.Count * 2 + Mutations().Count, 12, 40);
        Assert.Equal(["en", "es", "fr"], specs.Select(s => s.Language).Distinct().Order());
        Assert.True(specs.Select(s => s.Mode).Distinct().Count() >= 3);
        Assert.True(specs.Select(s => s.Type).Distinct().Count() >= 8);
        Assert.Equal(specs.Count, specs.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Jsonl_round_trip_preserves_every_case()
    {
        var cases = new[] { GoodCase("G01"), GoodCase("G02", Variant.Accessibility), BadCase(Mutations()[0]) };
        var back = EvalJson.FromJsonl(EvalJson.ToJsonl(cases));
        Assert.Equal(cases.Length, back.Count);
        for (var i = 0; i < cases.Length; i++)
        {
            Assert.Equal(cases[i].CaseId, back[i].CaseId);
            Assert.Equal(cases[i].Response, back[i].Response);
            Assert.Equal(cases[i].AuthoritativeFacts, back[i].AuthoritativeFacts);
            Assert.Equal(cases[i].Kind, back[i].Kind);
            Assert.Equal(cases[i].DeterministicFindings, back[i].DeterministicFindings);
            Assert.Equal(cases[i].DeterministicMetrics.Count, back[i].DeterministicMetrics.Count);
        }
    }

    [Fact]
    public void Authoritative_context_keeps_score_time_metrics_and_every_source_event()
    {
        var c = Ctx("G01");
        var text = AuthoritativeContext.Text(c);
        Assert.Contains($"{c.Score.Home}-{c.Score.Away}", text);
        Assert.Contains(c.MatchTime, text);
        foreach (var m in c.DeterministicMetrics)
        {
            Assert.Contains(m.DisplayCurrent, text);
            if (m.BaselineSampleSize is { } bs) Assert.Contains($"baseline sample {bs}", text);
        }
        foreach (var e in c.SourceEvents) Assert.Contains(e.Id, text);
        foreach (var p in c.Players) Assert.Contains(p.Name, text);
    }

    [Fact]
    public void Multilingual_contexts_share_the_same_authoritative_numbers()
    {
        var en = Ctx("G01");
        foreach (var other in new[] { Ctx("G02"), Ctx("G03") })
        {
            Assert.NotEqual(en.Language, other.Language);
            Assert.Equal(en.InsightId, other.InsightId);
            Assert.Equal(en.Score, other.Score);
            Assert.Equal(en.DeterministicMetrics.Select(m => m.Current), other.DeterministicMetrics.Select(m => m.Current));
            Assert.Equal(en.SupportingEventIds.ToArray(), other.SupportingEventIds.ToArray());
        }
    }

    [Fact]
    public void Adversarial_set_has_eleven_labelled_single_defect_mutations_of_the_real_text()
    {
        var ms = Mutations();
        Assert.Equal(11, ms.Count);
        Assert.Equal(ms.Count, ms.Select(m => m.Id).Distinct().Count());
        Assert.All(ms, m => Assert.False(string.IsNullOrWhiteSpace(m.Description)));
        var c = Ctx("G01");
        var original = AudienceText(c);
        foreach (var m in ms.Where(m => m.Id is not ("ACCESSIBILITY_CONTRADICTS" or "LANGUAGE_VALUE_CHANGE" or "AUDIENCE_FACT_CHANGE")))
            Assert.StartsWith(original, m.Text);
        Assert.All(ms.Select(m => BadCase(m)), b => Assert.False(b.ShouldPass));
    }

    [Theory]
    [InlineData("WRONG_SCORE", IssueCodes.WrongScore)]
    [InlineData("UNSUPPORTED_PERCENTAGE", IssueCodes.UnsupportedNumber)]
    [InlineData("INVENTED_PLAYER", IssueCodes.UnknownPlayer)]
    [InlineData("PLAYER_COUNT", IssueCodes.UnsupportedNumber)]
    public void Deterministic_layer_flags_the_injected_defect_with_the_expected_code(string mutation, string code)
    {
        var bad = BadCase(Mutations().First(m => m.Id == mutation));
        Assert.Contains(bad.DeterministicFindings, f => f.StartsWith(code, StringComparison.Ordinal));
    }

    [Fact]
    public void Deterministic_layer_flags_every_adversarial_mutation()
    {
        var missed = Mutations().Select(m => BadCase(m)).Where(b => b.IntroducedFindings.Count == 0).Select(b => b.Mutation).ToList();
        Assert.Empty(missed);
    }

    [Theory]
    [InlineData("G01")]
    [InlineData("G02")]
    [InlineData("G03")]
    [InlineData("G06")]
    [InlineData("G12")]
    [InlineData("G14")]
    public void Deterministic_template_text_passes_the_deterministic_evaluator_in_every_language(string id)
    {
        Assert.Empty(GoodCase(id).DeterministicFindings);
        Assert.Empty(GoodCase(id, Variant.Accessibility).DeterministicFindings);
    }

    [Fact]
    public void Revalidation_recomputes_findings_offline_and_keeps_adversarial_catches()
    {
        var stale = new[] { "UNSUPPORTED_NUMBER: stale finding from an older validator" };
        var good = GoodCase("G01") with { DeterministicFindings = stale, RawAgentFindings = stale };
        var bad = BadCase(Mutations().First(m => m.Id == "WRONG_SCORE")) with { IntroducedFindings = [] };
        var result = Revalidation.Apply([good, bad]);
        Assert.Empty(result[0].DeterministicFindings);
        Assert.Empty(result[0].RawAgentFindings);
        Assert.Equal(good.Response, result[0].Response);                     // texts are never changed
        Assert.Contains(result[1].IntroducedFindings, f => f.StartsWith(IssueCodes.WrongScore, StringComparison.Ordinal));
    }

    [Fact]
    public void Introduced_findings_ignore_defects_already_in_the_base_text()
    {
        Assert.Empty(DeterministicEvaluators.Introduced(["A", "B"], ["B", "A"]));
        Assert.Equal(["A"], DeterministicEvaluators.Introduced(["A"], ["A", "A"]));
        Assert.Equal(["C"], DeterministicEvaluators.Introduced(["A"], ["A", "C"]));
    }

    [Fact]
    public void Aggregation_reports_the_three_layers_separately()
    {
        var good = new[] { GoodCase("G01"), GoodCase("G02"), GoodCase("G03"), GoodCase("G04"), GoodCase("G01", Variant.Accessibility) };
        var ms = Mutations();
        var bad = new[] { BadCase(ms[0], "REJECTED"), BadCase(ms[1], "APPROVED"), BadCase(ms[2], null) };
        var scores = new List<FoundryScore>
        {
            new("G01-Audience", "groundedness", 5, true, null, null),
            new("G02-Audience", "groundedness", 3, false, null, null),
            new("G01-Audience", "relevance", 4, true, null, null),
            new(bad[0].CaseId, "groundedness", 2, false, null, null),
            new(bad[1].CaseId, "groundedness", 4, true, null, null),
            new(bad[2].CaseId, "groundedness", null, null, null, "judge error"),
        };
        var s = Aggregation.Summarise([.. good, .. bad], scores);

        Assert.Equal(8, s.Cases);
        Assert.Equal(5, s.GoodCases);
        Assert.Equal(3, s.AdversarialCases);
        Assert.Equal(4.0, s.FoundryGood["groundedness"].Mean);
        Assert.Equal(1, s.FoundryGood["groundedness"].Passed);
        Assert.Equal(2, s.FoundryGood["groundedness"].Scored);
        Assert.Equal(1, s.FoundryAdversarial["groundedness"].Passed);
        Assert.Equal(2, s.FoundryAdversarial["groundedness"].Scored);    // the errored row is not scored
        Assert.Equal(3, s.AdversarialCaughtDeterministic);
        Assert.Equal(1, s.AdversarialCaughtVerifier);
        Assert.Equal(2, s.AdversarialVerifierRun);
        Assert.Equal(1, s.AdversarialCaughtFoundryGroundedness);
        Assert.Equal(2, s.AdversarialFoundryScored);
        Assert.Equal(1, s.MultilingualGroups);                            // G01/G02/G03: one insight in en, fr, es
        Assert.Equal(1, s.AudienceGroups);                                // G01/G04: same insight and language, two modes
        Assert.Equal(4, s.WorkflowRuns);
        Assert.Equal(5, s.RawAgentTotal);
    }

    [Fact]
    public void Eval_definition_uses_the_three_builtin_judges_with_the_existing_deployment()
    {
        var d = FoundryEvaluationClient.EvalDefinition("x", "gpt-4.1-mini");
        var criteria = d["testing_criteria"]!.AsArray();
        Assert.Equal(["builtin.groundedness", "builtin.relevance", "builtin.coherence"], criteria.Select(c => (string)c!["evaluator_name"]!));
        Assert.All(criteria, c => Assert.Equal("azure_ai_evaluator", (string)c!["type"]!));
        Assert.All(criteria, c => Assert.Equal("gpt-4.1-mini", (string)c!["initialization_parameters"]!["model"]!));
        Assert.Equal("{{item.context}}", (string)criteria[0]!["data_mapping"]!["context"]!);
        Assert.Null(criteria[1]!["data_mapping"]!["context"]);
    }

    [Fact]
    public void Run_definition_sends_only_case_id_query_context_and_response()
    {
        var c = GoodCase("G01");
        var run = FoundryEvaluationClient.RunDefinition("r", [c]);
        var item = run["data_source"]!["source"]!["content"]![0]!["item"]!.AsObject();
        Assert.Equal(["case_id", "context", "query", "response"], item.Select(p => p.Key).Order());
        Assert.Equal(c.AuthoritativeFacts, (string)item["context"]!);
        Assert.Equal("file_content", (string)run["data_source"]!["source"]!["type"]!);
    }

    [Fact]
    public void Scores_are_parsed_from_an_output_item()
    {
        const string json = """
            {"datasource_item":{"case_id":"G01-Audience","query":"q","context":"c","response":"r"},
             "results":[{"name":"groundedness","type":"azure_ai_evaluator","score":4,"label":"pass","reason":"Supported.","threshold":3,"passed":true,
                          "sample":{"model":"gpt-4.1-mini-2025-04-14","usage":{"prompt_tokens":2164,"completion_tokens":192}}},
                        {"name":"coherence","score":2,"label":"fail","reason":"Disjointed."},
                        {"name":"relevance","score":null,"error":{"message":"judge failed"}}]}
            """;
        var scores = FoundryEvaluationClient.Scores([JsonDocument.Parse(json).RootElement]);
        Assert.Equal(3, scores.Count);
        Assert.Equal(new FoundryScore("G01-Audience", "groundedness", 4, true, "Supported.", null, 3, "gpt-4.1-mini-2025-04-14", 2164, 192), scores[0]);
        var summary = Aggregation.Summarise([GoodCase("G01")], scores);
        Assert.Equal(["gpt-4.1-mini-2025-04-14"], summary.JudgeModels);
        Assert.Equal(2164, summary.JudgePromptTokens);
        Assert.False(scores[1].Passed);
        Assert.Equal("coherence", scores[1].Evaluator);
        Assert.Null(scores[2].Score);
        Assert.NotNull(scores[2].Error);
    }

    [Fact]
    public async Task Client_talks_only_to_the_injected_handler_with_an_entra_bearer_token()
    {
        var handler = new FakeEvalsHandler();
        var client = new FoundryEvaluationClient(new HttpClient(handler), new Uri("https://example.services.ai.azure.com/api/projects/p"), new FakeCredential());
        var evalId = await client.CreateEvalAsync(FoundryEvaluationClient.EvalDefinition("e", "gpt-4.1-mini"), default);
        var runId = await client.CreateRunAsync(evalId, FoundryEvaluationClient.RunDefinition("r", [GoodCase("G01")]), default);
        var run = await client.WaitForRunAsync(evalId, runId, TimeSpan.FromMinutes(1), null, default);
        var items = await client.OutputItemsAsync(evalId, runId, default);

        Assert.Equal("completed", run.GetProperty("status").GetString());
        Assert.Equal(2, items.Count);                                     // two pages followed via has_more / last_id
        Assert.All(handler.Requests, r => Assert.Equal("Bearer fake-token", r.Auth));
        Assert.All(handler.Requests, r => Assert.StartsWith("/api/projects/p/openai/v1/evals", r.Path));
        Assert.DoesNotContain(handler.Requests, r => r.Path.Contains("api-key", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(FoundryEvaluationClient.TokenScope, handler.Scope ?? FakeCredential.LastScope);
    }

    [Fact]
    public async Task Client_reports_api_errors_with_status_and_no_token()
    {
        var handler = new FakeEvalsHandler { FailWith = HttpStatusCode.Forbidden };
        var client = new FoundryEvaluationClient(new HttpClient(handler), new Uri("https://example.services.ai.azure.com/api/projects/p"), new FakeCredential());
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateEvalAsync(new JsonObject(), default));
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.DoesNotContain("fake-token", ex.Message);
    }

    [Fact]
    public void Report_and_dataset_exports_contain_no_secrets_ids_or_private_prompts()
    {
        var cases = new[] { GoodCase("G01"), GoodCase("G02"), BadCase(Mutations()[0]) };
        var scores = new[] { new FoundryScore("G01-Audience", "groundedness", 5, true, "ok", null) };
        var run = new FoundryRunInfo("eval_abc", "evalrun_def", "completed", "https://ai.azure.com/x", "gpt-4.1-mini", FoundryEvaluationClient.Evaluators, 3, null, null);
        var report = EvaluationReport.Build(cases, scores, run, "gpt-4.1-mini", "example.services.ai.azure.com", 10, EvaluationRunner.EstimateText(), DateTimeOffset.UnixEpoch);
        foreach (var text in new[] { ReportWriter.Json(report), ReportWriter.Markdown(report), EvalJson.ToJsonl(cases) })
        {
            Assert.DoesNotMatch(new Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase), text);
            Assert.DoesNotContain("Bearer", text);
            Assert.DoesNotContain("eyJ", text);                          // JWT prefix
            Assert.DoesNotContain("api-key", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ROLE: ", text);                       // agent instructions are never exported
            Assert.DoesNotContain(MatchShiftAgents.Verifier.Instructions[..60], text);
        }
    }

    [Fact]
    public void Portal_report_link_loses_its_encoded_subscription_segment()
    {
        const string url = "https://ai.azure.com/nextgen/r/AbC_dEf-123,rg-x,,acct,proj/build/evaluations/eval_1/run/evalrun_2";
        Assert.Equal("https://ai.azure.com/nextgen/r/<subscription>,rg-x,,acct,proj/build/evaluations/eval_1/run/evalrun_2", Redact.ReportUrl(url));
        Assert.Null(Redact.ReportUrl(null));
    }

    [Fact]
    public void Markdown_report_keeps_the_three_layers_apart()
    {
        var cases = new[] { GoodCase("G01"), BadCase(Mutations()[0]) };
        var md = ReportWriter.Markdown(EvaluationReport.Build(cases, [], null, "gpt-4.1-mini", "h", 0, "e", DateTimeOffset.UnixEpoch));
        Assert.Contains("## Layer 3: Microsoft Foundry Evaluations", md);
        Assert.Contains("## Layer 1: deterministic validation", md);
        Assert.Contains("Verification Agent", md);
        Assert.Contains("No Foundry evaluation run was recorded", md);   // never implies a run that did not happen
    }

    [Fact]
    public void Cost_estimate_prints_the_required_fields_and_stays_small()
    {
        var block = EvaluationRunner.EstimateBlock("gpt-4.1-mini");
        foreach (var label in new[] { "DATASET SIZE:", "EVALUATORS:", "ESTIMATED EVALUATOR CALLS:", "ESTIMATED TOTAL MODEL CALLS:", "EXPECTED SCALE:" })
            Assert.Contains(label, block);
        Assert.InRange(DatasetGenerator.Estimate().Total, 50, 250);
    }

    [Fact]
    public async Task Capturing_client_records_each_agents_output_by_role()
    {
        var sink = new CaptureSink();
        var inner = new ScriptedChatClient();
        var client = new CapturingChatClient(inner, sink);
        var c = Ctx("G01");
        var prompt = JsonSerializer.Serialize(new { insightContext = c }, AgentJson.Options);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], new ChatOptions { Instructions = MatchShiftAgents.TacticalAnalyst.Instructions });
        Assert.True(sink.Outputs.TryGetValue(AgentRole.TacticalAnalyst, out var json));
        Assert.NotNull(JsonSerializer.Deserialize<TacticalAnalystResult>(json!, AgentJson.Options));
        Assert.False(sink.Outputs.ContainsKey(AgentRole.Verification));
    }

    private sealed class FakeCredential : TokenCredential
    {
        public static string? LastScope;
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("fake-token", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            LastScope = requestContext.Scopes.Single();
            return new(GetToken(requestContext, cancellationToken));
        }
    }

    private sealed class FakeEvalsHandler : HttpMessageHandler
    {
        public sealed record Seen(string Method, string Path, string? Auth);
        public List<Seen> Requests { get; } = [];
        public HttpStatusCode? FailWith { get; init; }
        public string? Scope => null;
        private int _polls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Seen(request.Method.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));
            if (FailWith is { } code) return Task.FromResult(Json(code, """{"error":{"message":"denied"}}"""));
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri.Query;
            var body = path switch
            {
                _ when path.EndsWith("/evals") => """{"id":"eval_1"}""",
                _ when path.EndsWith("/runs") => """{"id":"evalrun_1","status":"queued"}""",
                _ when path.EndsWith("/output_items") && !query.Contains("after") =>
                    """{"data":[{"id":"oi_1","datasource_item":{"case_id":"a"},"results":[]}],"has_more":true,"last_id":"oi_1"}""",
                _ when path.EndsWith("/output_items") => """{"data":[{"id":"oi_2","datasource_item":{"case_id":"b"},"results":[]}],"has_more":false}""",
                _ => ++_polls == 1 ? """{"id":"evalrun_1","status":"completed"}""" : """{"status":"completed"}""",
            };
            return Task.FromResult(Json(HttpStatusCode.OK, body));
        }

        private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
            new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
