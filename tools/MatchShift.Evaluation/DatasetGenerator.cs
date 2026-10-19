using System.Text.Json;
using Azure.Core;
using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Analytics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Clubs;
using MatchShift.SyntheticData.Scenarios;

namespace MatchShift.Evaluation;

/// <summary>One good case to generate live: which match and insight, in which language and audience mode.</summary>
public sealed record CaseSpec(string Id, string MatchKey, InsightType Type, string Language, ViewerMode Mode);

/// <summary>
/// Generates the evaluation dataset from REAL LIVE_FOUNDRY workflow executions (the production Agent Framework workflow
/// and agents, against the configured Foundry project), then derives the adversarial set from those real outputs.
/// Developer-triggered only; never runs in the application or in unit tests.
/// </summary>
public sealed class DatasetGenerator(Uri projectEndpoint, string deployment, TokenCredential credential, Action<string> log)
{
    public static readonly IReadOnlyList<CaseSpec> GoodSpecs =
    [
        new("G01", "flagship", InsightType.AttackChannelShift, "en", ViewerMode.Analyst),
        new("G02", "flagship", InsightType.AttackChannelShift, "fr", ViewerMode.Analyst),
        new("G03", "flagship", InsightType.AttackChannelShift, "es", ViewerMode.Analyst),
        new("G04", "flagship", InsightType.AttackChannelShift, "en", ViewerMode.Studio),
        new("G05", "flagship", InsightType.AttackChannelShift, "en", ViewerMode.TeachMe),
        new("G06", "flagship", InsightType.Goal, "en", ViewerMode.Analyst),
        new("G07", "flagship", InsightType.Goal, "fr", ViewerMode.Analyst),
        new("G08", "flagship", InsightType.Goal, "es", ViewerMode.Analyst),
        new("G09", "flagship", InsightType.DangerousTurnover, "en", ViewerMode.Analyst),
        new("G10", "flagship", InsightType.PressureIncrease, "en", ViewerMode.WhatMatters),
        new("G11", "flagship", InsightType.ControlShift, "fr", ViewerMode.Analyst),
        new("G12", "flagship", InsightType.TerritorialShift, "es", ViewerMode.TeachMe),
        new("G13", "flagship", InsightType.TurnoverSpike, "en", ViewerMode.Analyst),
        new("G14", "passing", InsightType.PassingStyleShift, "en", ViewerMode.Analyst),
    ];

    /// <summary>Good audience texts re-checked by the Verification Agent alone, as controls (it should not reject them).</summary>
    public static readonly string[] VerifierControls = ["G01", "G06", "G09", "G14"];

    public const int EstimatedCallsPerWorkflow = 5;

    public static (int Workflows, int VerifierCalls, int FoundryRows, int FoundryJudgeCalls, int Total) Estimate(int adversarial = 11)
    {
        var rows = GoodSpecs.Count * 2 + adversarial;
        var judge = rows * FoundryEvaluationClient.Evaluators.Length;
        var verifier = adversarial + VerifierControls.Length;
        return (GoodSpecs.Count, verifier, rows, judge, GoodSpecs.Count * EstimatedCallsPerWorkflow + verifier + judge);
    }

    public int ModelCalls { get; private set; }

    public static Match MatchFor(string key) => key switch
    {
        "passing" => SyntheticMatchGenerator.GenerateScenario(ScenarioKind.PassingStyleShift, 7001).Match,
        _ => SyntheticMatchGenerator.GenerateFlagship().Match,
    };

    private static readonly string[] Foreign = [.. ClubCatalog.DatasetClubs.SelectMany(c => new[] { c.Name, c.ShortName })];

    public static InsightContext ContextFor(CaseSpec spec, string executionId)
    {
        var match = MatchFor(spec.MatchKey);
        var insight = LiveMatchAnalyzer.AnalyzeFull(match).Insights.First(i => i.Type == spec.Type);
        return InsightContextBuilder.Build(insight, match, new ViewerProfile(spec.Mode, spec.Language), AccessibilityRequirements.All, executionId, Foreign);
    }

