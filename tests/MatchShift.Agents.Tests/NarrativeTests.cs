using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
using MatchShift.Analytics;
using MatchShift.Analytics.Story;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Clubs;

namespace MatchShift.Agents.Tests;

public class NarrativeTests
{
    private static readonly Match Flagship = SyntheticMatchGenerator.GenerateFlagship().Match;
    private static readonly LiveMatchAnalyzer Analysis = LiveMatchAnalyzer.AnalyzeFull(Flagship);
    private static readonly MatchRoster Roster = new(Flagship);
    private static readonly string[] Languages = ["en", "fr", "es", "de", "it", "pt"];

    public static TheoryData<string, ViewerMode> LanguagesAndModes()
    {
        var data = new TheoryData<string, ViewerMode>();
        foreach (var l in Languages)
            foreach (var m in Enum.GetValues<ViewerMode>()) data.Add(l, m);
        return data;
    }

    private static NarrativeContext Context(string language, ViewerMode mode) =>
        new(Roster, new ViewerProfile(mode, language, ClubCatalog.WesthavenId, "WHA-17"));

    [Theory]
    [MemberData(nameof(LanguagesAndModes))]
    public void Every_insight_renders_with_no_missing_slots(string language, ViewerMode mode)
    {
        foreach (var insight in Analysis.Insights)
        {
            var n = TemplateInsightProvider.Narrate(insight, Context(language, mode));
            Assert.True(n.MissingSlots.Count == 0, $"{language}/{mode} {insight.Type.Code()}: missing {string.Join(",", n.MissingSlots)}");
            Assert.False(string.IsNullOrWhiteSpace(n.Headline));
            Assert.False(string.IsNullOrWhiteSpace(n.Fact));
            Assert.DoesNotContain("{", n.Fact);
            if (mode == ViewerMode.TeachMe) Assert.NotNull(n.Teaching);
            else Assert.Null(n.Teaching);
        }
    }

    [Theory]
    [MemberData(nameof(LanguagesAndModes))]
    public void Template_narratives_pass_verification(string language, ViewerMode mode)
    {
        var verifier = new VerificationAgent();
        var lex = LexiconRegistry.Get(language);
        foreach (var insight in Analysis.Insights)
        {
            var context = Context(language, mode);
            var n = TemplateInsightProvider.Narrate(insight, context);
            var slots = NarrativeSlots.ForInsight(insight, Roster, lex, context.Profile);
            var allowed = VerificationAgent.AllowedNumbers(insight, slots)
                .Append(TemplateInsightProvider.Involvement(insight, Flagship, "WHA-17"));
            var result = verifier.Verify(n.Sentences(), allowed);
            Assert.True(result.Approved, $"{language}/{mode} {insight.Type.Code()}: {string.Join(" | ", result.Issues.Select(i => i.Problem + " in: " + i.Sentence))}");
        }
    }

    [Fact]
    public void Facts_carry_identical_numbers_in_every_language()
    {
        foreach (var insight in Analysis.Insights)
        {
            var numbers = Languages
                .Select(l => VerificationAgent.ExtractNumbers(TemplateInsightProvider.Narrate(insight, Context(l, ViewerMode.Analyst)).Fact)
                    .OrderBy(x => x).ToList())
                .ToList();
            for (var i = 1; i < numbers.Count; i++) Assert.Equal(numbers[0], numbers[i]);
        }
    }

