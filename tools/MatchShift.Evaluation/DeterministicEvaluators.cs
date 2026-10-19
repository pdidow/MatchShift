using MatchShift.Agents.Workflow;

namespace MatchShift.Evaluation;

/// <summary>
/// MatchShift's own evaluators. These are exact code checks against the authoritative InsightContext (the same
/// GroundingValidator the runtime uses), kept separate from and alongside the Microsoft Foundry judge scores.
/// </summary>
public static class DeterministicEvaluators
{
    /// <summary>Every finding the deterministic validator raises for one generated text.</summary>
    public static IReadOnlyList<string> Findings(InsightContext c, Variant variant, string text) =>
        [.. GroundingValidator.CheckText(c, variant.ToString(), "response", text, TextKind.Narrative).Select(i => $"{i.Code}: {i.Message}")];

    /// <summary>Findings present after a mutation that were not present before it (multiset difference).</summary>
    public static IReadOnlyList<string> Introduced(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var remaining = before.ToList();
        return [.. after.Where(f => !remaining.Remove(f))];
    }
}

/// <summary>Foundry judge result for one row and one evaluator.</summary>
public sealed record FoundryScore(string CaseId, string Evaluator, double? Score, bool? Passed, string? Reason, string? Error,
    double? Threshold = null, string? JudgeModel = null, int? PromptTokens = null, int? CompletionTokens = null);

/// <summary>Mean score, rows passing the evaluator's threshold, and rows scored (errored rows excluded).</summary>
public sealed record EvaluatorStat(double Mean, int Passed, int Scored);

public sealed record LayerCatch(string Mutation, string CaseId, bool DeterministicCaught, bool? VerifierCaught, string? VerifierDecision, bool? FoundryGroundednessFailed, double? FoundryGroundedness);

public sealed record EvaluationSummary
{
    public required int Cases { get; init; }
    public required int GoodCases { get; init; }
    public required int AdversarialCases { get; init; }
    public required IReadOnlyList<string> Languages { get; init; }
    public required IReadOnlyList<string> AudienceModes { get; init; }
    public required IReadOnlyList<string> InsightTypes { get; init; }

    /// <summary>Per Foundry evaluator, on GOOD rows: mean score, pass count and rows scored (errors excluded).</summary>
    public required IReadOnlyDictionary<string, EvaluatorStat> FoundryGood { get; init; }

    /// <summary>Per Foundry evaluator, on ADVERSARIAL rows (a good evaluator should FAIL these).</summary>
    public required IReadOnlyDictionary<string, EvaluatorStat> FoundryAdversarial { get; init; }

    /// <summary>Raw agent texts (before verification) with no deterministic findings, and how many were checked.</summary>
    public required int RawAgentClean { get; init; }
    public required int RawAgentTotal { get; init; }

    public required int GoodAudienceClean { get; init; }
    public required int GoodAudienceTotal { get; init; }
    public required int GoodAccessibilityClean { get; init; }
    public required int GoodAccessibilityTotal { get; init; }
    public required int MultilingualGroupsConsistent { get; init; }
    public required int MultilingualGroups { get; init; }
    public required int AudienceGroupsConsistent { get; init; }
    public required int AudienceGroups { get; init; }

    public required int AdversarialCaughtDeterministic { get; init; }
    public required int AdversarialCaughtVerifier { get; init; }
    public required int AdversarialVerifierRun { get; init; }
    public required int AdversarialCaughtFoundryGroundedness { get; init; }
    public required int AdversarialFoundryScored { get; init; }
    public required int AdversarialCaughtByAnyLayer { get; init; }

    public required int GoodControlsVerifierApproved { get; init; }
    public required int GoodControlsVerifierRun { get; init; }
    public required int WorkflowApproved { get; init; }
    public required int WorkflowRuns { get; init; }

    public required IReadOnlyList<LayerCatch> Catches { get; init; }

    /// <summary>Judge model(s) Foundry reported, and the judge token usage it reported per call.</summary>
    public required IReadOnlyList<string> JudgeModels { get; init; }
    public required int JudgeCalls { get; init; }
    public required long JudgePromptTokens { get; init; }
    public required long JudgeCompletionTokens { get; init; }
}

public static class Aggregation
{
    public const string Groundedness = "groundedness";