    public async Task<IReadOnlyList<EvalCase>> GenerateAsync(CancellationToken ct)
    {
        var cases = new List<EvalCase>();
        var contexts = new Dictionary<string, InsightContext>();
        var outputs = new Dictionary<string, (TacticalAnalystResult? T, AudienceNarrativeResult? A, AccessibilityNarrativeResult? X)>();

        var published = new Dictionary<string, (string? Audience, string? Accessible)>();
        foreach (var spec in GoodSpecs)
        {
            var context = ContextFor(spec, $"wf-eval-{spec.Id}");
            contexts[spec.Id] = context;
            var sink = new CaptureSink();
            var runner = new InsightWorkflowRunner(new CapturingFoundryAgentFactory(projectEndpoint, deployment, credential, sink));
            var x = await runner.RunAsync(context, runner.CreateRecorder(context), ct);
            ModelCalls += x.ModelCalls;
            var t = Parse<TacticalAnalystResult>(sink, AgentRole.TacticalAnalyst);
            var a = Parse<AudienceNarrativeResult>(sink, AgentRole.AudienceNarrative);
            var acc = Parse<AccessibilityNarrativeResult>(sink, AgentRole.AccessibilityNarrator);
            outputs[spec.Id] = (t, a, acc);
            log($"{spec.Id} {spec.Type.Code()} {spec.Language} {spec.Mode}: {x.Status} {x.Outcome} verified={x.Verified} calls={x.ModelCalls}");

            // What the viewer is shown: the published AI text when it was verified, otherwise nothing to evaluate as AI output.
            var p = x.Verified ? x.Published : null;
            var shownAudience = p?.Headline is { } h && p.Summary is { } sm ? $"{h}. {sm}" : null;
            var shownAccessible = p?.SpokenSummary;
            published[spec.Id] = (shownAudience, shownAccessible);
            var rawAudience = a is null ? null : $"{a.Headline}. {a.Summary}";
            if (shownAudience is not null) cases.Add(Case(spec, context, Variant.Audience, shownAudience, rawAudience, t, a, acc, x));
            if (shownAccessible is not null) cases.Add(Case(spec, context, Variant.Accessibility, shownAccessible, acc?.SpokenSummary, t, a, acc, x));
        }

        // Adversarial: one defect injected into the PUBLISHED (verified) text of G01, and of G02/G04 for the language and audience
        // variants. Only findings the mutation introduces count as a deterministic catch.
        var baseCtx = contexts["G01"];
        var bt = outputs["G01"].T;
        var audienceText = published["G01"].Audience ?? baseCtx.DeterministicNarrative.Fact;
        var accessibleText = published["G01"].Accessible ?? baseCtx.DeterministicNarrative.Fact;
        foreach (var m in Adversarial.For(baseCtx, audienceText, accessibleText, published["G02"].Audience, published["G04"].Audience))
        {
            var spec = m.Id switch { "LANGUAGE_VALUE_CHANGE" => GoodSpecs[1], "AUDIENCE_FACT_CHANGE" => GoodSpecs[3], _ => GoodSpecs[0] };
            var variant = m.Id == "ACCESSIBILITY_CONTRADICTS" ? Variant.Accessibility : Variant.Audience;
            var context = contexts[spec.Id];
            var baseText = m.Id switch
            {
                "ACCESSIBILITY_CONTRADICTS" => accessibleText,
                "LANGUAGE_VALUE_CHANGE" => published["G02"].Audience ?? audienceText,
                "AUDIENCE_FACT_CHANGE" => published["G04"].Audience ?? audienceText,
                _ => audienceText,
            };
            var before = DeterministicEvaluators.Findings(context, variant, baseText);
            var after = DeterministicEvaluators.Findings(context, variant, m.Text);
            var introduced = DeterministicEvaluators.Introduced(before, after);
            var (decision, claims) = await IndependentVerifierAsync(context, bt, variant, m.Text, ct);
            cases.Add(Case(spec, context, variant, m.Text, null, bt, null, null, null) with
            {
                CaseId = $"A-{m.Id}",
                Kind = CaseKind.Adversarial,
                Mutation = m.Id,
                ShouldPass = false,
                ExpectedGroundingNotes = m.Description,
                IntroducedFindings = introduced,
                IndependentVerifierDecision = decision,
                IndependentVerifierClaims = claims,
            });
            log($"A-{m.Id}: deterministic {(introduced.Count > 0 ? "caught" : "missed")}, verifier {decision ?? "unavailable"}");
        }

        // Good controls: the same independent verifier on unmodified real outputs (it should not reject them).
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            if (c.Kind != CaseKind.Good || c.Variant != Variant.Audience || !VerifierControls.Contains(c.CaseId.Split('-')[0])) continue;
            var (decision, claims) = await IndependentVerifierAsync(contexts[c.CaseId.Split('-')[0]], c.TacticalOutput, Variant.Audience, c.Response, ct);
            cases[i] = c with { IndependentVerifierDecision = decision, IndependentVerifierClaims = claims };
            log($"{c.CaseId} control: verifier {decision ?? "unavailable"}");
        }
        return cases;
    }

    /// <summary>
    /// The production Verification Agent judging one text on its own: no deterministic findings are given to it, so this
    /// measures its independent semantic catch rate (in the live workflow it also receives the code findings).
    /// </summary>
    private async Task<(string? Decision, IReadOnlyList<string> Claims)> IndependentVerifierAsync(InsightContext context, TacticalAnalystResult? tactical,
        Variant variant, string text, CancellationToken ct)
    {
        var sink = new CaptureSink();
        var factory = new CapturingFoundryAgentFactory(projectEndpoint, deployment, credential, sink);
        var agent = factory.Create(MatchShiftAgents.Verifier)!;
        var invoker = new AgentInvoker(factory, WorkflowLimits.Default, new ModelCallBudget(2));
        var payload = JsonSerializer.Serialize(new
        {
            insightContext = context,
            deterministicFindings = Array.Empty<string>(),
            tacticalAnalystResult = tactical,
            audienceNarrativeResult = variant == Variant.Audience ? new AudienceNarrativeResult(context.SelectedAudienceMode, "", text, null, "") : null,
            accessibilityNarrativeResult = variant == Variant.Accessibility ? new AccessibilityNarrativeResult(text, null, text, null) : null,
        }, AgentJson.Options);
        var call = await invoker.RunAsync<VerificationAgentResult>(agent, payload, TemplateOutputs.Normalize, r => VerifierDecisions.All.Contains(r.Decision), ct);
        ModelCalls += call.Attempts;
        return call.Result is { } v ? (v.Decision, v.UnsupportedClaims) : (null, []);
    }

    private static EvalCase Case(CaseSpec spec, InsightContext c, Variant variant, string response, string? raw, TacticalAnalystResult? t,
        AudienceNarrativeResult? a, AccessibilityNarrativeResult? x, WorkflowExecution? run) => new()
    {
        CaseId = $"{spec.Id}-{variant}",
        Kind = CaseKind.Good,
        Variant = variant,
        InsightId = c.InsightId,
        InsightType = c.InsightType,
        Language = c.Language,
        AudienceMode = c.SelectedAudienceMode,
        MatchTime = c.MatchTime,
        AuthoritativeFacts = AuthoritativeContext.Text(c),
        DeterministicMetrics = c.DeterministicMetrics,
        SupportingEvidenceIds = c.SupportingEventIds,
        SourceEvents = c.SourceEvents,
        TacticalOutput = t,
        AudienceOutput = a,
        AccessibilityOutput = x,
        Response = response,
        RawAgentResponse = raw,
        RawAgentFindings = raw is null ? [] : DeterministicEvaluators.Findings(c, variant, raw),
        Query = AuthoritativeContext.Query(c),
        WorkflowOutcome = run?.Outcome?.ToString(),
        VerifierDecision = run?.VerificationDecision,
        PublishedVerified = run?.Verified,
        DeterministicFindings = DeterministicEvaluators.Findings(c, variant, response),
        ExpectedGroundingNotes = "Published (verified) agent output; should be fully supported by the authoritative context.",
    };

    private static T? Parse<T>(CaptureSink sink, AgentRole role) where T : class
    {
        if (!sink.Outputs.TryGetValue(role, out var json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, AgentJson.Options); }
        catch (JsonException) { return null; }
    }
}
