using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MatchShift.Api.Playback;
using MatchShift.Core.Domain;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MatchShift.Api.Tests;

public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient Client = factory.CreateClient();

    private async Task<JsonElement> Get(string url)
    {
        var res = await Client.GetAsync(url);
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Health_and_meta_report_synthetic_data_and_template_provider()
    {
        var health = await Get("/health");
        Assert.True(health.GetProperty("synthetic").GetBoolean());
        var meta = await Get("/api/meta");
        Assert.Equal("template", meta.GetProperty("aiProvider").GetString());
        // Same six codes, in the same order, as src/MatchShift.Web/src/languages.ts.
        Assert.Equal(["en", "fr", "es", "de", "it", "pt"], meta.GetProperty("languages").EnumerateArray().Select(l => l.GetProperty("code").GetString()));
        Assert.Contains("synthetic", meta.GetProperty("dataNotice").GetString());
    }

    [Fact]
    public async Task Matches_are_listed_and_flagship_has_squads()
    {
        var matches = await Get("/api/matches");
        Assert.True(matches.GetArrayLength() >= 2);
        var detail = await Get("/api/matches/MS-FLAGSHIP-001");
        Assert.Equal(18, detail.GetProperty("homeSquad").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/matches/NOPE")).StatusCode);
    }

    [Fact]
    public async Task Seek_back_replays_to_exactly_the_same_analytics()
    {
        await Client.PostAsJsonAsync("/api/demo/reset", new { matchId = "MS-FLAGSHIP-001" });
        await Client.PostAsJsonAsync("/api/demo/seek", new { minute = 70 });
        var at70 = await Get("/api/demo/state");
        await Client.PostAsJsonAsync("/api/demo/seek", new { minute = 80 });
        await Client.PostAsJsonAsync("/api/demo/seek", new { minute = 70 });
        var again = await Get("/api/demo/state");

        Assert.Equal(70 * 60, again.GetProperty("clock").GetProperty("second").GetInt32());
        Assert.Equal(Ids(at70), Ids(again));
        Assert.Equal(at70.GetProperty("score").ToString(), again.GetProperty("score").ToString());
        Assert.Contains("ATTACK_CHANNEL_SHIFT", Types(again));

        static string[] Ids(JsonElement s) => s.GetProperty("insights").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToArray();
        static string[] Types(JsonElement s) => s.GetProperty("insights").EnumerateArray().Select(i => i.GetProperty("type").GetString()!).ToArray();
    }

    [Fact]
    public void Playback_advances_the_clock_at_the_selected_speed()
    {
        var session = new DemoSession(new MatchCatalog());
        session.SetSpeed(10);
        session.Play();
        session.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal((int)(2 * DemoSession.MatchSecondsPerRealSecondAt1x * 10), session.ClockSecond);
        session.Pause();
        session.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal(600, session.ClockSecond);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetSpeed(3));
    }

    [Fact]
    public void Playback_stops_at_full_time()
    {
        var session = new DemoSession(new MatchCatalog());
        session.SetSpeed(10);
        session.Play();
        session.Tick(TimeSpan.FromMinutes(1));
        Assert.Equal(MatchClock.FullTimeSecond, session.ClockSecond);
        Assert.False(session.IsPlaying);
        Assert.True(session.IsFinished);
    }

    [Fact]
    public async Task Insight_evidence_events_and_agent_trace_are_served()
    {
        await Client.PostAsJsonAsync("/api/demo/reset", new { matchId = "MS-FLAGSHIP-001" });
        await Client.PostAsJsonAsync("/api/demo/seek", new { minute = 66 });
        var insights = await Get("/api/demo/insights?lang=es&mode=Studio");
        var shift = insights.EnumerateArray().First(i => i.GetProperty("type").GetString() == "ATTACK_CHANNEL_SHIFT");
        var id = shift.GetProperty("id").GetString();
        Assert.Equal("es", shift.GetProperty("narrative").GetProperty("language").GetString());

        var evidence = await Get($"/api/demo/insights/{id}/evidence?lang=fr");
        Assert.Equal("Pourquoi cela a été détecté", evidence.GetProperty("whyTitle").GetString());

        var events = await Get($"/api/demo/insights/{id}/events");
        Assert.True(events.GetProperty("events").GetArrayLength() > 5);

        // The older deterministic narration pipeline is still served, under an honest name.
        var trace = await Get($"/api/demo/insights/{id}/narration-trace");
        Assert.Equal(6, trace.GetProperty("steps").GetArrayLength());
        Assert.Equal("Approved", trace.GetProperty("steps")[5].GetProperty("status").GetString());

        // /agents is now the Microsoft Agent Framework workflow (OFFLINE_TEMPLATE in tests: no model is called).
        var workflow = await Get($"/api/demo/insights/{id}/agents");
        Assert.Equal("OFFLINE_TEMPLATE", workflow.GetProperty("executionMode").GetString());
    }

    [Fact]
    public async Task Recap_is_locked_until_play_reaches_it()
    {
        await Client.PostAsJsonAsync("/api/demo/reset", new { matchId = "MS-FLAGSHIP-001" });
        await Client.PostAsJsonAsync("/api/demo/seek", new { minute = 30 });
        Assert.Equal(HttpStatusCode.Conflict, (await Client.GetAsync("/api/demo/recap/halftime")).StatusCode);

        await Client.PostAsJsonAsync("/api/demo/seek", new { minute = 90 });
        var recap = await Get("/api/demo/recap/fulltime?style=Broadcast&lang=en");
        var text = recap.GetProperty("narrative").GetProperty("sections").ToString();
        Assert.Contains("[OPEN]", text);
        Assert.Contains("2-2", text);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/demo/recap/extratime")).StatusCode);
    }

    [Fact]
    public async Task Invalid_speed_is_rejected()
    {
        var res = await Client.PostAsJsonAsync("/api/demo/speed", new { speed = 7 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Replay_endpoint_reconstructs_the_selected_insight()
    {
        await Client.PostAsJsonAsync("/api/demo/reset", new { matchId = "MS-FLAGSHIP-001" });
        await Client.PostAsJsonAsync("/api/demo/seek", new { minute = 66 });
        var insights = await Get("/api/demo/insights");
        var turnover = insights.EnumerateArray().First(i => i.GetProperty("type").GetString() == "DANGEROUS_TURNOVER" && i.GetProperty("minute").GetInt32() == 35);
        var id = turnover.GetProperty("id").GetString();

        var replay = await Get($"/api/demo/insights/{id}/replay");
        Assert.Equal(id, replay.GetProperty("insightId").GetString());
        var clip = replay.GetProperty("clips")[0];
        Assert.Equal(22, clip.GetProperty("tracks").GetArrayLength());
        var labels = clip.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("label").GetString()).ToList();
        Assert.Contains("HIGH_PRESS_INTERCEPTION", labels);
        Assert.Contains("GOAL", labels);
        Assert.Contains(clip.GetProperty("tracks")[0].GetProperty("samples").EnumerateArray(),
            s => s.GetProperty("provenance").GetString() is "ESTIMATED_POSITION" or "INTERPOLATED_POSITION" or "ACTUAL_EVENT_POSITION");

        var shift = insights.EnumerateArray().First(i => i.GetProperty("type").GetString() == "ATTACK_CHANNEL_SHIFT");
        var shiftReplay = await Get($"/api/demo/insights/{shift.GetProperty("id").GetString()}/replay");
        Assert.Equal(["before", "after"], shiftReplay.GetProperty("clips").EnumerateArray().Select(c => c.GetProperty("key").GetString()));

        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/demo/insights/NOPE/replay")).StatusCode);
    }
}
