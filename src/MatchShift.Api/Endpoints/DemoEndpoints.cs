using System.Text.Json;
using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
using MatchShift.Analytics.Story;
using MatchShift.Api.Contracts;
using MatchShift.Api.Playback;
using MatchShift.Core.Insights;

namespace MatchShift.Api.Endpoints;

public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapMatchShiftApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/meta", (NarrationService narration, IConfiguration config) => new MetaDto(
            LexiconRegistry.Supported.Select(l => new LanguageDto(l.Code, l.Name)).ToList(),
            Enum.GetNames<ViewerMode>(),
            Enum.GetNames<RecapStyle>(),
            DemoSession.AllowedSpeeds,
            narration.ProviderName,
            narration.UsesAi,
            "All football data in MatchShift is synthetic: fictional clubs, players, matches and events.",
            config["Narration:Provider"] ?? (config.GetValue<bool>("Foundry:Enabled") ? "foundry" : "template"),
            narration.ProviderName switch { "mock" => "Offline protocol simulator; no model calls.",
                "microsoft-foundry" => "Configured; availability is established only by an accepted response.",
                _ => "Offline templates. Foundry may be disabled or missing endpoint/deployment configuration." },
            MatchShift.Infrastructure.Foundry.FoundryInsightProvider.PromptVersion));

        api.MapGet("/matches", (MatchCatalog catalog) => catalog.All.Select(m => SnapshotBuilder.Summary(m.Match)));

        api.MapGet("/matches/{id}", (string id, MatchCatalog catalog) =>
            catalog.Find(id) is { } m
                ? Results.Ok(new MatchDetailDto(SnapshotBuilder.Summary(m.Match), m.Match.Venue, m.Match.Competition,
                    m.Match.HomeTeam.Squad.Select(SnapshotBuilder.Player).ToList(), m.Match.AwayTeam.Squad.Select(SnapshotBuilder.Player).ToList()))
                : Results.NotFound());

        var demo = api.MapGroup("/demo");

        demo.MapPost("/reset", (ResetRequest? request, DemoSession session, MatchCatalog catalog) =>
        {
            if (request?.MatchId is { } id)
            {
                if (catalog.Find(id) is not { } match) return Results.NotFound();
                session.Load(match.Match);
            }
            else session.Reset();
            return Results.NoContent();
        });
        demo.MapPost("/play", (DemoSession s) => { s.Play(); return Results.NoContent(); });
        demo.MapPost("/pause", (DemoSession s) => { s.Pause(); return Results.NoContent(); });
        demo.MapPost("/speed", (SpeedRequest r, DemoSession s) =>
        {
            if (!DemoSession.AllowedSpeeds.Contains(r.Speed)) return Results.BadRequest($"Speed must be one of {string.Join(", ", DemoSession.AllowedSpeeds)}.");
            s.SetSpeed(r.Speed);
            return Results.NoContent();
        });
        demo.MapPost("/seek", (SeekRequest r, DemoSession s) => { s.Seek((int)Math.Round(r.Minute * 60)); return Results.NoContent(); });

        demo.MapGet("/state", (string? lang, string? mode, string? team, string? player, DemoSession s) =>
            Snapshot(s, SnapshotBuilder.Profile(lang, mode, team, player)));
        demo.MapGet("/overlay", (string? lang, DemoSession s) =>
        {
            var snap = Snapshot(s, SnapshotBuilder.Profile(lang, "Studio", null, null));
            return Results.Ok(new { snap.SessionId, snap.Epoch, snap.MatchId, snap.Clock, overlay = snap.Overlay });
        });

        demo.MapGet("/stream", async (string? lang, string? mode, string? team, string? player, DemoSession s, HttpContext http,
            IConfiguration config, CancellationToken ct) =>
        {
            var profile = SnapshotBuilder.Profile(lang, mode, team, player);
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            var options = http.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
            long lastVersion = -1;
            var lastSent = DateTime.MinValue;
            while (!ct.IsCancellationRequested)
            {
                if (s.Version != lastVersion || DateTime.UtcNow - lastSent > TimeSpan.FromSeconds(10))
                {
                    lastVersion = s.Version;
                    lastSent = DateTime.UtcNow;
                    var json = JsonSerializer.Serialize(Snapshot(s, profile), options);
                    await http.Response.WriteAsync($"event: snapshot\ndata: {json}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                }
                await Task.Delay(250, ct);
            }
        });

        demo.MapGet("/events", (int? since, DemoSession s) => s.Read((a, _) =>
            a.State.Events.Where(e => e.SequenceNumber > (since ?? 0)).Select(e => SnapshotBuilder.Event(e, s.Match)).ToList()));

        demo.MapGet("/analytics", (DemoSession s) => s.Read((a, clock) =>
        {
            var snap = SnapshotBuilder.Build(a, clock, s.Match, ViewerProfile.Default, s.IsPlaying, s.Speed);
            return new { snap.Clock, snap.Score, snap.Metrics, snap.Timeline };
        }));

        demo.MapGet("/insights", (string? lang, string? mode, string? team, string? player, DemoSession s) =>
            Snapshot(s, SnapshotBuilder.Profile(lang, mode, team, player)).Insights);

        demo.MapGet("/highlights", (string? lang, string? mode, DemoSession s) =>
            Snapshot(s, SnapshotBuilder.Profile(lang, mode, null, null)).Highlights);

        demo.MapGet("/insights/{id}/evidence", (string id, string? lang, string? mode, string? team, string? player, DemoSession s) => s.Read((a, _) =>
        {
            var insight = a.Insights.FirstOrDefault(i => i.Id == id);
            if (insight is null) return Results.NotFound();
            if (!EvidenceGate.Check(insight, s.Match).Approved) return Results.Conflict(new { message = "Evidence withheld." });
            var profile = SnapshotBuilder.Profile(lang, mode, team, player);
            var lex = LexiconRegistry.Get(profile.Language);
            var labels = new[] { "why.title", "why.change", "why.threshold", "why.sample", "why.passed", "why.notMet", "why.confidence", "why.supporting", "why.comparison" }
                .ToDictionary(k => k[4..], k => lex.Get(k));
            var card = SnapshotBuilder.Card(insight, new NarrativeContext(new MatchRoster(s.Match), profile), s.Narrative);
            return Results.Ok(new EvidenceResponseDto(card, lex.Get("why.title"), labels, insight.Evidence, insight.Facts));
        }));

        demo.MapGet("/insights/{id}/events", (string id, DemoSession s) => s.Read((a, _) =>
        {
            var insight = a.Insights.FirstOrDefault(i => i.Id == id);
            if (insight is null) return Results.NotFound();
            var ids = insight.Visuals.SelectMany(v => v.EventIds).Concat(insight.SupportingEventIds).ToHashSet();
            var events = s.Match.Events.Where(e => ids.Contains(e.Id)).Select(e => SnapshotBuilder.Event(e, s.Match)).ToList();
            return Results.Ok(new InsightEventsDto(insight.Id, insight.Visuals, events));
        }));

        // 3D tactical replay: a deterministic reconstruction of the sequence behind the insight.
        demo.MapGet("/insights/{id}/replay", (string id, DemoSession s) => s.Read((a, _) =>
        {
            var insight = a.Insights.FirstOrDefault(i => i.Id == id);
            if (insight is null) return Results.NotFound();
            if (!EvidenceGate.Check(insight, s.Match).Approved) return Results.Conflict(new { message = "Evidence withheld." });
            return Results.Ok(MatchShift.Analytics.Replay.ReplayBuilder.ForInsight(s.Match, insight));
        }));

        demo.MapGet("/insights/{id}/agents", async (string id, string? lang, string? mode, string? team, string? player,
            DemoSession s, CancellationToken ct) =>
        {
            var task = s.Read((a, _) =>
            {
                var insight = a.Insights.FirstOrDefault(i => i.Id == id);
                if (insight is null || !EvidenceGate.Check(insight, s.Match).Approved) return null;
                return s.Trace(insight, new NarrativeContext(new MatchRoster(s.Match), SnapshotBuilder.Profile(lang, mode, team, player)));
            });
            if (task is null) return Results.NotFound();
            try { return Results.Ok(await task.WaitAsync(ct)); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Results.Conflict(new { message = "Playback changed; reload this insight." }); }
        });

        demo.MapGet("/highlights/{id}/events", (string id, DemoSession s) => s.Read((a, clock) =>
        {
            var highlight = HighlightRanker.Rank(a, clock).FirstOrDefault(h => h.Id == id);
            if (highlight is null) return Results.NotFound();
            var ids = highlight.RelevantEventIds.ToHashSet();
            var events = s.Match.Events.Where(e => ids.Contains(e.Id)).Select(e => SnapshotBuilder.Event(e, s.Match)).ToList();
            return Results.Ok(new InsightEventsDto(highlight.Id, [new VisualLayer("highlight", VisualStyle.Routes, highlight.RelevantEventIds)], events));
        }));

        demo.MapGet("/recap/{period}", async (string period, string? style, string? lang, string? team, DemoSession s,
            NarrationService narration, CancellationToken ct) =>
        {
            var p = period.Equals("halftime", StringComparison.OrdinalIgnoreCase) ? RecapPeriod.HalfTime
                : period.Equals("fulltime", StringComparison.OrdinalIgnoreCase) ? RecapPeriod.FullTime
                : (RecapPeriod?)null;
            if (p is null) return Results.BadRequest("Period must be 'halftime' or 'fulltime'.");
            var needed = p == RecapPeriod.HalfTime ? Core.Domain.MatchClock.SecondHalfStartSecond : Core.Domain.MatchClock.FullTimeSecond;
            var (facts, insights, match, epoch) = s.Read((a, clock) => clock < needed - 1
                ? (null, a.Insights.ToList(), s.Match, s.Epoch)
                : (RecapBuilder.Build(a, p.Value), a.Insights.ToList(), s.Match, s.Epoch));
            if (facts is null) return Results.Conflict(new { message = $"The {period} recap is available once play reaches that point." });
            var recapStyle = Enum.TryParse<RecapStyle>(style, true, out var rs) ? rs : RecapStyle.Casual;
            var context = new NarrativeContext(new MatchRoster(match), SnapshotBuilder.Profile(lang, null, team, null));
            var narrative = await narration.RecapAsync(facts, recapStyle, context, insights, ct);
            if (s.Epoch != epoch) return Results.Conflict(new { message = "Playback changed; request the recap again." });
            if (narrative.VerificationNote?.Contains("Template used", StringComparison.Ordinal) == true) MatchShiftMetrics.NarrativesFallback.Add(1);
            return Results.Ok(new { facts, narrative });
        });

        return app;
    }

    private static SnapshotDto Snapshot(DemoSession s, ViewerProfile profile)
    {
        s.Touch();
        MatchShiftMetrics.SnapshotsServed.Add(1);
        return s.Read((a, clock) =>
        {
            var snap = SnapshotBuilder.Build(a, clock, s.Match, profile, s.IsPlaying, s.Speed, s.Narrative)
                with { SessionId = s.Id, Epoch = s.Epoch };
            return snap with { Overlay = OverlayDto.From(snap) };
        });
    }
}
