using MatchShift.Core.Insights;

namespace MatchShift.Agents.Narrative;

/// <summary>Models choose evidence-bound editorial treatments. They cannot write arbitrary factual prose.</summary>
public sealed record NarrativeSelection(string InsightId, string? TeamId, string? PlayerId,
    string Metric, double? Baseline, double Recent, TimeSpanRef? BaselineWindow, TimeSpanRef? RecentWindow,
    IReadOnlyList<string> EventIds, string Treatment)
{
    public static NarrativeSelection For(Insight i, string treatment = "analysis")
    {
        var e = i.PrimaryEvidence ?? i.Evidence.First();
        return new(i.Id, i.TeamId, i.KeyPlayerId, e.Metric, e.BaselineValue, e.RecentValue,
            e.BaselineWindow, e.RecentWindow, i.SupportingEventIds, treatment);
    }

    public bool Matches(Insight i)
    {
        var expected = For(i, Treatment);
        return Treatment is "analysis" or "brief" or "context" &&
            InsightId == expected.InsightId && TeamId == expected.TeamId && PlayerId == expected.PlayerId &&
            Metric == expected.Metric && Baseline == expected.Baseline && Recent == expected.Recent &&
            BaselineWindow == expected.BaselineWindow && RecentWindow == expected.RecentWindow &&
            EventIds.SequenceEqual(expected.EventIds);
    }

    public InsightNarrative Render(Insight i, NarrativeContext c, string source)
    {
        if (!Matches(i)) throw new InvalidDataException("Invalid evidence reference or editorial treatment.");
        var t = TemplateInsightProvider.Narrate(i, c);
        // Keep observations, inference and possible explanation in their respective fields.
        return t with { Headline = Treatment == "brief" ? t.Ticker : Treatment == "context" ? t.StudioLine : t.Headline,
            Explanation = Treatment == "brief" ? null : t.Explanation, Source = source, Selection = this };
    }
}