    public static EvaluationSummary Summarise(IReadOnlyList<EvalCase> cases, IReadOnlyList<FoundryScore> scores)
    {
        var good = cases.Where(c => c.Kind == CaseKind.Good).ToList();
        var bad = cases.Where(c => c.Kind == CaseKind.Adversarial).ToList();
        var goodIds = good.Select(c => c.CaseId).ToHashSet();
        var badIds = bad.Select(c => c.CaseId).ToHashSet();

        IReadOnlyDictionary<string, EvaluatorStat> Per(HashSet<string> ids) => scores.Where(s => ids.Contains(s.CaseId) && s.Error is null && s.Score is not null)
            .GroupBy(s => s.Evaluator)
            .ToDictionary(g => g.Key, g => new EvaluatorStat(Math.Round(g.Average(s => s.Score!.Value), 2), g.Count(s => s.Passed == true), g.Count()));

        // Language and audience groups: the same insight generated in several languages or modes; consistent when every
        // variant passes the deterministic validator against the SAME authoritative facts.
        var goodAudience = good.Where(c => c.Variant == Variant.Audience).ToList();
        var languageGroups = goodAudience.GroupBy(c => c.InsightId).Where(g => g.Select(c => c.Language).Distinct().Count() > 1).ToList();
        var modeGroups = goodAudience.GroupBy(c => (c.InsightId, c.Language)).Where(g => g.Select(c => c.AudienceMode).Distinct().Count() > 1).ToList();

        var groundedness = scores.Where(s => s.Evaluator == Groundedness && s.Error is null && s.Passed is not null).ToDictionary(s => s.CaseId);
        var catches = bad.Select(c =>
        {
            var g = groundedness.GetValueOrDefault(c.CaseId);
            return new LayerCatch(c.Mutation ?? "?", c.CaseId, c.IntroducedFindings.Count > 0,
                c.IndependentVerifierDecision is null ? null : c.IndependentVerifierDecision != VerifierDecisions.Approved,
                c.IndependentVerifierDecision, g is null ? null : g.Passed == false, g?.Score);
        }).ToList();

        var controls = good.Where(c => c.IndependentVerifierDecision is not null).ToList();
        var runs = good.Where(c => c.Variant == Variant.Audience).ToList();
        return new EvaluationSummary
        {
            Cases = cases.Count,
            GoodCases = good.Count,
            AdversarialCases = bad.Count,
            Languages = [.. cases.Select(c => c.Language).Distinct().Order()],
            AudienceModes = [.. cases.Select(c => c.AudienceMode).Distinct().Order()],
            InsightTypes = [.. cases.Select(c => c.InsightType).Distinct().Order()],
            FoundryGood = Per(goodIds),
            FoundryAdversarial = Per(badIds),
            RawAgentClean = good.Count(c => c.RawAgentResponse is not null && c.RawAgentFindings.Count == 0),
            RawAgentTotal = good.Count(c => c.RawAgentResponse is not null),
            GoodAudienceClean = goodAudience.Count(c => c.DeterministicFindings.Count == 0),
            GoodAudienceTotal = goodAudience.Count,
            GoodAccessibilityClean = good.Count(c => c.Variant == Variant.Accessibility && c.DeterministicFindings.Count == 0),
            GoodAccessibilityTotal = good.Count(c => c.Variant == Variant.Accessibility),
            MultilingualGroupsConsistent = languageGroups.Count(g => g.All(c => c.DeterministicFindings.Count == 0)),
            MultilingualGroups = languageGroups.Count,
            AudienceGroupsConsistent = modeGroups.Count(g => g.All(c => c.DeterministicFindings.Count == 0)),
            AudienceGroups = modeGroups.Count,
            AdversarialCaughtDeterministic = catches.Count(x => x.DeterministicCaught),
            AdversarialCaughtVerifier = catches.Count(x => x.VerifierCaught == true),
            AdversarialVerifierRun = catches.Count(x => x.VerifierCaught is not null),
            AdversarialCaughtFoundryGroundedness = catches.Count(x => x.FoundryGroundednessFailed == true),
            AdversarialFoundryScored = catches.Count(x => x.FoundryGroundednessFailed is not null),
            AdversarialCaughtByAnyLayer = catches.Count(x => x.DeterministicCaught || x.VerifierCaught == true || x.FoundryGroundednessFailed == true),
            GoodControlsVerifierApproved = controls.Count(c => c.IndependentVerifierDecision != VerifierDecisions.Rejected),
            GoodControlsVerifierRun = controls.Count,
            WorkflowApproved = runs.Count(c => c.PublishedVerified == true),
            WorkflowRuns = runs.Count,
            Catches = catches,
            JudgeModels = [.. scores.Select(x => x.JudgeModel).OfType<string>().Distinct().Order()],
            JudgeCalls = scores.Count(x => x.PromptTokens is not null),
            JudgePromptTokens = scores.Sum(x => (long)(x.PromptTokens ?? 0)),
            JudgeCompletionTokens = scores.Sum(x => (long)(x.CompletionTokens ?? 0)),
        };
    }
}
