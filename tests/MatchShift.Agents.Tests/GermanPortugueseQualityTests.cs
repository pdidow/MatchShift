using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Analytics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;

namespace MatchShift.Agents.Tests;

/// <summary>
/// German phrasing the validator must understand (ranges with ordinals, "2. Halbzeit", German counted nouns), each paired
/// with a case that must still fail; and European Portuguese expectations. Values come from the flagship match.
/// </summary>
public class GermanPortugueseQualityTests
{
    private static readonly Match Flagship = SyntheticMatchGenerator.GenerateFlagship().Match;
    private static readonly IReadOnlyList<Insight> Insights = LiveMatchAnalyzer.AnalyzeFull(Flagship).Insights;

    private static InsightContext Ctx(InsightType type, string lang) =>
        InsightContextBuilder.Build(Insights.First(i => i.Type == type), Flagship, new ViewerProfile(ViewerMode.Analyst, lang),
            AccessibilityRequirements.All, "wf-test", ["Ravensmoor Rovers"]);

    private static InsightContext Shift(string lang = "de") => Ctx(InsightType.AttackChannelShift, lang);

    private static IReadOnlyList<GroundingIssue> Check(InsightContext c, string text) => GroundingValidator.CheckText(c, "agent", "field", text, TextKind.Narrative);

    private static bool Numeric(GroundingIssue i) => i.Code is IssueCodes.UnsupportedNumber or IssueCodes.UnsupportedTime or IssueCodes.FutureTime or IssueCodes.WrongScore;

    private static void Grounded(InsightContext c, string text) =>
        Assert.True(!Check(c, text).Any(Numeric), $"false positive on \"{text}\": {string.Join(" | ", Check(c, text).Select(i => $"{i.Code}: {i.Message}"))}");

    private static void Rejected(InsightContext c, string text, params string[] codes) =>
        Assert.True(Check(c, text).Any(i => codes.Contains(i.Code)), $"not rejected: \"{text}\"");

    private static MetricFact Primary(InsightContext c) => c.DeterministicMetrics.First(m => m.IsPrimary);

    // ---------------------------------------------------------------- German minute ranges

    [Fact]
    public void German_ordinal_minute_ranges_are_grounded_when_both_ends_are_known()
    {
        var c = Shift();
        var w = Primary(c).CurrentWindow!;
        Grounded(c, $"Von der {w.FromMinute}. bis zur {w.ToMinute}. Minute liefen 90 % der Angriffe über rechts.");
        Grounded(c, $"Zwischen der {w.FromMinute}. und der {w.ToMinute}. Minute liefen 90 % der Angriffe über rechts.");
        Grounded(c, $"Zwischen {w.FromMinute}. und {w.ToMinute}. Minute liefen 90 % der Angriffe über rechts.");
    }

    [Fact]
    public void German_minute_ranges_with_an_invented_or_future_end_still_fail()
    {
        var c = Shift();
        var w = Primary(c).CurrentWindow!;
        Rejected(c, $"Von der {w.FromMinute}. bis zur 88. Minute liefen die Angriffe über rechts.", IssueCodes.FutureTime);
        Rejected(c, $"Zwischen der 40. und der {w.ToMinute}. Minute liefen die Angriffe über rechts.", IssueCodes.UnsupportedTime);
        Rejected(c, "Von der 12. bis zur 20. Minute liefen die Angriffe über rechts.", IssueCodes.UnsupportedTime);
    }

    // ---------------------------------------------------------------- German halves

    [Fact]
    public void German_half_ordinals_are_periods_not_numeric_claims()
    {
        var second = Shift();                                                   // insight at 62:00, second half
        Grounded(second, "In der 2. Halbzeit verlagert Westhaven das Spiel nach rechts.");
        Grounded(second, "In der 1. Halbzeit lief das Spiel noch über links.");
        Grounded(second, "Nach der 1. Hälfte hat Westhaven umgestellt.");
    }

    [Fact]
    public void German_half_ordinals_stay_strict()
    {
        var firstHalf = Ctx(InsightType.Goal, "de");                            // goal at 34:33, first half
        Rejected(firstHalf, "In der 2. Halbzeit trifft Owen Blackwood.", IssueCodes.FutureTime);
        var c = Shift();
        Rejected(c, "In der 3. Halbzeit verlagert Westhaven das Spiel.", IssueCodes.UnsupportedNumber);  // no third half
        Rejected(c, "Der 7. Spieler im Angriff ist entscheidend.", IssueCodes.UnsupportedNumber);        // other ordinals not allowed
        Rejected(c, "Das ist der 4. Angriffswechsel heute.", IssueCodes.UnsupportedNumber);
    }

    // ---------------------------------------------------------------- German counts

    [Fact]
    public void German_counted_nouns_are_checked_against_that_noun()
    {
        var c = Shift();
        var m = Primary(c);
        var rightAttacks = c.DeterministicMetrics.First(x => x.Key == "RightAttacks").Current;
        Grounded(c, $"Zuvor waren es {m.BaselineSampleSize} Angriffe.");
        Grounded(c, $"Die Stichprobe umfasst {m.SampleSize} Angriffe gegenüber {m.BaselineSampleSize} Angriffen zuvor.");
        Grounded(c, $"Westhaven hatte {rightAttacks} Angriffe über rechts.");
        Grounded(c, $"Das stützt sich auf {c.SupportingEventIds.Length} Ereignisse.");
    }

