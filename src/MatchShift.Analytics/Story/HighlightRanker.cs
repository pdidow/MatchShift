using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Story;

/// <summary>
/// Builds a ranked highlight timeline from insights and the momentum timeline - meaningful
/// even without video.
///
/// Scoring (0-100) starts from the insight's importance, then:
/// - goals that change who is winning, equalise, or come after 80' get a bonus;
/// - pressure peaks (Pressure Index ≥ <see cref="PressurePeakThreshold"/>) are added once per team per half;
/// - highlights within <see cref="MergeWindowSeconds"/> of a more important highlight for the same team
///   are folded into it (their events are kept as supporting events), so one sequence is one highlight.
/// </summary>
public static class HighlightRanker
{
    public const double PressurePeakThreshold = 60;
    public const int MergeWindowSeconds = 90;

    public static IReadOnlyList<Highlight> Rank(LiveMatchAnalyzer analysis, int upToSecond = MatchClock.FullTimeSecond)
    {
        var candidates = new List<Highlight>();
        var insights = analysis.Insights.Where(i => i.DetectedAtSecond <= upToSecond).ToList();

        foreach (var insight in insights)
        {
            var kind = KindOf(insight.Type);
            if (kind is null) continue;
            candidates.Add(new Highlight
            {
                Id = $"H-{insight.Id}",
                Second = insight.Type.IsMoment() ? insight.Span.ToSecond - 1 : insight.DetectedAtSecond,
                Kind = kind.Value,
                TeamId = insight.TeamId,
                InsightId = insight.Id,
                InsightType = insight.Type,
                ImportanceScore = Score(insight),
                WhyItMattersCode = WhyItMatters(insight),
                Facts = insight.Facts,
                Evidence = insight.Evidence,
                RelevantEventIds = insight.SupportingEventIds,
            });
        }

        candidates.AddRange(PressurePeaks(analysis, upToSecond));

        // Merge: keep the most important highlight of a sequence.
        var ordered = candidates.OrderByDescending(h => h.ImportanceScore).ThenBy(h => h.Second).ToList();
        var kept = new List<Highlight>();
        foreach (var h in ordered)
        {
            var parent = kept.FirstOrDefault(k =>
                k.TeamId == h.TeamId && Math.Abs(k.Second - h.Second) <= MergeWindowSeconds &&
                k.Kind != HighlightKind.PressurePeak && h.Kind is not (HighlightKind.Goal or HighlightKind.TacticalShift));
            if (parent is null)
            {
                kept.Add(h);
                continue;
            }
            var index = kept.IndexOf(parent);
            kept[index] = parent with { RelevantEventIds = parent.RelevantEventIds.Union(h.RelevantEventIds).ToList() };
        }

        return kept.OrderByDescending(h => h.ImportanceScore).ThenBy(h => h.Second).ToList();
    }

    private static double Score(Insight insight)
    {
        var score = insight.Importance;
        // Late goals and goals that change the result outrank early openers.
        if (insight.Type == InsightType.Goal)
            score = 92 + (insight.DetectedAtSecond >= 80 * 60 ? 4 : 0) + (WhyItMatters(insight) is "goal_equaliser" or "goal_go_ahead" ? 4 : 0);
        return Math.Round(Math.Clamp(score, 0, 100), 1);
    }

