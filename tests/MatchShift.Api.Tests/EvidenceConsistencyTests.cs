using System.Net.Http.Json;
using System.Text.Json;
using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Analytics;
using MatchShift.Api.Contracts;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Scenarios;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MatchShift.Api.Tests;

/// <summary>
/// The card's supporting-event count, WHY?, SHOW ME, the Evidence Replay, the agents' InsightContext and the broadcast
/// overlay must all agree on what the supporting evidence is. Context-only events may be drawn, but never counted.
/// </summary>
public class EvidenceConsistencyTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Flagship = "MS-FLAGSHIP-001-I015";
    private const string ContextEvent = "MS-FLAGSHIP-001-E00797";
    private readonly HttpClient Client = factory.CreateClient();
    private readonly string Session = Guid.NewGuid().ToString();

    private string Url(string path) => $"{path}{(path.Contains('?') ? '&' : '?')}session={Session}";

    private async Task<JsonElement> Get(string path)
    {
        var res = await Client.GetAsync(Url(path));
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }

    /// <summary>The same jump the guided judge demo makes: flagship match, paused at 64:00.</summary>
    private async Task JumpToFlagshipMoment()
    {
        (await Client.PostAsJsonAsync(Url("/api/demo/reset"), new { matchId = "MS-FLAGSHIP-001" })).EnsureSuccessStatusCode();
        (await Client.PostAsJsonAsync(Url("/api/demo/seek"), new { minute = 64 })).EnsureSuccessStatusCode();
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    private static MatchShift.Core.Insights.Insight AnalyzerInsight() =>
        LiveMatchAnalyzer.AnalyzeFull(SyntheticMatchGenerator.GenerateFlagship().Match).Insights.Single(i => i.Id == Flagship);

    [Fact]
    public async Task Guided_demo_jump_reaches_the_same_flagship_insight()
    {
        await JumpToFlagshipMoment();
        var insights = await Get("/api/demo/insights?lang=en&mode=Analyst");
        var shift = insights.EnumerateArray().Single(i => i.GetProperty("type").GetString() == "ATTACK_CHANNEL_SHIFT" && Math.Abs(i.GetProperty("minute").GetInt32() - 63) <= 2);
        Assert.Equal(Flagship, shift.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Card_and_WHY_count_equal_the_authoritative_supporting_event_ids()
    {
        var authoritative = AnalyzerInsight().SupportingEventIds;
        Assert.Equal(authoritative.Count, authoritative.Distinct().Count());           // no duplicate evidence IDs
        Assert.Equal(30, authoritative.Count);

        await JumpToFlagshipMoment();
        var card = (await Get("/api/demo/insights?lang=en&mode=Analyst")).EnumerateArray().Single(i => i.GetProperty("id").GetString() == Flagship);
        Assert.Equal(authoritative.Count, card.GetProperty("supportingEventCount").GetInt32());
        var why = await Get($"/api/demo/insights/{Flagship}/evidence?lang=en");
        Assert.Equal(authoritative.Count, why.GetProperty("insight").GetProperty("supportingEventCount").GetInt32());
        Assert.Equal([ContextEvent], Strings(card.GetProperty("contextEventIds")));
    }

    [Fact]
    public async Task SHOW_ME_separates_the_30_supporting_events_from_the_one_context_event()
    {
        var authoritative = AnalyzerInsight().SupportingEventIds.ToHashSet();
        await JumpToFlagshipMoment();
        var show = await Get($"/api/demo/insights/{Flagship}/events");
        string[] drawn = [.. show.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("id").GetString()!)];
        var supporting = Strings(show.GetProperty("supportingEventIds"));
        var context = Strings(show.GetProperty("contextEventIds"));

        Assert.Equal(drawn.Length, drawn.Distinct().Count());                          // nothing drawn twice
        Assert.Equal(authoritative.Count, supporting.Length);                          // SHOW ME's supporting count = card count
        Assert.True(authoritative.SetEquals(supporting));
        Assert.Equal([ContextEvent], context);                                         // the 31st drawn event is context
        Assert.Empty(context.Intersect(supporting));                                   // context is never counted as evidence
        Assert.Equal(supporting.Length + context.Length, drawn.Length);
        Assert.True(drawn.ToHashSet().SetEquals(supporting.Concat(context)));

        // The context event is the one recent Westhaven attack that did not use the right channel, drawn de-emphasised.
        var layers = show.GetProperty("layers").EnumerateArray().ToList();
        var other = layers.Single(l => l.GetProperty("key").GetString() == "otherAttacks");
        Assert.False(other.GetProperty("emphasised").GetBoolean());
        Assert.Contains(ContextEvent, Strings(other.GetProperty("eventIds")));
    }

    [Fact]
    public async Task Evidence_Replay_flags_only_supporting_events_as_evidence()
    {
        var authoritative = AnalyzerInsight().SupportingEventIds.ToHashSet();
        await JumpToFlagshipMoment();
        var replay = await Get($"/api/demo/insights/{Flagship}/replay");
        var evidenceIds = Strings(replay.GetProperty("evidenceEventIds"));
        Assert.True(evidenceIds.ToHashSet().IsSubsetOf(authoritative));
        Assert.DoesNotContain(ContextEvent, evidenceIds);
        Assert.Equal([ContextEvent], Strings(replay.GetProperty("contextEventIds")));
        foreach (var clip in replay.GetProperty("clips").EnumerateArray())
        foreach (var e in clip.GetProperty("events").EnumerateArray())
        {
            var id = e.GetProperty("eventId").GetString()!;
            Assert.Equal(authoritative.Contains(id), e.GetProperty("isEvidence").GetBoolean());
            Assert.Equal(id == ContextEvent, e.GetProperty("isContext").GetBoolean());
        }
    }

    [Fact]
    public void Agent_InsightContext_uses_only_supporting_evidence()
    {
        var match = SyntheticMatchGenerator.GenerateFlagship().Match;
        var insight = AnalyzerInsight();
        var context = InsightContextBuilder.Build(insight, match, new ViewerProfile(ViewerMode.Analyst, "en"), AccessibilityRequirements.All, "wf-test");
        Assert.DoesNotContain(ContextEvent, context.SupportingEventIds);
        Assert.True(context.SupportingEventIds.ToHashSet().IsSubsetOf(insight.SupportingEventIds));
    }

    [Fact]
    public void Every_insight_in_every_catalog_match_keeps_evidence_and_context_apart()
    {
        var matches = new[]
        {
            SyntheticMatchGenerator.GenerateFlagship().Match,
            SyntheticMatchGenerator.GenerateScenario(ScenarioKind.SustainedPressure, 4101).Match,
            SyntheticMatchGenerator.GenerateScenario(ScenarioKind.AttackChannelShift, 6035).Match,
            SyntheticMatchGenerator.GenerateScenario(ScenarioKind.ChaoticFinish, 9502).Match,
            SyntheticMatchGenerator.GenerateScenario(ScenarioKind.PassingStyleShift, 7001).Match,
        };
        foreach (var match in matches)
        foreach (var insight in LiveMatchAnalyzer.AnalyzeFull(match).Insights)
        {
            Assert.Equal(insight.SupportingEventIds.Count, insight.SupportingEventIds.Distinct().Count());
            Assert.Empty(insight.ContextEventIds.Intersect(insight.SupportingEventIds));
            var card = SnapshotBuilder.Card(insight, new NarrativeContext(new MatchRoster(match), new ViewerProfile(ViewerMode.Analyst, "en")));
            Assert.Equal(insight.SupportingEventIds.Count, card.SupportingEventCount);
            var replay = MatchShift.Analytics.Replay.ReplayBuilder.ForInsight(match, insight);
            Assert.Empty(replay.EvidenceEventIds.Except(insight.SupportingEventIds));
            Assert.All(replay.Clips.SelectMany(c => c.Events).Where(e => e.IsEvidence), e => Assert.False(e.IsContext));
        }
    }

    [Fact]
    public async Task Broadcast_overlay_evidence_references_exclude_context_events()
    {
        // The overlay shows an insight for 120 match seconds after detection (62:00), so read it at 63:00.
        (await Client.PostAsJsonAsync(Url("/api/demo/reset"), new { matchId = "MS-FLAGSHIP-001" })).EnsureSuccessStatusCode();
        (await Client.PostAsJsonAsync(Url("/api/demo/seek"), new { minute = 63 })).EnsureSuccessStatusCode();
        var overlay = (await Get("/api/demo/overlay?lang=en")).GetProperty("overlay");
        Assert.Equal(Flagship, overlay.GetProperty("insightId").GetString());
        var refs = Strings(overlay.GetProperty("evidenceReferences"));
        Assert.DoesNotContain(ContextEvent, refs);
        Assert.True(refs.ToHashSet().IsSubsetOf(AnalyzerInsight().SupportingEventIds));
    }
}
