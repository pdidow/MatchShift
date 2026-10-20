using System.Globalization;
using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Analytics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;

namespace MatchShift.Agents.Tests;

/// <summary>
/// German, Italian and European Portuguese: realistic agent-style sentences built from the real flagship values must pass the
/// deterministic validator, and the same sentences with a changed value must fail. The wording may change; the evidence must not.
/// </summary>
public class NewLanguageGroundingTests
{
    private static readonly Match Flagship = SyntheticMatchGenerator.GenerateFlagship().Match;
    private static readonly IReadOnlyList<Insight> Insights = LiveMatchAnalyzer.AnalyzeFull(Flagship).Insights;

    private static InsightContext Ctx(InsightType type, string lang) =>
        InsightContextBuilder.Build(Insights.First(i => i.Type == type), Flagship, new ViewerProfile(ViewerMode.Analyst, lang),
            AccessibilityRequirements.All, "wf-test", ["Ravensmoor Rovers"]);

    private static IReadOnlyList<GroundingIssue> Check(InsightContext c, string text) => GroundingValidator.CheckText(c, "agent", "field", text, TextKind.Narrative);

    private static bool Numeric(GroundingIssue i) => i.Code is IssueCodes.UnsupportedNumber or IssueCodes.UnsupportedTime or IssueCodes.FutureTime or IssueCodes.WrongScore;

    private static string Issues(IEnumerable<GroundingIssue> issues) => string.Join(" | ", issues.Select(i => $"{i.Code}: {i.Message}"));

    private static void Grounded(InsightContext c, string text)
    {
        var bad = Check(c, text).Where(Numeric).ToList();
        Assert.True(bad.Count == 0, $"[{c.Language}] false positive on \"{text}\": {Issues(bad)}");
    }

    private static void Rejected(InsightContext c, string text, params string[] codes)
    {
        var issues = Check(c, text);
        Assert.True(issues.Any(i => codes.Contains(i.Code)), $"[{c.Language}] not rejected: \"{text}\" ({Issues(issues)})");
    }

    private static MetricFact Primary(InsightContext c) => c.DeterministicMetrics.First(m => m.IsPrimary);

    private static string Pct(double share) => Math.Round(share * 100).ToString("0", CultureInfo.InvariantCulture);

    public static TheoryData<string> NewLanguages() => ["de", "it", "pt"];

    // ---------------------------------------------------------------- the flagship shift in each language

    [Fact]
    public void German_shift_wording_keeps_every_grounded_value()
    {
        var c = Ctx(InsightType.AttackChannelShift, "de");
        var m = Primary(c);
        var (from, to, w) = (Pct(m.Baseline!.Value), Pct(m.Current), m.CurrentWindow!);
        var z = c.DeterministicMetrics.Single(x => x.Unit == "Statistic");
        Grounded(c, $"Seit der {w.FromMinute}. Minute laufen {to} % der Angriffe von Westhaven über rechts, zuvor waren es {from} %.");
        Grounded(c, $"Zwischen der {w.FromMinute}. und der {w.ToMinute}. Minute liefen {to} % der Angriffe über rechts.");
        Grounded(c, $"Der z-Wert liegt bei {Math.Round(z.Current, 1).ToString("0.0", CultureInfo.GetCultureInfo("de-DE"))}, die Schwelle bei {z.Threshold!.Value.ToString("0.0", CultureInfo.GetCultureInfo("de-DE"))}.");
        Grounded(c, $"Beim Stand von {c.Score.Home}-{c.Score.Away} verlagert Westhaven das Spiel.");
        Grounded(c, $"Die Stichprobe umfasst {m.SampleSize} Angriffe.");
        Grounded(c, $"In den letzten {w.ToMinute - w.FromMinute} Minuten liefen {to} % der Angriffe über rechts.");
        Rejected(c, "In den letzten 17 Minuten liefen die Angriffe über rechts.", IssueCodes.UnsupportedNumber);

        Rejected(c, $"Seit der {w.FromMinute}. Minute laufen {int.Parse(to) - 23} % der Angriffe über rechts.", IssueCodes.UnsupportedNumber);
        Rejected(c, "Seit der 88. Minute laufen die Angriffe über rechts.", IssueCodes.FutureTime);
        Rejected(c, $"Beim Stand von {c.Score.Home + 2}-{c.Score.Away} verlagert Westhaven das Spiel.", IssueCodes.WrongScore);
        Rejected(c, "Der z-Wert liegt bei 4,9.", IssueCodes.UnsupportedNumber);
    }

    [Fact]
    public void Italian_shift_wording_keeps_every_grounded_value()
    {
        var c = Ctx(InsightType.AttackChannelShift, "it");
        var m = Primary(c);
        var (from, to, w) = (Pct(m.Baseline!.Value), Pct(m.Current), m.CurrentWindow!);
        var z = c.DeterministicMetrics.Single(x => x.Unit == "Statistic");
        Grounded(c, $"Dal {w.FromMinute}° minuto il {to}% degli attacchi del Westhaven passa sulla fascia destra, contro il {from}%.");
        Grounded(c, $"Tra il {w.FromMinute}° e il {w.ToMinute}° minuto il {to}% degli attacchi è passato a destra.");
        Grounded(c, $"Il punteggio z è {Math.Round(z.Current, 1).ToString("0.0", CultureInfo.GetCultureInfo("it-IT"))}, sopra la soglia di {z.Threshold!.Value.ToString("0.0", CultureInfo.GetCultureInfo("it-IT"))}.");
        Grounded(c, $"Sul {c.Score.Home}-{c.Score.Away} il Westhaven cambia lato.");
        Grounded(c, $"Il campione è di {m.SampleSize} attacchi.");
        Grounded(c, $"Negli ultimi {w.ToMinute - w.FromMinute} minuti il {to}% degli attacchi è passato a destra.");
        Rejected(c, "Negli ultimi 17 minuti il Westhaven ha attaccato a destra.", IssueCodes.UnsupportedNumber);

        Rejected(c, $"Dal {w.FromMinute}° minuto il {int.Parse(to) - 23}% degli attacchi passa a destra.", IssueCodes.UnsupportedNumber);
        Rejected(c, "Dall'88° minuto il Westhaven attacca a destra.", IssueCodes.FutureTime);
        Rejected(c, $"Sul {c.Score.Home + 2}-{c.Score.Away} il Westhaven cambia lato.", IssueCodes.WrongScore);
    }