    private static IEnumerable<Highlight> PressurePeaks(LiveMatchAnalyzer analysis, int upToSecond)
    {
        var ctx = analysis.State.Context;
        foreach (var (from, to) in new[] { (1, 45), (46, 90) })
        {
            foreach (var team in ctx.TeamIds)
            {
                var isHome = team == ctx.HomeTeamId;
                var peak = analysis.Timeline
                    .Where(t => t.Minute >= from && t.Minute <= to && t.Minute * 60 <= upToSecond)
                    .OrderByDescending(t => isHome ? t.HomePressure : t.AwayPressure)
                    .ThenBy(t => t.Minute)
                    .FirstOrDefault();
                if (peak is null) continue;
                var value = isHome ? peak.HomePressure : peak.AwayPressure;
                if (value < PressurePeakThreshold) continue;
                var window = new TimeSpanRef(Math.Max(0, (peak.Minute - LiveMatchAnalyzer.TimelinePressureWindowMinutes) * 60), peak.Minute * 60);
                var metrics = Metrics.MetricsCalculator.Compute(analysis.State, new Metrics.AnalysisWindow(window.FromSecond, window.ToSecond)).For(team);
                yield return new Highlight
                {
                    Id = $"H-PP-{team}-{peak.Minute}",
                    Second = peak.Minute * 60,
                    Kind = HighlightKind.PressurePeak,
                    TeamId = team,
                    ImportanceScore = Math.Round(Math.Clamp(40 + (value - PressurePeakThreshold) * 0.6, 0, 75), 1),
                    WhyItMattersCode = "pressure_peak",
                    Facts = new Dictionary<string, string> { ["half"] = from == 1 ? "1" : "2" },
                    Evidence =
                    [
                        new EvidenceItem
                        {
                            Metric = "PressureIndex", Unit = MetricUnit.Index, RecentValue = Math.Round(value, 1), RecentWindow = window,
                            Threshold = PressurePeakThreshold, SampleSize = metrics.EventCount, IsPrimary = true,
                        },
                        new EvidenceItem { Metric = "Shots", Unit = MetricUnit.Count, RecentValue = metrics.Shots, RecentWindow = window },
                        new EvidenceItem { Metric = "AttackingThirdEntries", Unit = MetricUnit.Count, RecentValue = metrics.FinalThirdEntries, RecentWindow = window },
                    ],
                    RelevantEventIds = metrics.EntryIds.Concat(metrics.ShotIds).Concat(metrics.HighRecoveryIds).ToList(),
                };
            }
        }
    }

    public static HighlightKind? KindOf(InsightType type) => type switch
    {
        InsightType.Goal => HighlightKind.Goal,
        InsightType.BigChance => HighlightKind.BigChance,
        InsightType.DangerousTurnover => HighlightKind.DangerousTurnover,
        InsightType.AttackChannelShift => HighlightKind.TacticalShift,
        InsightType.TerritorialShift => HighlightKind.TacticalShift,
        InsightType.TempoShift => HighlightKind.TacticalShift,
        InsightType.ControlShift => HighlightKind.ControlChange,
        InsightType.PostSubstitutionChange => HighlightKind.SubstitutionImpact,
        InsightType.CounterAttackThreat => HighlightKind.CounterAttack,
        InsightType.ChaosIncrease => HighlightKind.ChaoticSpell,
        InsightType.PressureIncrease => HighlightKind.PressurePeak,
        InsightType.ShotPressure => HighlightKind.PressurePeak,
        _ => null,
    };

    public static string WhyItMatters(Insight insight)
    {
        if (insight.Type == InsightType.Goal)
        {
            var home = int.Parse(insight.Fact("homeScore") ?? "0");
            var away = int.Parse(insight.Fact("awayScore") ?? "0");
            var homeScored = insight.Fact("scoringSide") == "home";
            var forScorer = homeScored ? home - away : away - home;
            return forScorer switch
            {
                0 => "goal_equaliser",
                1 when home + away == 1 => "goal_opener",
                1 => "goal_go_ahead",
                > 1 => "goal_extends_lead",
                _ => "goal_pulls_one_back",
            };
        }

        return insight.Type switch
        {
            InsightType.BigChance => "chance_missed",
            InsightType.DangerousTurnover => "turnover_punished",
            InsightType.AttackChannelShift => "new_attacking_route",
            InsightType.PostSubstitutionChange => "substitution_changed_pattern",
            InsightType.ControlShift => "control_swing",
            InsightType.PressureIncrease => "pressure_building",
            InsightType.ShotPressure => "pressure_building",
            InsightType.CounterAttackThreat => "counter_threat",
            InsightType.ChaosIncrease => "game_opened_up",
            InsightType.TerritorialShift => "territory_gained",
            InsightType.TempoShift => "tempo_changed",
            _ => "notable",
        };
    }
}
