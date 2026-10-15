using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Agents.Narrative;

/// <summary>
/// Deterministic, offline narrative provider. Produces a complete demo in every
/// supported language with no AI service, keys or network - and is the fallback
/// whenever a cloud provider fails verification or is unavailable.
/// </summary>
public sealed class TemplateInsightProvider : IAiInsightProvider
{
    public string Name => "template";

    public Task<InsightNarrative> NarrateInsightAsync(Insight insight, NarrativeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(Narrate(insight, context));

    public Task<RecapNarrative> NarrateRecapAsync(RecapFacts recap, RecapStyle style, NarrativeContext context,
        IReadOnlyList<Insight> insights, CancellationToken cancellationToken = default) =>
        Task.FromResult(RecapNarrator.Narrate(recap, style, context, insights));

    public static InsightNarrative Narrate(Insight insight, NarrativeContext context)
    {
        var lex = LexiconRegistry.Get(context.Profile.Language);
        var slots = NarrativeSlots.ForInsight(insight, context.Roster, lex, context.Profile);
        var code = insight.Type.Code();
        var variant = VariantOf(insight);
        var missing = new List<string>();

        string Render(string key, bool optional = false)
        {
            var template = lex.Find(key, variant);
            if (template is null)
            {
                if (!optional) missing.Add(key);
                return "";
            }
            var (text, miss) = NarrativeSlots.Fill(template, slots);
            missing.AddRange(miss);
            return text;
        }

        var mode = context.Profile.Mode;
        string? perspective = null, playerLine = null;
        if (mode == ViewerMode.MyTeam && slots["focusTeam"] is not null)
            perspective = Render($"perspective.{PerspectiveOf(insight, context.Profile.FocusTeamId!)}");
        if (mode == ViewerMode.MyTeam && context.Profile.FocusPlayerId is { } focusPlayer)
        {
            var involvement = Involvement(insight, context.Roster.Match, focusPlayer);
            if (involvement > 0)
            {
                slots["involvement"] = involvement.ToString(lex.Culture);
                playerLine = Render("player.involved");
            }
        }

        return new InsightNarrative
        {
            InsightId = insight.Id,
            Language = lex.Code,
            Mode = mode,
            TypeLabel = lex.Get($"type.{code}"),
            Headline = Render($"headline.{code}"),
            Fact = Render($"fact.{code}"),
            Inference = Render($"inference.{code}"),
            Explanation = insight.InterpretationCode is null ? null : NullIfEmpty(Render($"explain.{insight.InterpretationCode}", optional: true)),
            Teaching = mode == ViewerMode.TeachMe ? Render($"teach.{code}") : null,
            Ticker = Render($"ticker.{code}"),
            StudioLine = Render($"studio.{code}"),
            Perspective = perspective,
            PlayerLine = playerLine,
            ConfidenceLabel = lex.Get($"confidence.{insight.Confidence}"),
            SeverityLabel = lex.Get($"severity.{insight.Severity}"),
            Evidence = EvidenceLines(insight, lex),
            MissingSlots = missing,
        };
    }

    public static string? VariantOf(Insight insight) => insight.Type switch
    {
        InsightType.TempoShift => insight.Fact("direction"),
        InsightType.PostSubstitutionChange => insight.Fact("aspect"),
        InsightType.Goal => Analytics.Story.HighlightRanker.WhyItMatters(insight),
        _ => null,
    };

    /// <summary>"good" when the insight favours the focus team, "warning" when it favours the opponent.</summary>
    public static string PerspectiveOf(Insight insight, string focusTeamId)
    {
        if (insight.TeamId is null) return "neutral";
        var negativeForTeam = insight.Type is InsightType.PressureDecrease or InsightType.TurnoverSpike or InsightType.DangerousTurnover;
        var aboutFocus = insight.TeamId == focusTeamId;
        return aboutFocus ^ negativeForTeam ? "good" : "warning";
    }

    public static int Involvement(Insight insight, Match match, string playerId)
    {
        var ids = insight.SupportingEventIds.ToHashSet();
        return match.Events.Count(e => ids.Contains(e.Id) && (e.PlayerId == playerId || e.RelatedPlayerId == playerId));
    }

    public static IReadOnlyList<EvidenceLine> EvidenceLines(Insight insight, Lexicon lex) =>
        insight.Evidence.Select(e => new EvidenceLine(
            e.Metric,
            lex.Find($"metric.{e.Metric}") ?? e.Metric,
            e.BaselineValue is { } b ? lex.Format(b, e.Unit) : null,
            lex.Format(e.RecentValue, e.Unit),
            e.Difference is { } d && e.BaselineValue is not null ? lex.FormatDiff(d, e.Unit) : null,
            e.Threshold is { } t && t != 0 ? (e.BaselineValue is not null ? lex.FormatDiff(t, e.Unit).TrimStart('+') : lex.Format(t, e.Unit)) : null,
            e.BaselineWindow?.Label,
            e.RecentWindow?.Label,
            e.SampleSize,
            e.MinimumSampleSize,
            e.SampleRequirementMet,
            e.IsPrimary,
            e.Comparison)).ToList();

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>Which insights a viewer should see, and in what order.</summary>
public static class NarrativeFilter
{
    public const double WhatMattersMinImportance = 70;

    public static IReadOnlyList<Insight> Select(IEnumerable<Insight> insights, ViewerProfile profile)
    {
        var list = insights.ToList();
        return profile.Mode switch
        {
            ViewerMode.WhatMatters => list.Where(i => i.Importance >= WhatMattersMinImportance).ToList(),
            ViewerMode.TeachMe => list.Where(i => i.Importance >= 55 || i.Type == InsightType.Goal).ToList(),
            ViewerMode.MyTeam => list
                .Where(i => i.TeamId is null || i.TeamId == profile.FocusTeamId || i.Importance >= 65 ||
                            (profile.FocusPlayerId is not null && (i.KeyPlayerId == profile.FocusPlayerId || i.RelatedPlayerIds.Contains(profile.FocusPlayerId))))
                .ToList(),
            ViewerMode.Studio => list.Where(i => i.Importance >= 50).ToList(),
            _ => list,
        };
    }
}
