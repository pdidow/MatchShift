using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MatchShift.Evaluation;

/// <summary>The Foundry cloud evaluation run as recorded in the project (IDs and portal link, no credentials).</summary>
public sealed record FoundryRunInfo(string EvalId, string RunId, string Status, string? ReportUrl, string JudgeDeployment,
    IReadOnlyList<string> Evaluators, int RowsSubmitted, string? ResultCounts, string? Error);

public sealed record CaseResult(EvalCase Case, IReadOnlyList<FoundryScore> Foundry);

/// <summary>
/// Everything a judge needs to check the evaluation: what ran, against which deployment, the three quality layers
/// reported separately, and every case. Contains no secrets, tenant/subscription/principal IDs, prompts or reasoning.
/// </summary>
public sealed record EvaluationReport
{
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string ModelDeployment { get; init; }
    public required string ProjectEndpointHost { get; init; }
    public required string DatasetSource { get; init; }
    public required int LiveWorkflowRuns { get; init; }
    public required int GenerationModelCalls { get; init; }
    public required string CostEstimate { get; init; }
    public FoundryRunInfo? Foundry { get; init; }
    public required EvaluationSummary Summary { get; init; }
    public required IReadOnlyList<CaseResult> Cases { get; init; }

    /// <summary>Factual history notes (e.g. validator changes since an earlier run), shown at the top of the report.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public static EvaluationReport Build(IReadOnlyList<EvalCase> cases, IReadOnlyList<FoundryScore> scores, FoundryRunInfo? foundry,
        string deployment, string endpointHost, int generationCalls, string estimate, DateTimeOffset now) => new()
    {
        GeneratedAt = now,
        ModelDeployment = deployment,
        ProjectEndpointHost = endpointHost,
        DatasetSource = "Real LIVE_FOUNDRY Microsoft Agent Framework workflow outputs; adversarial rows are those outputs with one injected defect each.",
        LiveWorkflowRuns = cases.Where(c => c.Kind == CaseKind.Good).Select(c => c.CaseId.Split('-')[0]).Distinct().Count(),
        GenerationModelCalls = generationCalls,
        CostEstimate = estimate,
        Foundry = foundry,
        Summary = Aggregation.Summarise(cases, scores),
        Cases = [.. cases.Select(c => new CaseResult(c, [.. scores.Where(s => s.CaseId == c.CaseId)]))],
    };
}

public static class ReportWriter
{
    public static string Json(EvaluationReport report) => JsonSerializer.Serialize(report, EvalJson.Pretty);

