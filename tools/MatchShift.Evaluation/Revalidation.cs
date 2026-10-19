namespace MatchShift.Evaluation;

/// <summary>
/// Re-runs MatchShift's deterministic evaluators over an existing dataset with the current validator. No model or
/// Azure call: the generated texts are unchanged, only the code checks are recomputed.
/// </summary>
public static class Revalidation
{
    /// <summary>The good case an adversarial mutation was derived from (same rule as <see cref="DatasetGenerator"/>).</summary>
    public static string BaseCaseId(EvalCase adversarial) =>
        $"{DatasetGenerator.AdversarialSpec(adversarial.Mutation ?? "").Id}-{adversarial.Variant}";

    public static IReadOnlyList<EvalCase> Apply(IReadOnlyList<EvalCase> cases)
    {
        var contexts = DatasetGenerator.GoodSpecs.ToDictionary(s => s.Id, s => DatasetGenerator.ContextFor(s, $"wf-eval-{s.Id}"));
        var good = cases.Where(c => c.Kind == CaseKind.Good).ToDictionary(c => c.CaseId);
        return [.. cases.Select(c =>
        {
            var specId = c.Kind == CaseKind.Good ? c.CaseId.Split('-')[0] : DatasetGenerator.AdversarialSpec(c.Mutation ?? "").Id;
            var context = contexts[specId];
            var findings = DeterministicEvaluators.Findings(context, c.Variant, c.Response);
            if (c.Kind == CaseKind.Good)
                return c with
                {
                    DeterministicFindings = findings,
                    RawAgentFindings = c.RawAgentResponse is null ? [] : DeterministicEvaluators.Findings(context, c.Variant, c.RawAgentResponse),
                };
            var before = good.TryGetValue(BaseCaseId(c), out var b) ? DeterministicEvaluators.Findings(context, c.Variant, b.Response) : [];
            return c with { DeterministicFindings = findings, IntroducedFindings = DeterministicEvaluators.Introduced(before, findings) };
        })];
    }
}
