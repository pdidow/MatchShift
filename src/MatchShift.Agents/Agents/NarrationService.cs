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
    public bool UsesAi => _primary is not TemplateInsightProvider;

    public async Task<InsightNarrative> InsightAsync(Insight insight, NarrativeContext context, CancellationToken ct = default)
    {
        var template = TemplateInsightProvider.Narrate(insight, context);
        if (!UsesAi) return template;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            var ai = await _primary.NarrateInsightAsync(insight, context, cts.Token);
            var slots = NarrativeSlots.ForInsight(insight, context.Roster, LexiconRegistry.Get(context.Profile.Language), context.Profile);
            var result = _verifier.VerifyInsight(ai, insight, slots);
            return result.Approved
                ? ai with { Evidence = template.Evidence, VerificationNote = $"approved ({result.NumbersChecked} numbers checked)" }
                : template with { VerificationNote = $"AI text rejected: {result.Issues[0].Problem} Template used." };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return template with { VerificationNote = $"AI unavailable ({ex.GetType().Name}). Template used." };
        }
    }

    public async Task<RecapNarrative> RecapAsync(RecapFacts recap, RecapStyle style, NarrativeContext context,
        IReadOnlyList<Insight> insights, CancellationToken ct = default)
    {
        var template = RecapNarrator.Narrate(recap, style, context, insights);
        if (!UsesAi) return template;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            var ai = await _primary.NarrateRecapAsync(recap, style, context, insights, cts.Token);
            var result = _verifier.Verify(ai.Sections.SelectMany(s => s.Paragraphs).Append(ai.Title),
                RecapEvidence.AllowedNumbers(recap, insights, context.Roster));
            return result.Approved
                ? ai with { VerificationNote = $"approved ({result.NumbersChecked} numbers checked)" }
                : template with { VerificationNote = $"AI recap rejected: {result.Issues[0].Problem} Template used." };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return template with { VerificationNote = $"AI unavailable ({ex.GetType().Name}). Template used." };
        }
    }
}
