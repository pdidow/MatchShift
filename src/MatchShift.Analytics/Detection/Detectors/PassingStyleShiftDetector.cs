using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Detection.Detectors;

/// <summary>A substantial change in long-ball share, comparing adjacent eight-minute windows.</summary>
public sealed class PassingStyleShiftDetector : IShiftDetector
{
    public InsightType Type => InsightType.PassingStyleShift;
    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        if (ctx.Now < 16 * 60) yield break;
        var (baseline, recent, comparison) = ctx.Compare(8, 8);
        foreach (var team in ctx.Match.TeamIds)
        {
            var b = baseline.For(team); var r = recent.For(team);
            if (b.PassesAttempted < 20 || r.PassesAttempted < 20) continue;
            var from = (double)b.LongBalls / b.PassesAttempted;
            var to = (double)r.LongBalls / r.PassesAttempted;
            if (Math.Abs(to - from) < .30) continue;
            var ids = ctx.State.Events.Where(e => e.TeamId == team && e.Type == EventType.Pass &&
                e.MatchSecond >= comparison.Baseline.FromSecond && e.MatchSecond < comparison.Recent.ToSecond).Select(e => e.Id).ToArray();
            yield return new DetectionCandidate
            {
                Type = Type, TeamId = team, Variant = to > from ? "more" : "less",
                Span = DetectionContext.Ref(comparison.Recent), Severity = Severity.Medium,
                ConfidenceScore = Statistics.ConfidenceFromMargin(Math.Abs(to - from) / .30, Math.Min(b.PassesAttempted, r.PassesAttempted)),
                Evidence = [EvidenceFactory.Change("LongBallShare", MetricUnit.Share, from, to, comparison, .30,
                    r.PassesAttempted, b.PassesAttempted, 20, primary: true)],
                SupportingEventIds = ids, Visuals = [new VisualLayer("passes", VisualStyle.Routes, ids)],
                KeyPlayerId = ctx.MostInvolvedPlayer(ids),
                RelatedPlayerIds = ids.Select(id => ctx.State.FindEvent(id)?.PlayerId).OfType<string>().Distinct().ToArray(),
            };
        }
    }
}
