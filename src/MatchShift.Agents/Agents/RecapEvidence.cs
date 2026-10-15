using MatchShift.Agents.Narrative;
using MatchShift.Core.Insights;

namespace MatchShift.Agents.Agents;

/// <summary>The numbers a recap is allowed to contain, derived from <see cref="RecapFacts"/>.</summary>
public static class RecapEvidence
{
    public static IEnumerable<double> AllowedNumbers(RecapFacts r, IReadOnlyList<Insight> insights, MatchRoster roster)
    {
        yield return r.HomeScore;
        yield return r.AwayScore;
        foreach (var g in r.Goals) { yield return g.DisplayMinute; yield return g.HomeScore; yield return g.AwayScore; }
        foreach (var m in r.MomentumPeriods) { yield return m.FromMinute; yield return m.ToMinute; yield return m.AverageControl; yield return m.PeakPressure; }
        foreach (var p in r.PressurePeaks) { yield return p.Minute; yield return p.Value; }
        foreach (var t in new[] { r.Home, r.Away })
        {
            foreach (var v in new[] { t.Goals, t.Shots, t.ShotsOnTarget, t.Attacks, t.Turnovers, t.DangerousTurnovers, t.HighRecoveries, t.BigChances })
                yield return v;
            foreach (var share in new[] { t.PossessionShare, t.LeftAttackShare, t.CentreAttackShare, t.RightAttackShare })
            {
                yield return share;
                yield return Math.Round(share * 100);
            }
            yield return t.ChanceQuality;
            yield return t.AveragePressure;
            yield return t.PeakPressure;
        }
        yield return Math.Round(r.ChaoticMinuteShare * 100);
        foreach (var p in r.KeyPlayers) { yield return p.Goals; yield return p.Assists; yield return p.Shots; yield return p.KeyPasses; }
        foreach (var b in r.Story) yield return b.Minute;

        var lex = LexiconRegistry.Get("en");
        foreach (var insight in insights)
        {
            var slots = NarrativeSlots.ForInsight(insight, roster, lex);
            foreach (var n in VerificationAgent.AllowedNumbers(insight, slots)) yield return n;
        }
    }
}
