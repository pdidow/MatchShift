using System.Globalization;

namespace MatchShift.Agents.Workflow;

/// <summary>
/// Deterministic stand-ins for each agent stage, built only from the authoritative lexicon wording in the context.
/// Used in OFFLINE_TEMPLATE mode and as the safe fallback when an agent fails. Never labelled as AI output.
/// </summary>
public static class TemplateOutputs
{
    public static TacticalAnalystResult Tactical(InsightContext c)
    {
        var w = c.DeterministicNarrative;
        var limitations = new List<string> { "Synthetic match data." };
        if (!c.ConfidenceInputs.AllSampleRequirementsMet) limitations.Add("At least one metric is below its minimum sample.");
        return new TacticalAnalystResult(
            [w.Fact], w.Inference, w.PossibleExplanation, null,
            string.Create(CultureInfo.InvariantCulture, $"Deterministic confidence: {c.ConfidenceInputs.Level} ({c.ConfidenceInputs.Score:0.00})."),
            [.. c.SourceEvents.TakeLast(5).Select(e => e.Id)], limitations);
    }

    public static AudienceNarrativeResult Audience(InsightContext c)
    {
        var w = c.DeterministicNarrative;
        var summary = c.SelectedAudienceMode switch
        {
            "Studio" => w.StudioLine,
            "WhatMatters" => w.Ticker,
            "TeachMe" when w.Teaching is not null => $"{w.Fact} {w.Teaching}",
            _ => w.Fact,
        };
        return new AudienceNarrativeResult(c.SelectedAudienceMode, w.Headline, summary, null, w.Inference);
    }

    public static AccessibilityNarrativeResult Accessibility(InsightContext c)
    {
        var w = c.DeterministicNarrative;
        return new AccessibilityNarrativeResult($"{w.Headline.TrimEnd('.')}. {w.Fact}", null, w.Inference, null);
    }

    /// <summary>Models may omit arrays; normalise so validation never sees null collections.</summary>
    public static TacticalAnalystResult Normalize(TacticalAnalystResult t) => t with
    {
        FactsUsed = t.FactsUsed ?? [], EvidenceIds = t.EvidenceIds ?? [], Limitations = t.Limitations ?? [],
        Inference = t.Inference ?? "", ConfidenceQualification = t.ConfidenceQualification ?? "",
        PossibleExplanation = string.IsNullOrWhiteSpace(t.PossibleExplanation) ? null : t.PossibleExplanation,
        TacticalSignificance = string.IsNullOrWhiteSpace(t.TacticalSignificance) ? null : t.TacticalSignificance,
    };

    public static AudienceNarrativeResult Normalize(AudienceNarrativeResult a) => a with
    {
        Headline = a.Headline ?? "", Summary = a.Summary ?? "", KeyTakeaway = a.KeyTakeaway ?? "", Mode = a.Mode ?? "",
        Detail = string.IsNullOrWhiteSpace(a.Detail) ? null : a.Detail,
    };

    public static AccessibilityNarrativeResult Normalize(AccessibilityNarrativeResult a) => a with
    {
        SpokenSummary = a.SpokenSummary ?? "", SimplifiedExplanation = a.SimplifiedExplanation ?? "",
        VisualDescription = string.IsNullOrWhiteSpace(a.VisualDescription) ? null : a.VisualDescription,
        ReplayAudioDescription = string.IsNullOrWhiteSpace(a.ReplayAudioDescription) ? null : a.ReplayAudioDescription,
    };

    public static VerificationAgentResult Normalize(VerificationAgentResult v) => v with
    {
        Decision = (v.Decision ?? "").Trim().ToUpperInvariant(), Reasons = v.Reasons ?? [], UnsupportedClaims = v.UnsupportedClaims ?? [],
        Corrections = v.Corrections ?? [], EvidenceIdsChecked = v.EvidenceIdsChecked ?? [], Limitations = v.Limitations ?? [],
    };
}