    [Fact]
    public void Every_lexicon_has_exactly_the_English_template_keys_and_no_corrupted_characters()
    {
        var english = LexiconRegistry.Get("en").Keys.ToHashSet();
        Assert.Equal(Languages.Order(), LexiconRegistry.Supported.Select(l => l.Code).Order());
        foreach (var lex in LexiconRegistry.Supported)
        {
            var keys = lex.Keys.ToHashSet();
            Assert.True(english.SetEquals(keys), $"{lex.Code}: missing [{string.Join(", ", english.Except(keys))}] extra [{string.Join(", ", keys.Except(english))}]");
            foreach (var key in keys)
            {
                var text = lex.Get(key);
                // A '?' between letters, or as an arrow between two values, is an encoding casualty (e.g. "pass?e", "{from} ? {to}").
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, @"\p{L}\?\p{L}|\} \? \{"), $"{lex.Code}.{key}: {text}");
            }
        }
    }

    [Fact]
    public void New_languages_use_their_own_culture_and_minute_wording()
    {
        Assert.Equal("de-DE", LexiconRegistry.Get("de").Culture.Name);
        Assert.Equal("it-IT", LexiconRegistry.Get("it").Culture.Name);
        Assert.Equal("pt-PT", LexiconRegistry.Get("pt").Culture.Name);
        Assert.Equal("50. Minute", NarrativeSlots.Minute(LexiconRegistry.Get("de"), 50));
        Assert.Equal("50° minuto", NarrativeSlots.Minute(LexiconRegistry.Get("it"), 50));
        Assert.Equal("minuto 50", NarrativeSlots.Minute(LexiconRegistry.Get("pt"), 50));
        Assert.Equal("Golo", LexiconRegistry.Get("pt").Get("type.GOAL"));               // European Portuguese, not "Gol"
    }

    [Fact]
    public void Same_insight_reads_differently_but_cites_the_same_evidence()
    {
        var shift = Analysis.Insights.First(i => i.Type == InsightType.AttackChannelShift);
        var en = TemplateInsightProvider.Narrate(shift, Context("en", ViewerMode.Analyst));
        var fr = TemplateInsightProvider.Narrate(shift, Context("fr", ViewerMode.Analyst));
        var es = TemplateInsightProvider.Narrate(shift, Context("es", ViewerMode.Analyst));
        Assert.Contains("down the right", en.Headline);
        Assert.Contains("droit", fr.Headline);
        Assert.Contains("derecha", es.Headline);
        Assert.Contains("%", en.Fact);
        Assert.Contains(" %", fr.Fact);
        Assert.Equal(en.Evidence.Select(e => e.Metric), fr.Evidence.Select(e => e.Metric));
        Assert.Contains("Westhaven", es.Fact);
    }

    [Fact]
    public void Explanations_are_cautious_and_separate_from_facts()
    {
        foreach (var insight in Analysis.Insights.Where(i => i.InterpretationCode is not null))
        {
            var n = TemplateInsightProvider.Narrate(insight, Context("en", ViewerMode.Analyst));
            Assert.NotEqual(n.Fact, n.Explanation);
        }
        var shift = Analysis.Insights.First(i => i.Type == InsightType.AttackChannelShift);
        var text = TemplateInsightProvider.Narrate(shift, Context("en", ViewerMode.Analyst)).Explanation!;
        Assert.Contains("may", text);
    }

    [Fact]
    public void My_team_mode_frames_insights_from_the_focus_team()
    {
        var shift = Analysis.Insights.First(i => i.Type == InsightType.AttackChannelShift && i.TeamId == ClubCatalog.WesthavenId);
        var forWesthaven = TemplateInsightProvider.Narrate(shift, new NarrativeContext(Roster, new ViewerProfile(ViewerMode.MyTeam, "en", ClubCatalog.WesthavenId)));
        var forNorthbridge = TemplateInsightProvider.Narrate(shift, new NarrativeContext(Roster, new ViewerProfile(ViewerMode.MyTeam, "en", ClubCatalog.NorthbridgeId)));
        Assert.StartsWith("Good news", forWesthaven.Perspective);
        Assert.StartsWith("A warning sign", forNorthbridge.Perspective);
    }

    [Fact]
    public void What_matters_mode_shows_fewer_insights()
    {
        var all = NarrativeFilter.Select(Analysis.Insights, new ViewerProfile(ViewerMode.Analyst));
        var matters = NarrativeFilter.Select(Analysis.Insights, new ViewerProfile(ViewerMode.WhatMatters));
        Assert.True(matters.Count < all.Count);
        Assert.All(matters, i => Assert.True(i.Importance >= NarrativeFilter.WhatMattersMinImportance));
        Assert.Contains(matters, i => i.Type == InsightType.Goal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    [InlineData("it")]
    [InlineData("pt")]
    public void Recaps_render_in_every_style_and_pass_verification(string language)
    {
        var verifier = new VerificationAgent();
        foreach (var period in new[] { RecapPeriod.HalfTime, RecapPeriod.FullTime })
        {
            var facts = RecapBuilder.Build(Analysis, period);
            var allowed = RecapEvidence.AllowedNumbers(facts, Analysis.Insights, Roster).ToList();
            foreach (var style in Enum.GetValues<RecapStyle>())
            {
                var recap = RecapNarrator.Narrate(facts, style, new NarrativeContext(Roster, new ViewerProfile(Language: language)), Analysis.Insights);
                Assert.False(string.IsNullOrWhiteSpace(recap.Text));
                Assert.DoesNotContain("{", recap.Text);
                var result = verifier.Verify(recap.Sections.SelectMany(s => s.Paragraphs), allowed);
                Assert.True(result.Approved, $"{language} {period} {style}: {string.Join(" | ", result.Issues.Select(i => i.Problem + " in: " + i.Sentence))}");
                if (style == RecapStyle.Social) Assert.True(recap.Text.Length <= RecapNarrator.SocialMaxLength);
            }
        }
    }

    [Fact]
    public void Full_time_casual_recap_tells_the_story()
    {
        var facts = RecapBuilder.Build(Analysis, RecapPeriod.FullTime);
        var recap = RecapNarrator.Narrate(facts, RecapStyle.Casual, new NarrativeContext(Roster, ViewerProfile.Default), Analysis.Insights);
        Assert.Contains("2-2", recap.Text);
        Assert.Contains("Tavian Brooks", RecapNarrator.Narrate(facts, RecapStyle.Analyst, new NarrativeContext(Roster, ViewerProfile.Default), Analysis.Insights).Text);
        Assert.Contains("synthetic", recap.Text);
    }

    [Fact]
    public void Verification_rejects_invented_numbers_and_intent()
    {
        var verifier = new VerificationAgent(["Ravensmoor"]);
        var ok = verifier.Verify(["Right-side attacks rose from 31% to 90%."], [0.31, 0.9, 31, 90]);
        Assert.True(ok.Approved);

        var invented = verifier.Verify(["Right-side attacks rose from 31% to 75%."], [0.31, 0.9, 31, 90]);
        Assert.False(invented.Approved);

        var intent = verifier.Verify(["The manager decided to attack down the right."], []);
        Assert.False(intent.Approved);

        var wrongClub = verifier.Verify(["Ravensmoor are pressing."], []);
        Assert.False(wrongClub.Approved);
    }

    [Theory]
    [InlineData("from 31 % to 90 %", new[] { 31.0, 90.0 })]
    [InlineData("qualité 0,36 à la 63e", new[] { 0.36, 63.0 })]
    [InlineData("WHA-17 ignored? 12 s", new[] { 17.0, 12.0 })]
    public void Number_extraction_is_culture_agnostic(string text, double[] expected) =>
        Assert.Equal(expected, VerificationAgent.ExtractNumbers(text));
}