    public static string Markdown(EvaluationReport r)
    {
        var s = r.Summary;
        var sb = new StringBuilder();
        string F(double? v) => v is null ? "-" : v.Value.ToString("0.00", CultureInfo.InvariantCulture);
        string Pct(int a, int b) => b == 0 ? "-" : $"{a}/{b} ({100.0 * a / b:0}%)";

        sb.AppendLine("# MatchShift evaluation: latest run");
        sb.AppendLine();
        sb.AppendLine($"Generated {r.GeneratedAt:yyyy-MM-dd HH:mm} UTC. Model deployment `{r.ModelDeployment}` on the Foundry project at `{r.ProjectEndpointHost}`.");
        sb.AppendLine();
        sb.AppendLine($"Dataset: {s.Cases} rows ({s.GoodCases} real agent outputs from {r.LiveWorkflowRuns} live workflow runs, {s.AdversarialCases} adversarial). " +
                      $"Languages: {string.Join(", ", s.Languages)}. Audience modes: {string.Join(", ", s.AudienceModes)}. Insight types: {string.Join(", ", s.InsightTypes)}.");
        sb.AppendLine();
        sb.AppendLine($"Model calls to generate the dataset (workflow agents plus the independent verifier): {r.GenerationModelCalls}. Estimate printed before running: {r.CostEstimate}");
        sb.AppendLine();
        if (r.Notes.Count > 0)
        {
            sb.AppendLine("## History");
            sb.AppendLine();
            foreach (var n in r.Notes) sb.AppendLine($"- {n}");
            sb.AppendLine();
        }
        sb.AppendLine("MatchShift reports three separate quality layers. They are not blended into one score.");
        sb.AppendLine();
        sb.AppendLine("1. **Deterministic validation**: exact code checks against the authoritative match data.");
        sb.AppendLine("2. **Verification Agent**: the production Agent Framework verifier, run here on each text on its own.");
        sb.AppendLine("3. **Microsoft Foundry Evaluations**: built-in AI-assisted judges, run in the Foundry project.");
        sb.AppendLine();

        sb.AppendLine("## Layer 3: Microsoft Foundry Evaluations");
        sb.AppendLine();
        if (r.Foundry is { } f)
        {
            sb.AppendLine($"Eval `{f.EvalId}`, run `{f.RunId}`: **{f.Status}**. Judge deployment `{f.JudgeDeployment}`. Rows submitted: {f.RowsSubmitted}. Result counts: {f.ResultCounts ?? "-"}.");
            if (s.JudgeModels.Count > 0)
                sb.AppendLine($"Judge model reported by Foundry: {string.Join(", ", s.JudgeModels)}. Judge calls with usage reported: {s.JudgeCalls}; " +
                              $"judge tokens: {s.JudgePromptTokens:N0} input, {s.JudgeCompletionTokens:N0} output.");
            if (f.ReportUrl is not null) sb.AppendLine($"Portal report (subscription segment removed; open the project's Evaluations page and find the eval ID): {f.ReportUrl}");
            if (f.Error is not null) sb.AppendLine($"Error: {f.Error}");
            sb.AppendLine();
            sb.AppendLine("| Evaluator | Good rows: mean (1-5) | Good rows passing | Adversarial rows: mean | Adversarial rows passing |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var e in FoundryEvaluationClient.Evaluators)
            {
                var g = s.FoundryGood.GetValueOrDefault(e);
                var a = s.FoundryAdversarial.GetValueOrDefault(e);
                sb.AppendLine($"| {e} | {F(g?.Mean)} | {(g is null ? "-" : Pct(g.Passed, g.Scored))} | {F(a?.Mean)} | {(a is null ? "-" : Pct(a.Passed, a.Scored))} |");
            }
        }
        else sb.AppendLine("No Foundry evaluation run was recorded in this report.");
        sb.AppendLine();

        sb.AppendLine("## Layer 1: deterministic validation of the real outputs");
        sb.AppendLine();
        sb.AppendLine($"- Raw agent texts (before the Verification Agent and repair) with no findings: {Pct(s.RawAgentClean, s.RawAgentTotal)}");
        sb.AppendLine($"- Published audience texts with no findings: {Pct(s.GoodAudienceClean, s.GoodAudienceTotal)}");
        sb.AppendLine($"- Published accessible spoken summaries with no findings: {Pct(s.GoodAccessibilityClean, s.GoodAccessibilityTotal)}");
        sb.AppendLine($"- Multilingual consistency (same insight in several languages, every variant clean): {Pct(s.MultilingualGroupsConsistent, s.MultilingualGroups)}");
        sb.AppendLine($"- Audience-mode consistency (same insight and language in several modes, every variant clean): {Pct(s.AudienceGroupsConsistent, s.AudienceGroups)}");
        sb.AppendLine($"- Live workflow runs whose AI output was published as verified: {Pct(s.WorkflowApproved, s.WorkflowRuns)}");
        sb.AppendLine();

        sb.AppendLine("## Adversarial set: which layer caught each defect");
        sb.AppendLine();
        sb.AppendLine($"Caught by deterministic validation {Pct(s.AdversarialCaughtDeterministic, s.AdversarialCases)}; " +
                      $"by the Verification Agent alone {Pct(s.AdversarialCaughtVerifier, s.AdversarialVerifierRun)}; " +
                      $"by Foundry groundedness {Pct(s.AdversarialCaughtFoundryGroundedness, s.AdversarialFoundryScored)}; " +
                      $"by at least one layer {Pct(s.AdversarialCaughtByAnyLayer, s.AdversarialCases)}.");
        sb.AppendLine();
        sb.AppendLine($"Verification Agent on unmodified good controls: not rejected {Pct(s.GoodControlsVerifierApproved, s.GoodControlsVerifierRun)}.");
        sb.AppendLine();
        sb.AppendLine("| Defect | Deterministic | Verification Agent | Foundry groundedness |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var c in s.Catches)
            sb.AppendLine($"| {c.Mutation} | {(c.DeterministicCaught ? "caught" : "missed")} | {(c.VerifierCaught is null ? "not run" : c.VerifierCaught.Value ? $"caught ({c.VerifierDecision})" : "missed (APPROVED)")} | " +
                          $"{(c.FoundryGroundednessFailed is null ? "not scored" : c.FoundryGroundednessFailed.Value ? $"caught ({F(c.FoundryGroundedness)})" : $"missed ({F(c.FoundryGroundedness)})")} |");
        sb.AppendLine();

        sb.AppendLine("## Every case");
        sb.AppendLine();
        sb.AppendLine("| Case | Kind | Lang | Mode | Insight | Workflow | Findings (raw / shown / introduced) | Groundedness | Relevance | Coherence |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var cr in r.Cases)
        {
            var c = cr.Case;
            string Score(string e) => cr.Foundry.FirstOrDefault(x => x.Evaluator == e) is { } x
                ? x.Error is not null ? "error" : $"{F(x.Score)}{(x.Passed == false ? " fail" : "")}" : "-";
            sb.AppendLine($"| {c.CaseId} | {c.Kind} | {c.Language} | {c.AudienceMode} | {c.InsightType} | {c.WorkflowOutcome ?? "-"} | {(c.RawAgentResponse is null ? "-" : c.RawAgentFindings.Count)} / {c.DeterministicFindings.Count} / {(c.Kind == CaseKind.Adversarial ? c.IntroducedFindings.Count : "-")} | {Score("groundedness")} | {Score("relevance")} | {Score("coherence")} |");
        }
        sb.AppendLine();

        var failures = r.Cases.Where(cr => cr.Case.Kind == CaseKind.Good && (cr.Case.DeterministicFindings.Count > 0 || cr.Case.RawAgentFindings.Count > 0 || cr.Foundry.Any(x => x.Passed == false))).ToList();
        sb.AppendLine("## Good cases with a finding (raw or published) or a failed judge");
        sb.AppendLine();
        if (failures.Count == 0) sb.AppendLine("None.");
        foreach (var cr in failures)
        {
            sb.AppendLine($"### {cr.Case.CaseId}");
            sb.AppendLine();
            sb.AppendLine($"> {cr.Case.Response.Replace("\n", " ")}");
            sb.AppendLine();
            foreach (var d in cr.Case.DeterministicFindings) sb.AppendLine($"- Deterministic (published text): {d}");
            if (cr.Case.RawAgentFindings.Count > 0)
            {
                sb.AppendLine($"- Raw agent text before verification: \"{cr.Case.RawAgentResponse?.Replace("\n", " ")}\"");
                foreach (var d in cr.Case.RawAgentFindings) sb.AppendLine($"  - Deterministic (raw): {d}");
            }
            foreach (var x in cr.Foundry.Where(x => x.Passed == false)) sb.AppendLine($"- Foundry {x.Evaluator} {F(x.Score)}: {x.Reason}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