    [Fact]
    public void Portuguese_shift_wording_keeps_every_grounded_value()
    {
        var c = Ctx(InsightType.AttackChannelShift, "pt");
        var m = Primary(c);
        var (from, to, w) = (Pct(m.Baseline!.Value), Pct(m.Current), m.CurrentWindow!);
        var z = c.DeterministicMetrics.Single(x => x.Unit == "Statistic");
        Grounded(c, $"Desde o minuto {w.FromMinute}, {to}% dos ataques do Westhaven seguiram pelo corredor direito, contra {from}%.");
        Grounded(c, $"Entre os minutos {w.FromMinute} e {w.ToMinute}, {to}% dos ataques foram pela direita.");
        Grounded(c, $"O valor z é {Math.Round(z.Current, 1).ToString("0.0", CultureInfo.GetCultureInfo("pt-PT"))}, acima do limiar de {z.Threshold!.Value.ToString("0.0", CultureInfo.GetCultureInfo("pt-PT"))}.");
        Grounded(c, $"Com {c.Score.Home}-{c.Score.Away} no marcador, o Westhaven muda de flanco.");
        Grounded(c, $"A amostra é de {m.SampleSize} ataques.");
        Grounded(c, $"Nos últimos {w.ToMinute - w.FromMinute} minutos, {to}% dos ataques foram pela direita.");
        Rejected(c, "Nos últimos 17 minutos, o Westhaven atacou pela direita.", IssueCodes.UnsupportedNumber);

        Rejected(c, $"Desde o minuto {w.FromMinute}, {int.Parse(to) - 23}% dos ataques seguiram pela direita.", IssueCodes.UnsupportedNumber);
        Rejected(c, "Desde o minuto 88, o Westhaven ataca pela direita.", IssueCodes.FutureTime);
        Rejected(c, $"Com {c.Score.Home + 2}-{c.Score.Away} no marcador, o Westhaven muda de flanco.", IssueCodes.WrongScore);
    }

    // ---------------------------------------------------------------- match times on the goal (34:33, minute 35)

    [Fact]
    public void Goal_minute_and_clock_are_grounded_in_each_new_language()
    {
        var de = Ctx(InsightType.Goal, "de");
        Grounded(de, $"Owen Blackwood trifft in der {de.DisplayMinute}. Minute.");
        Grounded(de, $"Owen Blackwood trifft nach {de.MatchTime} zur Führung.");
        Rejected(de, "Owen Blackwood trifft in der 80. Minute.", IssueCodes.FutureTime);

        var it = Ctx(InsightType.Goal, "it");
        Grounded(it, $"Owen Blackwood segna al {it.DisplayMinute}'.");
        Grounded(it, $"Owen Blackwood segna al {it.DisplayMinute}° minuto.");
        Rejected(it, "Owen Blackwood segna all'80° minuto.", IssueCodes.FutureTime);

        var pt = Ctx(InsightType.Goal, "pt");
        Grounded(pt, $"Owen Blackwood marca aos {pt.DisplayMinute} minutos.");
        Grounded(pt, $"Owen Blackwood marca ao minuto {pt.DisplayMinute}.");
        Rejected(pt, "Owen Blackwood marca aos 80 minutos.", IssueCodes.FutureTime);
    }

    // ---------------------------------------------------------------- the deterministic wording itself

    [Theory]
    [MemberData(nameof(NewLanguages))]
    public void Deterministic_wording_in_each_new_language_passes_the_validator_and_keeps_the_values(string lang)
    {
        foreach (var type in new[] { InsightType.AttackChannelShift, InsightType.Goal, InsightType.ControlShift, InsightType.DangerousTurnover })
        {
            var c = Ctx(type, lang);
            var en = Ctx(type, "en");
            var n = c.DeterministicNarrative;
            foreach (var text in new[] { n.Headline, n.Fact, n.Inference }.OfType<string>())
                Assert.DoesNotContain(Check(c, text), Numeric);
            // Same authoritative values whatever the language: score, metrics, evidence IDs, players, match time.
            Assert.Equal(en.Score, c.Score);
            Assert.Equal(en.MatchTime, c.MatchTime);
            Assert.Equal(en.DeterministicMetrics.Select(m => m.Current), c.DeterministicMetrics.Select(m => m.Current));
            Assert.Equal(en.DeterministicMetrics.Select(m => m.SampleSize), c.DeterministicMetrics.Select(m => m.SampleSize));
            Assert.Equal(en.SupportingEventIds.ToArray(), c.SupportingEventIds.ToArray());
            Assert.Equal(en.Players.Select(p => p.Name), c.Players.Select(p => p.Name));
            Assert.Equal(lang, c.Language);
        }
    }
}
