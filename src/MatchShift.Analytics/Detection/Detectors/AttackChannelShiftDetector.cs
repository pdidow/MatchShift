using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Detection.Detectors;

/// <summary>
/// ATTACK_CHANNEL_SHIFT: a team's attacks move decisively onto one flank.
///
/// An attack is a possession that reaches the attacking third, assigned to the channel
/// where most of its attacking-third play happened (see <see cref="Metrics.MetricsCalculator.ClassifyAttack"/>).
/// By default the recent window is compared with everything earlier in the match.
///
/// Fires when, for the left or right flank:
/// - the recent share rises by at least <see cref="AttackChannelOptions.MinShareIncrease"/>,
/// - the recent share is at least <see cref="AttackChannelOptions.MinRecentShare"/>,
/// - both windows contain enough attacks, and
/// - a one-sided two-proportion z-test on attacks clears <see cref="AttackChannelOptions.MinZScore"/>.
/// </summary>
public sealed class AttackChannelShiftDetector : IShiftDetector
{
    private static readonly AttackChannel[] Channels = [AttackChannel.Left, AttackChannel.Centre, AttackChannel.Right];

    /// <summary>Only flank shifts are tested: fewer comparisons, and "down the right" is the story broadcasters tell.</summary>
    private static readonly AttackChannel[] Flanks = [AttackChannel.Left, AttackChannel.Right];

    public InsightType Type => InsightType.AttackChannelShift;

    public IEnumerable<DetectionCandidate> Evaluate(DetectionContext ctx)
    {
        var o = ctx.Options.AttackChannel;
        var (baselineWindow, recentWindow, comparison) = o.BaselineMinutes <= 0
            ? ctx.CompareWithMatch(o.RecentMinutes)
            : ctx.Compare(o.RecentMinutes, o.BaselineMinutes);
        if (comparison.Baseline.Minutes < o.MinBaselineSpanMinutes) yield break;

        foreach (var team in ctx.Match.TeamIds)
        {
            var b = baselineWindow.For(team);
            var r = recentWindow.For(team);
            if (r.Attacks < o.MinRecentAttacks || b.Attacks < o.MinBaselineAttacks) continue;

            AttackChannel? best = null;
            var bestZ = 0.0;
            foreach (var channel in Flanks)
            {
                var diff = r.ChannelShare(channel) - b.ChannelShare(channel);
                if (diff < o.MinShareIncrease || r.ChannelShare(channel) < o.MinRecentShare) continue;
                var z = Statistics.TwoProportionZ(b.ChannelShare(channel), b.Attacks, r.ChannelShare(channel), r.Attacks);
                if (z >= o.MinZScore && z > bestZ)
                {
                    best = channel;
                    bestZ = z;
                }
            }
            if (best is not { } c) continue;

            var baselineShare = b.ChannelShare(c);
            var recentShare = r.ChannelShare(c);
            var channelIds = r.AttackIds(c);
            var keyPlayer = ctx.MostInvolvedPlayer(channelIds);
            var name = EvidenceFactory.ChannelName(c);

            var substitution = ctx.State.Substitutions.LastOrDefault(s =>
                s.TeamId == team && s.Second <= ctx.Now && ctx.Now - s.Second <= 15 * 60);

            var facts = new Dictionary<string, string>
            {
                ["channel"] = name,
                ["comparison"] = comparison.Name,
            };
            if (substitution is not null)
            {
                facts["afterSubstitution"] = "true";
                facts["substitutePlayerId"] = substitution.PlayerOnId;
            }

            var others = Channels.Where(x => x != c).SelectMany(r.AttackIds).ToList();
            yield return new DetectionCandidate
            {
                Type = Type,
                TeamId = team,
                Variant = name,
                Span = DetectionContext.Ref(comparison.Recent),
                Severity = Statistics.SeverityOf((recentShare - baselineShare) / o.MinShareIncrease),
                ConfidenceScore = Statistics.ConfidenceFromZ(bestZ, r.Attacks, 12),
                Evidence =
                [
                    EvidenceFactory.Change($"{name}AttackShare", MetricUnit.Share, baselineShare, recentShare, comparison,
                        o.MinShareIncrease, r.Attacks, b.Attacks, o.MinRecentAttacks, primary: true),
                    EvidenceFactory.Value($"{name}Attacks", MetricUnit.Count, r.AttacksIn(c), comparison.Recent, r.Attacks),
                    EvidenceFactory.Value("ShiftStrengthZ", MetricUnit.Statistic, bestZ, comparison.Recent, r.Attacks, threshold: o.MinZScore),
                    .. keyPlayer is null
                        ? Array.Empty<EvidenceItem>()
                        : [EvidenceFactory.Value("KeyPlayerInvolvement", MetricUnit.Count, ctx.Involvement(channelIds, keyPlayer),
                            comparison.Recent, channelIds.Count)],
                ],
                SupportingEventIds = channelIds,
                Facts = facts,
                Visuals =
                [
                    new VisualLayer("channel", VisualStyle.Channels, channelIds),
                    new VisualLayer("shiftedAttacks", VisualStyle.Routes, channelIds),
                    new VisualLayer("otherAttacks", VisualStyle.Routes, others, Emphasised: false),
                ],
                InterpretationCode = substitution is not null ? "channel_after_substitution" : "channel_overload",
                KeyPlayerId = keyPlayer,
                RelatedPlayerIds = substitution is null ? [] : [substitution.PlayerOnId, substitution.PlayerOffId],
            };
        }
    }
}
