using MatchShift.Analytics.Indices;
using MatchShift.Analytics.Metrics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Detection.Detectors;

/// <summary>
/// POST_SUBSTITUTION_CHANGE: compares the substituting team's play in the
/// <see cref="SubstitutionOptions.BeforeMinutes"/> before a substitution with the
/// period since, once <see cref="SubstitutionOptions.MinMinutesAfter"/> have passed.
/// Reports the strongest significant change among attack channel, pressure and possession.
///
/// It reports a change that FOLLOWED the substitution - correlation, not cause.
/// </summary>
public sealed class PostSubstitutionChangeDetector : IShiftDetector
{
    private static readonly AttackChannel[] Channels = [AttackChannel.Left, AttackChannel.Centre, AttackChannel.Right];

    public InsightType Type => InsightType.PostSubstitutionChange;

    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        var o = ctx.Options.Substitution;
        foreach (var sub in ctx.State.Substitutions)
        {
            var elapsed = (ctx.Now - sub.Second) / 60.0;
            if (elapsed < o.MinMinutesAfter || elapsed > o.MaxMinutesAfter) continue;

            var comparison = WindowComparison.AroundSubstitution(sub.Second, ctx.Now, o.BeforeMinutes);
            var before = ctx.Metrics(comparison.Baseline);
            var after = ctx.Metrics(comparison.Recent);
            var b = before.For(sub.TeamId);
            var a = after.For(sub.TeamId);

            var options = new List<(double Strength, Func<DetectionCandidate> Build)>();

            if (b.Attacks >= 6 && a.Attacks >= 5)
            {
                foreach (var c in Channels)
                {
                    var diff = a.ChannelShare(c) - b.ChannelShare(c);
                    if (diff < o.MinChannelShareIncrease) continue;
                    var z = Statistics.TwoProportionZ(b.ChannelShare(c), b.Attacks, a.ChannelShare(c), a.Attacks);
                    if (z < o.MinChannelZScore) continue;
                    var channel = c;
                    options.Add((diff / o.MinChannelShareIncrease, () => Build(ctx, sub, comparison, "channel",
                        Statistics.SeverityOf(diff / o.MinChannelShareIncrease),
                        Statistics.ConfidenceFromZ(z, a.Attacks, 12),
                        [
                            EvidenceFactory.Change($"{channel}AttackShare", MetricUnit.Share, b.ChannelShare(channel), a.ChannelShare(channel),
                                comparison, o.MinChannelShareIncrease, a.Attacks, b.Attacks, 5, primary: true),
                            EvidenceFactory.Value($"{channel}Attacks", MetricUnit.Count, a.AttacksIn(channel), comparison.Recent, a.Attacks),
                            EvidenceFactory.Value("ShiftStrengthZ", MetricUnit.Statistic, z, comparison.Recent, a.Attacks,
                                threshold: o.MinChannelZScore),
                        ],
                        a.AttackIds(channel),
                        new Dictionary<string, string> { ["aspect"] = "channel", ["channel"] = channel.ToString() },
                        [new VisualLayer("channel", VisualStyle.Channels, a.AttackIds(channel)), new VisualLayer("attacks", VisualStyle.Routes, a.AttackArrivals(channel))])));
                }
            }

            var pBefore = PressureIndex.Compute(b).Value;
            var pAfter = PressureIndex.Compute(a).Value;
            var longEnough = elapsed >= o.MinMinutesForIndexAspects;
            if (longEnough && Math.Abs(pAfter - pBefore) >= o.MinPressureChange && a.EventCount >= 25)
            {
                var diff = pAfter - pBefore;
                options.Add((Math.Abs(diff) / o.MinPressureChange, () => Build(ctx, sub, comparison, "pressure",
                    Statistics.SeverityOf(Math.Abs(diff) / o.MinPressureChange),
                    Statistics.ConfidenceFromMargin(Math.Abs(diff) / o.MinPressureChange, a.EventCount),
                    [
                        EvidenceFactory.Change("PressureIndex", MetricUnit.Index, pBefore, pAfter, comparison, o.MinPressureChange,
                            a.EventCount, b.EventCount, 25, primary: true),
                        .. PressureShiftDetector.Components(b, a, comparison),
                    ],
                    a.EntryIds.Concat(a.ShotIds).ToList(),
                    new Dictionary<string, string> { ["aspect"] = "pressure", ["direction"] = diff > 0 ? "increase" : "decrease" },
                    [new VisualLayer("entries", VisualStyle.Routes, a.EntryIds), new VisualLayer("shots", VisualStyle.Shots, a.ShotIds)])));
            }

            var possDiff = a.PossessionShare - b.PossessionShare;
            if (longEnough && Math.Abs(possDiff) >= o.MinPossessionChange)
            {
                options.Add((Math.Abs(possDiff) / o.MinPossessionChange * 0.9, () => Build(ctx, sub, comparison, "possession",
                    Statistics.SeverityOf(Math.Abs(possDiff) / o.MinPossessionChange),
                    Statistics.ConfidenceFromMargin(Math.Abs(possDiff) / o.MinPossessionChange, after.TotalPossessions, 25),
                    [
                        EvidenceFactory.Change("PossessionShare", MetricUnit.Share, b.PossessionShare, a.PossessionShare, comparison,
                            o.MinPossessionChange, after.TotalPossessions, before.TotalPossessions, 10, primary: true),
                    ],
                    a.EntryIds,
                    new Dictionary<string, string> { ["aspect"] = "possession", ["direction"] = possDiff > 0 ? "increase" : "decrease" },
                    [new VisualLayer("entries", VisualStyle.Routes, a.EntryIds)])));
            }

            if (options.Count == 0) continue;
            yield return options.OrderByDescending(x => x.Strength).First().Build();
        }
    }

    private static DetectionCandidate Build(
        DetectionContext ctx,
        State.SubstitutionRecord sub,
        WindowComparison comparison,
        string aspect,
        Severity severity,
        double confidence,
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<string> supporting,
        Dictionary<string, string> facts,
        IReadOnlyList<VisualLayer> visuals)
    {
        facts["playerOnId"] = sub.PlayerOnId;
        facts["playerOffId"] = sub.PlayerOffId;
        facts["substitutionMinute"] = MatchClock.DisplayMinute(sub.Second).ToString();
        var involvement = ctx.Involvement(supporting, sub.PlayerOnId);
        return new DetectionCandidate
        {
            Type = InsightType.PostSubstitutionChange,
            TeamId = sub.TeamId,
            Variant = sub.EventId,
            Span = DetectionContext.Ref(comparison.Recent),
            Severity = severity,
            ConfidenceScore = confidence,
            Evidence =
            [
                .. evidence,
                EvidenceFactory.Value("SubstituteInvolvement", MetricUnit.Count, involvement, comparison.Recent, supporting.Count),
            ],
            SupportingEventIds = [sub.EventId, .. supporting],
            Facts = facts,
            Visuals = visuals,
            InterpretationCode = $"substitution_{aspect}",
            KeyPlayerId = sub.PlayerOnId,
            RelatedPlayerIds = [sub.PlayerOnId, sub.PlayerOffId],
        };
    }
}