    [Fact]
    public void German_counts_stay_strict()
    {
        var c = Shift();
        var m = Primary(c);
        Rejected(c, $"Zuvor waren es {m.BaselineSampleSize!.Value + 1} Angriffe.", IssueCodes.UnsupportedNumber);   // wrong attack count
        Rejected(c, "Westhaven spielte 47 Pässe nach rechts.", IssueCodes.UnsupportedNumber);                      // invented pass count
        Rejected(c, "An der Aktion waren 8 Spieler beteiligt.", IssueCodes.UnsupportedNumber);                    // no grounded player count
        Rejected(c, $"Zuvor waren es {m.BaselineSampleSize} Tore.", IssueCodes.UnsupportedNumber);                // a real number, wrong noun
        Rejected(c, "Die Werte lagen bei 17 und 23.", IssueCodes.UnsupportedNumber);                             // arbitrary numbers
    }

    [Fact]
    public void Spelled_percent_is_a_percentage_not_a_count()
    {
        // Live German run, 2026-10-20: "90 Prozent der Angriffe" must be read as a share, not as 90 attacks.
        var de = Shift();
        Grounded(de, "Der Anteil stieg von 31 Prozent auf 90 Prozent der Angriffe über rechts.");
        Rejected(de, "Inzwischen laufen 77 Prozent der Angriffe über rechts.", IssueCodes.UnsupportedNumber);
        var it = Shift("it");
        Grounded(it, "Il 90 per cento degli attacchi passa a destra, contro il 31 per cento.");
        Rejected(it, "Il 77 per cento degli attacchi passa a destra.", IssueCodes.UnsupportedNumber);
        var pt = Shift("pt");
        Grounded(pt, "90 por cento dos ataques foram pela direita, contra 31 por cento.");
        Rejected(pt, "77 por cento dos ataques foram pela direita.", IssueCodes.UnsupportedNumber);
    }

    [Fact]
    public void A_bare_numeric_window_stays_strict()
    {
        // MatchShift writes windows as 50'-62'; a bare "50-62" is not produced by MatchShift and is not accepted.
        var c = Shift();
        var w = Primary(c).CurrentWindow!;
        Grounded(c, $"Im Abschnitt {w.Label} liefen 90 % der Angriffe über rechts.");
        Rejected(c, $"Im Abschnitt {w.FromMinute}-{w.ToMinute} liefen 90 % der Angriffe über rechts.", IssueCodes.UnsupportedNumber);
    }

    // ---------------------------------------------------------------- European Portuguese

    [Fact]
    public void Agents_are_told_to_write_European_Portuguese_with_Portugal_football_terms()
    {
        var shared = MatchShiftAgents.TacticalAnalyst.Instructions;
        foreach (var agent in MatchShiftAgents.All) Assert.Contains("European Portuguese (pt-PT", agent.Instructions);
        foreach (var term in new[] { "equipa", "golo", "remate", "guarda-redes", "registado" }) Assert.Contains(term, shared);
        foreach (var brazilian in new[] { "time", "gol", "chute", "goleiro", "registrado" }) Assert.Contains(brazilian, shared);   // named as forms to avoid
    }

    [Fact]
    public void Portuguese_lexicon_uses_Portugal_football_vocabulary()
    {
        var pt = LexiconRegistry.Get("pt");
        Assert.Equal("pt-PT", pt.Culture.Name);
        var all = string.Join(" ", pt.Keys.Select(k => pt.Get(k)));
        foreach (var term in new[] { "Golo", "remate", "guarda-redes", "equipa", "relvado", "detetado" }) Assert.Contains(term, all);
        foreach (var brazilian in new[] { " time ", "Gol!", " gol ", "chute", "goleiro", "gramado", "registrado", "detectado", "time " })
            Assert.DoesNotContain(brazilian, all);
    }

    [Fact]
    public void Portuguese_keeps_exactly_the_same_evidence()
    {
        var pt = Shift("pt");
        var en = Shift("en");
        Assert.Equal(en.Score, pt.Score);
        Assert.Equal(en.DeterministicMetrics.Select(m => (m.Current, m.Baseline, m.SampleSize, m.BaselineSampleSize)),
            pt.DeterministicMetrics.Select(m => (m.Current, m.Baseline, m.SampleSize, m.BaselineSampleSize)));
        Assert.Equal(en.SupportingEventIds.ToArray(), pt.SupportingEventIds.ToArray());
        Assert.Equal(en.MatchTime, pt.MatchTime);
        Assert.Equal(en.Players.Select(p => p.Name), pt.Players.Select(p => p.Name));
        var insight = Insights.First(i => i.Type == InsightType.AttackChannelShift);
        Assert.Equal(30, insight.SupportingEventIds.Count);
        Assert.Equal(["MS-FLAGSHIP-001-E00797"], insight.ContextEventIds);
    }
}
