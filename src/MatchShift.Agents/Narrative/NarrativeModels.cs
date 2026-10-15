using MatchShift.Core.Insights;

namespace MatchShift.Agents.Narrative;

/// <summary>One evidence item formatted for the WHY? panel in the viewer's language.</summary>
public sealed record EvidenceLine(
    string Metric,
    string Label,
    string? Baseline,
    string Recent,
    string? Change,
    string? Threshold,
    string? BaselineWindow,
    string? RecentWindow,
    int SampleSize,
    int? MinimumSample,
    bool SampleRequirementMet,
    bool IsPrimary,
    string? Comparison);

/// <summary>
/// An insight rendered for one viewer. FACT, INFERENCE and POSSIBLE EXPLANATION are kept
/// separate so the UI (and the verification agent) never confuse observation with interpretation.
/// </summary>
public sealed record InsightNarrative
{
    public required string InsightId { get; init; }
    public required string Language { get; init; }
    public required ViewerMode Mode { get; init; }
    public required string TypeLabel { get; init; }
    public required string Headline { get; init; }
    public required string Fact { get; init; }
    public required string Inference { get; init; }
    public string? Explanation { get; init; }
    public string? Teaching { get; init; }
    public required string Ticker { get; init; }
    public required string StudioLine { get; init; }
    public string? Perspective { get; init; }
    public string? PlayerLine { get; init; }
    public required string ConfidenceLabel { get; init; }
    public required string SeverityLabel { get; init; }
    public required IReadOnlyList<EvidenceLine> Evidence { get; init; }

    /// <summary>"template", or the AI provider that produced the wording.</summary>
    public string Source { get; init; } = "template";

    /// <summary>Verification outcome when an AI provider wrote the text.</summary>
    public string? VerificationNote { get; init; }

    public IReadOnlyList<string> MissingSlots { get; init; } = [];

    /// <summary>Every viewer-facing sentence, for verification.</summary>
    public IEnumerable<string> Sentences()
    {
        yield return Headline;
        yield return Fact;
        yield return Inference;
        if (Explanation is not null) yield return Explanation;
        if (Teaching is not null) yield return Teaching;
        yield return Ticker;
        yield return StudioLine;
        if (Perspective is not null) yield return Perspective;
        if (PlayerLine is not null) yield return PlayerLine;
    }
}

public sealed record RecapSection(string Heading, IReadOnlyList<string> Paragraphs);

public sealed record RecapNarrative
{
    public required string MatchId { get; init; }
    public required RecapPeriod Period { get; init; }
    public required RecapStyle Style { get; init; }
    public required string Language { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<RecapSection> Sections { get; init; }
    public string Source { get; init; } = "template";
    public string? VerificationNote { get; init; }

    public string Text => string.Join("\n\n", Sections.Select(s =>
        (string.IsNullOrEmpty(s.Heading) ? "" : s.Heading + "\n") + string.Join("\n", s.Paragraphs)));
}

public sealed record NarrativeContext(MatchRoster Roster, ViewerProfile Profile);

/// <summary>
/// Turns verified, structured intelligence into words. Implementations may use a language
/// model, but must only rephrase facts they are given; <see cref="Agents.VerificationAgent"/>
/// checks every number before anything reaches a viewer.
/// </summary>
public interface IAiInsightProvider
{
    string Name { get; }

    Task<InsightNarrative> NarrateInsightAsync(Insight insight, NarrativeContext context, CancellationToken cancellationToken = default);

    Task<RecapNarrative> NarrateRecapAsync(RecapFacts recap, RecapStyle style, NarrativeContext context,
        IReadOnlyList<Insight> insights, CancellationToken cancellationToken = default);
}
