using System.Globalization;
using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Analytics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Scenarios;

namespace MatchShift.Agents.Tests;

/// <summary>
/// False positives found by the Foundry evaluation run (compact z-scores, French and Spanish match minutes, Spanish
/// sample sizes, compound counts), each paired with a negative case that must still fail. Values come from the synthetic
/// scenarios, never hard-coded numbers that happen to pass.
/// </summary>
public class LocaleNumericGroundingTests
{
    private static readonly Match Flagship = SyntheticMatchGenerator.GenerateFlagship().Match;
    private static readonly Match Passing = SyntheticMatchGenerator.GenerateScenario(ScenarioKind.PassingStyleShift, 7001).Match;

    private static InsightContext Ctx(Match match, InsightType type, string lang) =>
        InsightContextBuilder.Build(LiveMatchAnalyzer.AnalyzeFull(match).Insights.First(i => i.Type == type), match,
            new ViewerProfile(ViewerMode.Analyst, lang), AccessibilityRequirements.All, "wf-test", ["Ravensmoor Rovers"]);

    private static InsightContext Shift(string lang = "en") => Ctx(Flagship, InsightType.AttackChannelShift, lang);
    private static InsightContext Goal(string lang) => Ctx(Flagship, InsightType.Goal, lang);
    private static InsightContext LongBalls => Ctx(Passing, InsightType.PassingStyleShift, "en");

    private static IReadOnlyList<GroundingIssue> Check(InsightContext c, string text) => GroundingValidator.CheckText(c, "agent", "field", text, TextKind.Narrative);

    private static bool Numeric(GroundingIssue i) => i.Code is IssueCodes.UnsupportedNumber or IssueCodes.UnsupportedTime or IssueCodes.FutureTime or IssueCodes.WrongScore;

    private static bool IsTime(GroundingIssue i) => i.Code is IssueCodes.UnsupportedTime or IssueCodes.FutureTime;

    private static string F(double x, string format = "0.##") => x.ToString(format, CultureInfo.InvariantCulture);

    private static MetricFact Z(InsightContext c) => c.DeterministicMetrics.Single(m => m.Unit == "Statistic");

    private static MetricFact Primary(InsightContext c) => c.DeterministicMetrics.First(m => m.IsPrimary);

    // ---------------------------------------------------------------- z-scores

    [Fact]
    public void Compact_z_rounded_to_one_decimal_is_grounded()
    {
        var c = Shift();
        var z = Z(c).Current;
        Assert.NotEqual(Math.Round(z, 1), z);                                       // the authoritative value has more precision
        Assert.DoesNotContain(Check(c, $"The shift is strong, z={F(Math.Round(z, 1), "0.0")}."), Numeric);
        Assert.DoesNotContain(Check(c, $"The shift is strong (z={F(Math.Round(z, 1), "0.0")} > {F(Z(c).Threshold!.Value)})."), Numeric);
    }

    [Fact]
    public void Compact_z_at_full_precision_is_grounded()
    {
        var c = Shift();
        Assert.DoesNotContain(Check(c, $"The shift is strong, z={F(Z(c).Current, "0.00")}."), Numeric);
    }

    [Fact]
    public void Unsupported_or_misrounded_z_is_rejected()
    {
        var c = Shift();
        var z = Z(c).Current;
        Assert.Contains(Check(c, "The shift is strong, z=4.9."), i => i.Code == IssueCodes.UnsupportedNumber);
        Assert.Contains(Check(c, $"The shift is strong, z={F(Math.Round(z, 1) - 0.1, "0.0")}."), i => i.Code == IssueCodes.UnsupportedNumber);
    }

    [Fact]
    public void A_decimal_from_another_metric_is_not_a_z_score()
    {
        // The goal context has a chance-quality value (e.g. 0.32) but no test statistic: "z=0.32" must still fail.
        var c = Goal("en");
        var quality = c.DeterministicMetrics.First(m => m.Key == "ChanceQuality").Current;
        Assert.DoesNotContain(c.DeterministicMetrics, m => m.Unit == "Statistic");
        Assert.Contains(Check(c, $"The chance was rated z={F(quality)}."), i => i.Code == IssueCodes.UnsupportedNumber);
    }

    // ---------------------------------------------------------------- French match minutes

    [Fact]
    public void French_ordinal_minutes_and_ranges_are_grounded()
    {
        var c = Shift("fr");
        var w = Primary(c).CurrentWindow!;
        Assert.DoesNotContain(Check(c, $"Westhaven attaque par la droite après la {w.FromMinute}e minute."), Numeric);
        Assert.DoesNotContain(Check(c, $"Entre la {w.FromMinute}e et la {w.ToMinute}e minute, Westhaven attaque à droite."), Numeric);
    }

    [Fact]
    public void French_minute_without_the_word_minute_is_grounded()
    {
        var c = Goal("fr");
        Assert.DoesNotContain(Check(c, $"But à la {c.DisplayMinute}e : Owen Blackwood marque."), Numeric);
    }

    [Fact]
    public void Future_French_minute_is_rejected()
    {
        var c = Goal("fr");
        Assert.Contains(Check(c, "But à la 90e : Owen Blackwood marque."), i => i.Code == IssueCodes.FutureTime);
        Assert.Contains(Check(c, "Il marque à la 90e minute."), i => i.Code == IssueCodes.FutureTime);
        var shift = Shift("fr");
        Assert.Contains(Check(shift, $"Entre la {Primary(shift).CurrentWindow!.FromMinute}e et la 88e minute, Westhaven attaque."), i => i.Code == IssueCodes.FutureTime);
    }

