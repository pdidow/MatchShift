using System.Text;
using System.Text.Json;
using MatchShift.Agents.Narrative;
using MatchShift.Core.Insights;

namespace MatchShift.Infrastructure.Foundry;

/// <summary>
/// Storytelling + personalization + translation through a Microsoft Foundry model.
///
/// The model is NEVER asked to analyse data. It receives: the verified FACT/INFERENCE/EXPLANATION
/// already rendered by the template engine, the structured evidence, the viewer profile and the
/// target language, and is asked to rephrase for that viewer without adding facts or numbers.
/// Its JSON output is then checked by the VerificationAgent (in <c>NarrationService</c>);
/// anything that fails is discarded in favour of the template text.
/// </summary>
public sealed class FoundryInsightProvider(IChatCompletionClient client) : IAiInsightProvider
{
    public const string SystemPrompt = """
        You are the storytelling and personalization agent of MatchShift, a football intelligence product.
        ALL football data is synthetic and fictional.
        Rules you must follow:
        - Use ONLY the facts and numbers provided. Never calculate, estimate or invent a statistic.
        - Every number you write must appear in the provided facts or evidence, written the same way.
        - Keep FACT (observation), INFERENCE (what it suggests) and EXPLANATION (cautious possibility) separate.
        - Never claim intent ("deliberately", "the manager decided") - the data cannot show intent.
        - Do not mention any club or player not named in the input.
        - Write in the requested language and for the requested viewer mode.
        - Respond with a single JSON object and nothing else.
        """;

    public string Name => "microsoft-foundry";

    public async Task<InsightNarrative> NarrateInsightAsync(Insight insight, NarrativeContext context, CancellationToken cancellationToken = default)
    {
        var template = TemplateInsightProvider.Narrate(insight, context);
        var prompt = new StringBuilder()
            .AppendLine($"Language: {context.Profile.Language}")
            .AppendLine($"Viewer mode: {context.Profile.Mode} ({ModeGuidance(context.Profile.Mode)})")
            .AppendLine($"Focus team: {context.Roster.Team(context.Profile.FocusTeamId)}")
            .AppendLine("Verified narrative to rephrase (keep every number exactly):")
            .AppendLine(JsonSerializer.Serialize(new
            {
                template.Headline, template.Fact, template.Inference, template.Explanation, template.Teaching,
                template.Ticker, template.StudioLine, template.Perspective,
            }))
            .AppendLine("Structured evidence:")
            .AppendLine(JsonSerializer.Serialize(template.Evidence.Select(e => new { e.Label, e.Baseline, e.Recent, e.Change, e.RecentWindow })))
            .AppendLine("""Return JSON: {"headline":"","fact":"","inference":"","explanation":"","teaching":"","ticker":"","studioLine":""}""")
            .ToString();

        var json = await client.CompleteJsonAsync(SystemPrompt, prompt, cancellationToken);
        var parsed = JsonSerializer.Deserialize<InsightJson>(json, FoundryChatClient.Json)
                     ?? throw new InvalidOperationException("Unparseable Foundry response.");

        return template with
        {
            Headline = Pick(parsed.Headline, template.Headline),
            Fact = Pick(parsed.Fact, template.Fact),
            Inference = Pick(parsed.Inference, template.Inference),
            Explanation = template.Explanation is null ? null : Pick(parsed.Explanation, template.Explanation),
            Teaching = template.Teaching is null ? null : Pick(parsed.Teaching, template.Teaching),
            Ticker = Pick(parsed.Ticker, template.Ticker),
            StudioLine = Pick(parsed.StudioLine, template.StudioLine),
            Source = Name,
        };
    }

    public async Task<RecapNarrative> NarrateRecapAsync(RecapFacts recap, RecapStyle style, NarrativeContext context,
        IReadOnlyList<Insight> insights, CancellationToken cancellationToken = default)
    {
        var template = RecapNarrator.Narrate(recap, style, context, insights);
        var prompt = new StringBuilder()
            .AppendLine($"Language: {context.Profile.Language}")
            .AppendLine($"Recap style: {style} ({StyleGuidance(style)})")
            .AppendLine("Verified recap to turn into a flowing story. Keep every number exactly as written; do not add new ones:")
            .AppendLine(template.Text)
            .AppendLine("""Return JSON: {"title":"","sections":[{"heading":"","paragraphs":[""]}]}""")
            .ToString();

        var json = await client.CompleteJsonAsync(SystemPrompt, prompt, cancellationToken);
        var parsed = JsonSerializer.Deserialize<RecapJson>(json, FoundryChatClient.Json)
                     ?? throw new InvalidOperationException("Unparseable Foundry response.");
        if (parsed.Sections is not { Count: > 0 }) throw new InvalidOperationException("Foundry recap had no sections.");

        return template with
        {
            Title = Pick(parsed.Title, template.Title),
            Sections = parsed.Sections.Select(s => new RecapSection(s.Heading ?? "", s.Paragraphs ?? [])).ToList(),
            Source = Name,
        };
    }

    private static string Pick(string? candidate, string fallback) => string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private static string ModeGuidance(ViewerMode mode) => mode switch
    {
        ViewerMode.TeachMe => "viewer is new to football; plain words, explain the idea",
        ViewerMode.WhatMatters => "one short, punchy sentence per field",
        ViewerMode.Analyst => "precise tactical language",
        ViewerMode.MyTeam => "speak from the focus team's point of view",
        ViewerMode.Studio => "broadcast talking points a presenter can read aloud",
        _ => "",
    };

    private static string StyleGuidance(RecapStyle style) => style switch
    {
        RecapStyle.Casual => "friendly story for a casual fan",
        RecapStyle.Analyst => "structured tactical debrief",
        RecapStyle.Broadcast => "time-coded script lines for a presenter",
        RecapStyle.Social => "one post under 280 characters",
        _ => "",
    };

    private sealed record InsightJson(string? Headline, string? Fact, string? Inference, string? Explanation, string? Teaching,
        string? Ticker, string? StudioLine);

    private sealed record RecapJson(string? Title, List<RecapSectionJson>? Sections);

    private sealed record RecapSectionJson(string? Heading, List<string>? Paragraphs);
}
