using MatchShift.Core.Insights;

namespace MatchShift.Agents.Narrative;

public sealed record HighlightNarrative(string HighlightId, string Headline, string WhyItMatters);

public static class HighlightNarrator
{
    public static HighlightNarrative Narrate(Highlight highlight, IReadOnlyList<Insight> insights, NarrativeContext context)
    {
        var lex = LexiconRegistry.Get(context.Profile.Language);
        var slots = new NarrativeSlots { ["team"] = context.Roster.Team(highlight.TeamId) };
        var why = NarrativeSlots.Fill(lex.Find($"highlight.{highlight.WhyItMattersCode}") ?? lex.Get("highlight.notable"), slots).Text;

        var insight = highlight.InsightId is null ? null : insights.FirstOrDefault(i => i.Id == highlight.InsightId);
        var headline = insight is not null
            ? TemplateInsightProvider.Narrate(insight, context).Headline
            : NarrativeSlots.Fill(lex.Get("highlight.headline.pressure_peak"), slots).Text;
        return new HighlightNarrative(highlight.Id, headline, why);
    }
}
