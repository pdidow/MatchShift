using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
using MatchShift.Analytics;
using MatchShift.Analytics.Metrics;
using MatchShift.Analytics.Replay;
using MatchShift.Analytics.Story;
using MatchShift.Api.Contracts;
using MatchShift.Api.Playback;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.Analytics.State;
// Offline only: instantiate templates directly, never application configuration or an AI provider.
if (args.Length != 2 || args[0] != "--output")
    throw new ArgumentException("Usage: MatchShift.DemoExport --output <site-directory>/demo");
var output = Path.GetFullPath(args[1]);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var catalog = new MatchCatalog();
var languages = LexiconRegistry.Supported.Select(l => l.Code).ToArray();
var verifier = new VerificationAgent();
var pipeline = new AgentPipeline(new NarrationService(new TemplateInsightProvider(), verifier), verifier);

void Write(string path, object value)
{
    var destination = Path.Combine(output, path);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllText(destination, JsonSerializer.Serialize(value, json), new UTF8Encoding(false));
}

foreach (var labelled in catalog.All)
{
    var match = labelled.Match;
    Console.WriteLine($"Exporting {match.Id} ({match.Events.Count} events)...");
    var roster = new MatchRoster(match);
    var detail = new MatchDetailDto(SnapshotBuilder.Summary(match), match.Venue, match.Competition,
        match.HomeTeam.Squad.Select(SnapshotBuilder.Player).ToArray(), match.AwayTeam.Squad.Select(SnapshotBuilder.Player).ToArray());
    var players = match.HomeTeam.Squad.Concat(match.AwayTeam.Squad).ToArray();
    var analysis = new LiveMatchAnalyzer(MatchContext.From(match));
    var frames = new List<object>();
    var highlights = new Dictionary<string, Highlight>();
    var narrationCache = new Dictionary<string, InsightNarrative>();
    var context = new NarrativeContext(roster, ViewerProfile.Default);
    InsightNarrative Narrate(Insight i, NarrativeContext c)
    {
        if (!narrationCache.TryGetValue(i.Id, out var n)) narrationCache[i.Id] = n = TemplateInsightProvider.Narrate(i, c);
        return n;
    }
    var cursor = 0;
    // Events and minute ticks are exact; quiet rolling-metric windows are sampled every ten seconds.
    var times = Enumerable.Range(0, 541).Select(n => n * 10).Concat(match.Events.Select(e => e.MatchSecond))
        .Where(s => s <= 5400).Distinct().Order().ToArray();
    foreach (var second in times)
    {
        while (cursor < match.Events.Count && match.Events[cursor].MatchSecond <= second) analysis.Ingest(match.Events[cursor++]);
        analysis.AdvanceTo(second);
        var snapshot = SnapshotBuilder.Build(analysis, second, match, ViewerProfile.Default, false, 2, Narrate);
        var keys = new List<string>();
        foreach (var h in HighlightRanker.Rank(analysis, second).Take(8).OrderBy(h => h.Second))
        {
            // A merged highlight can gain evidence later: retain each historical version separately.
            var key = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(h, json)))[..20];
            highlights.TryAdd(key, h);
            keys.Add(key);
        }
        frames.Add(new { second, snapshot.Score, snapshot.Metrics, highlights = keys });
    }
    var insights = analysis.Insights.Where(i => EvidenceGate.Check(i, match).Approved).ToArray();
    var focuses = new List<(string? Team, string? Player)> { (null, null), (match.HomeTeam.Id, null), (match.AwayTeam.Id, null) };
    focuses.AddRange(players.Select(p => ((string?)p.TeamId, (string?)p.Id)));
    var profiles = new Dictionary<string, string[]>();
    foreach (var mode in Enum.GetValues<ViewerMode>())
    foreach (var (team, player) in focuses)
    {
        var p = new ViewerProfile(mode, "en", team, player);
        profiles[$"{mode}|{team}|{player}"] = NarrativeFilter.Select(insights, p)
            .OrderByDescending(i => player is not null && TemplateInsightProvider.Involvement(i, match, player) > 0)
            .ThenByDescending(i => i.DetectedAtSecond).ThenByDescending(i => i.Importance).Select(i => i.Id).ToArray();
    }
    var performance = players.ToDictionary(p => p.Id, p =>
    {
        var stats = PlayerPerformance.Calculate(analysis.State, p.Id);
        return new { stats.PlayerId, stats.Passes, stats.Completed, stats.Progressive, stats.Shots, stats.Recoveries, stats.Turnovers,
            events = stats.Events.Select(e => new { e.Id, e.MatchSecond, e.Type, e.Outcome, e.PlayerId }),
            stats.PassIds, stats.CompletedIds, stats.ProgressiveIds, stats.ShotIds, stats.RecoveryIds, stats.TurnoverIds };
    });
    Write($"{match.Id}/core.json", new { detail, frames, events = match.Events.Select(e => SnapshotBuilder.Event(e, match)),
        analysis.Timeline, profiles, players = performance,
        insightEvents = insights.ToDictionary(i => i.Id, i => i.Visuals.SelectMany(v => v.EventIds).Concat(i.SupportingEventIds).Distinct().ToArray()),
        highlightEvents = highlights.ToDictionary(h => h.Key, h => h.Value.RelevantEventIds) });

    foreach (var insight in insights)
        Write($"{match.Id}/replay/{insight.Id}.json", ReplayBuilder.ForInsight(match, insight));

    var half = LiveMatchAnalyzer.AnalyzeUntil(match, 2700);
    foreach (var language in languages)
    {
        var c = context with { Profile = new ViewerProfile(ViewerMode.TeachMe, language) };
        var cards = insights.Select(i => SnapshotBuilder.Card(i, c)).ToArray();
        var focusText = new Dictionary<string, object>();
        foreach (var (team, player) in focuses.Where(f => f.Team is not null))
        {
            var focused = c with { Profile = new ViewerProfile(ViewerMode.MyTeam, language, team, player) };
            focusText[$"{team}|{player}"] = insights.ToDictionary(i => i.Id, i =>
            {
                var n = TemplateInsightProvider.Narrate(i, focused);
                return new { n.Perspective, n.PlayerLine };
            });
        }
        var localizedHighlights = highlights.ToDictionary(h => h.Key, h =>
        {
            var n = HighlightNarrator.Narrate(h.Value, analysis.Insights, c);
            return new HighlightCardDto(h.Key, h.Value.Kind, h.Value.DisplayMinute, h.Value.Second, h.Value.TeamId,
                h.Value.ImportanceScore, h.Value.InsightId, n.Headline, n.WhyItMatters, h.Value.RelevantEventIds.Count);
        });
        Write($"{match.Id}/{language}.json", new { cards, focusText, highlights = localizedHighlights });
        var recaps = new Dictionary<string, object>();
        foreach (var period in Enum.GetValues<RecapPeriod>())
        foreach (var style in Enum.GetValues<RecapStyle>())
        foreach (var team in new string?[] { null, match.HomeTeam.Id, match.AwayTeam.Id })
        {
            var a = period == RecapPeriod.HalfTime ? half : analysis;
            var rc = c with { Profile = new ViewerProfile(ViewerMode.Analyst, language, team) };
            recaps[$"{period.ToString().ToLowerInvariant()}|{style}|{team}"] = new {
                narrative = RecapNarrator.Narrate(RecapBuilder.Build(a, period), style, rc, a.Insights) };
        }
        Write($"{match.Id}/recaps-{language}.json", recaps);
        // The Agents tab of the static demo replays agent-recordings.json (recorded by `MatchShift.Cli agent-workflow --recording`),
        // always labelled RECORDED_DEMO. The old deterministic narration traces are no longer exported.
    }
    Console.WriteLine($"  {frames.Count} frames, {insights.Length} verified insights, {highlights.Count} highlight versions");
}
Write("index.json", new {
    schemaVersion = 1, defaultMatchId = catalog.Default.Id,
    meta = new MetaDto(LexiconRegistry.Supported.Select(l => new LanguageDto(l.Code, l.Name)).ToArray(),
        Enum.GetNames<ViewerMode>(), Enum.GetNames<RecapStyle>(), DemoSession.AllowedSpeeds, "template", false,
        "Saved synthetic matches. Fictional clubs and players. Template narration; no live AI service."),
    matches = catalog.All.Select(m => SnapshotBuilder.Summary(m.Match)) });
var bytes = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
Console.WriteLine($"Export complete: {bytes / 1_000_000.0:F1} MB. No network or AI calls.");
