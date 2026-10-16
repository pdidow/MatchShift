using MatchShift.Agents.Narrative;
using MatchShift.Core.Insights;

namespace MatchShift.Agents.Agents;

/// <summary>
/// Front door for all narrative generation. Uses the configured provider (e.g. Microsoft Foundry)
/// when available, but every AI-written text is verified against structured evidence; anything
/// that fails verification, times out or errors is replaced by the deterministic template text.
/// The demo therefore never depends on a cloud service being up.
/// </summary>
public sealed class NarrationService
{
    private readonly IAiInsightProvider _primary;
    private readonly TemplateInsightProvider _template = new();
    private readonly VerificationAgent _verifier;
    private readonly TimeSpan _timeout;

    public NarrationService(IAiInsightProvider primary, VerificationAgent? verifier = null, TimeSpan? timeout = null)
    {
        _primary = primary;
        _verifier = verifier ?? new VerificationAgent();
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
    }

    public string ProviderName => _primary.Name;
    public bool UsesAi => _primary.Name == "microsoft-foundry";
    public bool UsesProvider => _primary is not TemplateInsightProvider;

    public async Task<InsightNarrative> InsightAsync(Insight insight, NarrativeContext context, CancellationToken ct = default)
    {
        EvidenceGate.Require(insight, context.Roster.Match);
        var template = TemplateInsightProvider.Narrate(insight, context);
        if (!UsesProvider) return template;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            var ai = await _primary.NarrateInsightAsync(insight, context, cts.Token);
            if (ai.Selection is null || !ai.Selection.Matches(insight) ||
                !ai.Sentences().SequenceEqual(ai.Selection.Render(insight, context, ai.Source).Sentences()))
                return template with { VerificationNote = "AI text rejected: invalid evidence binding or unapproved prose. Template used." };
            var slots = NarrativeSlots.ForInsight(insight, context.Roster, LexiconRegistry.Get(context.Profile.Language), context.Profile);
            var result = _verifier.VerifyInsight(ai, insight, slots);
            return result.Approved
                ? ai.Selection.Render(insight, context, _primary.Name) with { Collaboration = ai.Collaboration,
                    VerificationNote = $"approved (evidence binding and {result.NumbersChecked} numeric occurrences checked)" }
                : template with { VerificationNote = $"AI text rejected: {result.Issues[0].Problem} Template used." };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return template with { VerificationNote = $"AI unavailable or rejected ({ex.GetType().Name}). Template used.",
                Collaboration = ex is EditorialRejectionException rejection ? rejection.Steps : [] };
        }
    }

    public async Task<RecapNarrative> RecapAsync(RecapFacts recap, RecapStyle style, NarrativeContext context,
        IReadOnlyList<Insight> insights, CancellationToken ct = default)
    {
        var template = RecapNarrator.Narrate(recap, style, context, insights);
        if (!UsesProvider) return template;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            var ai = await _primary.NarrateRecapAsync(recap, style, context, insights, cts.Token);
            if (ai.Title != template.Title || !ai.Sections.Select(s => System.Text.Json.JsonSerializer.Serialize(s)).Order()
                .SequenceEqual(template.Sections.Select(s => System.Text.Json.JsonSerializer.Serialize(s)).Order()))
                return template with { VerificationNote = "AI recap rejected: unsupported section or attribution. Template used." };
            var result = _verifier.Verify(ai.Sections.SelectMany(s => s.Paragraphs.Prepend(s.Heading)).Append(ai.Title),
                RecapEvidence.AllowedNumbers(recap, insights, context.Roster));
            return result.Approved
                ? template with { Sections = ai.Sections, Source = _primary.Name, VerificationNote = $"approved ({result.NumbersChecked} numbers checked)" }
                : template with { VerificationNote = $"AI recap rejected: {result.Issues[0].Problem} Template used." };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return template with { VerificationNote = $"AI unavailable ({ex.GetType().Name}). Template used." };
        }
    }
}