    [Fact]
    public void French_ordinal_that_is_not_a_time_is_not_read_as_a_minute()
    {
        var c = Goal("fr");
        Assert.DoesNotContain(Check(c, "C'est la 2e tentative de Northbridge."), IsTime);
        Assert.DoesNotContain(Check(c, "Le 3e joueur arrive."), IsTime);
    }

    // ---------------------------------------------------------------- Spanish match minutes

    [Fact]
    public void Spanish_minutes_are_grounded()
    {
        var c = Goal("es");
        Assert.DoesNotContain(Check(c, $"Gol de Owen Blackwood a los {c.DisplayMinute} minutos."), Numeric);
        Assert.DoesNotContain(Check(c, $"Gol de Owen Blackwood en el minuto {c.DisplayMinute}."), Numeric);
    }

    [Fact]
    public void Future_or_invented_Spanish_minute_is_rejected()
    {
        var c = Goal("es");
        Assert.Contains(Check(c, "Gol de Owen Blackwood a los 80 minutos."), i => i.Code == IssueCodes.FutureTime);
        Assert.Contains(Check(c, "Gol de Owen Blackwood en el minuto 80."), i => i.Code == IssueCodes.FutureTime);
        Assert.Contains(Check(c, "Gol de Owen Blackwood a los 12 minutos."), IsTime);
    }

    // ---------------------------------------------------------------- Spanish sample size

    [Fact]
    public void Spanish_sample_size_is_grounded_by_the_sample_field()
    {
        var c = Shift("es");
        Assert.DoesNotContain(Check(c, $"El cambio es fuerte, con muestra de {Primary(c).SampleSize} ataques."), Numeric);
        Assert.DoesNotContain(Check(c, $"Con una muestra de {Primary(c).BaselineSampleSize} ataques antes."), Numeric);
    }

    [Fact]
    public void Spanish_sample_size_must_be_a_sample_not_any_value()
    {
        var c = Shift("es");
        Assert.Contains(Check(c, "El cambio es fuerte, con muestra de 17 ataques."), i => i.Code == IssueCodes.UnsupportedNumber);
        // The right-attack COUNT is a real metric value, but it is not a sample size.
        var count = c.DeterministicMetrics.First(m => m.Unit == "Count" && !c.DeterministicMetrics.Any(o => o.SampleSize == (int)m.Current || o.BaselineSampleSize == (int)m.Current)).Current;
        Assert.Contains(Check(c, $"El cambio es fuerte, con muestra de {F(count)}."), i => i.Code == IssueCodes.UnsupportedNumber);
    }

    // ---------------------------------------------------------------- compound counts

    [Fact]
    public void Compound_pass_counts_are_grounded_when_both_are_pass_samples()
    {
        var c = LongBalls;
        var m = Primary(c);
        Assert.Equal("LongBallShare", m.Key);
        Assert.DoesNotContain(Check(c, $"Sample sizes of {m.BaselineSampleSize} and {m.SampleSize} passes meet the minimum."), Numeric);
    }

    [Fact]
    public void Compound_counts_reject_one_unsupported_value()
    {
        var c = LongBalls;
        var m = Primary(c);
        Assert.Contains(Check(c, $"Sample sizes of {m.BaselineSampleSize} and {m.SampleSize + 1} passes meet the minimum."), i => i.Code == IssueCodes.UnsupportedNumber);
        Assert.Contains(Check(c, $"Sample sizes of {m.BaselineSampleSize + 1} and {m.SampleSize} passes meet the minimum."), i => i.Code == IssueCodes.UnsupportedNumber);
    }

    [Fact]
    public void A_bare_pair_without_a_counted_noun_is_not_accepted_as_counts()
    {
        var c = LongBalls;
        var m = Primary(c);
        Assert.Contains(Check(c, $"The figures were {m.BaselineSampleSize} and {m.SampleSize}."),
            i => i.Code == IssueCodes.UnsupportedNumber && i.Message.Contains($"'{m.BaselineSampleSize}'"));
    }

    [Fact]
    public void Passes_are_not_grounded_by_an_attack_sample()
    {
        // The attack-shift sample counts attacks, not passes.
        var c = Shift();
        Assert.Contains(Check(c, $"It is based on {Primary(c).SampleSize} passes."), i => i.Code == IssueCodes.UnsupportedNumber);
    }

    // ---------------------------------------------------------------- regressions

    [Fact]
    public void Existing_rejections_still_hold()
    {
        var c = Shift();
        Assert.Contains(Check(c, "The move involved 8 key players."), i => i.Code == IssueCodes.UnsupportedNumber);
        Assert.DoesNotContain(Check(c, "Substitute WHA-17 came on."), Numeric);
        Assert.Contains(Check(c, "Possession rose by 17%."), i => i.Code == IssueCodes.UnsupportedNumber);
        Assert.DoesNotContain(Check(c, $"With the score {c.Score.Home}-{c.Score.Away}, they pushed on."), Numeric);
        Assert.Contains(Check(c, $"The score is {c.Score.Home + 2}-{c.Score.Away}."), i => i.Code == IssueCodes.WrongScore);
    }
}
