using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MatchShift.Analytics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;
using Parquet.Serialization;

namespace MatchShift.Infrastructure.Export;

/// <summary>Flat event row for analytics tools (Microsoft Fabric Lakehouse, notebooks, Power BI).</summary>
public sealed record EventRow(
    string MatchId, string Scenario, long Seed, int Seq, string EventId, int MatchSecond, int Minute, int Period,
    string EventType, string Outcome, string TeamId, bool IsHome, string? PlayerId, string? RelatedPlayerId,
    int PossessionId, string? PossessionTeamId, double? StartX, double? StartY, double? EndX, double? EndY,
    double? AbsStartX, double? AbsStartY, double? AbsEndX, double? AbsEndY, string? Qualifier);

public sealed record InsightRow(
    string MatchId, string InsightId, string Type, int DetectedAtSecond, int Minute, string? TeamId, string Severity, string Confidence,
    double ConfidenceScore, double Importance, string? PrimaryMetric, double? BaselineValue, double? RecentValue, double? Threshold,
    int SampleSize, string? InterpretationCode, int SupportingEvents, bool MatchesGroundTruth);

public sealed record MinuteRow(
    string MatchId, int Minute, double HomeControl, double HomePressure, double AwayPressure, double Chaos, string ChaosState,
    int HomeGoals, int AwayGoals);

public sealed record ExportManifest(
    string Dataset, string SchemaVersion, DateTimeOffset GeneratedAt, long SeedStart, int Matches, int Events, int Insights,
    IReadOnlyDictionary<string, int> Scenarios, IReadOnlyList<string> Files, string Notice);

/// <summary>
/// Writes a labelled synthetic dataset to disk in Fabric-friendly formats:
///
///   matches.jsonl, players.csv, events.jsonl, events.csv, events.parquet,
///   ground_truth.jsonl, insights.jsonl, insights.csv, minute_metrics.csv, manifest.json
///
/// Events are written in both team-relative (analytics) and absolute (visualisation) coordinates.
/// Ground truth is kept in its own table, exactly as in the engine: the detector never sees it.
/// </summary>
public static class DatasetExporter
{
    public const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<ExportManifest> ExportAsync(SyntheticDataset dataset, string directory, bool parquet = true,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var events = new List<EventRow>();
        var insights = new List<InsightRow>();
        var minutes = new List<MinuteRow>();
        var matchLines = new StringBuilder();
        var truthLines = new StringBuilder();
        var insightLines = new StringBuilder();
        var players = new StringBuilder("match_id,team_id,player_id,name,shirt_number,position,is_starter\n");

        foreach (var labelled in dataset.Matches)
        {
            ct.ThrowIfCancellationRequested();
            var m = labelled.Match;
            matchLines.AppendLine(JsonSerializer.Serialize(new
            {
                m.Id, m.ScenarioName, m.Seed, m.Competition, m.Venue, Date = m.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Home = m.HomeTeam.Id, Away = m.AwayTeam.Id, HomeName = m.HomeTeam.Name, AwayName = m.AwayTeam.Name,
                FinalScore = m.ScoreAt(MatchClock.FullTimeSecond), EventCount = m.Events.Count, Synthetic = true,
            }, Json));
            truthLines.AppendLine(JsonSerializer.Serialize(labelled.GroundTruth, Json));

            foreach (var team in new[] { m.HomeTeam, m.AwayTeam })
                foreach (var p in team.Squad)
                    players.AppendLine(Csv(m.Id, team.Id, p.Id, p.Name, p.ShirtNumber, p.Position, p.IsStarter));

            foreach (var e in m.Events)
            {
                var home = m.IsHome(e.TeamId);
                (double X, double Y)? s = e.HasLocation ? Pitch.ToAbsolute(e.StartX!.Value, e.StartY!.Value, home) : null;
                (double X, double Y)? t = e.HasEndLocation ? Pitch.ToAbsolute(e.EndX!.Value, e.EndY!.Value, home) : null;
                events.Add(new EventRow(m.Id, m.ScenarioName, m.Seed, e.SequenceNumber, e.Id, e.MatchSecond, e.MatchMinute, e.Period,
                    e.Type.ToString(), e.Outcome.ToString(), e.TeamId, home, e.PlayerId, e.RelatedPlayerId, e.PossessionId, e.PossessionTeamId,
                    e.StartX, e.StartY, e.EndX, e.EndY, R(s?.X), R(s?.Y), R(t?.X), R(t?.Y), e.Qualifier));
            }

            var analysis = LiveMatchAnalyzer.AnalyzeFull(m);
            foreach (var i in analysis.Insights)
            {
                var p = i.PrimaryEvidence;
                var matched = labelled.GroundTruth.ExpectedPatterns.Any(x => x.Matches(i.Type, i.TeamId, i.DetectedAtSecond / 60.0, 2));
                insights.Add(new InsightRow(m.Id, i.Id, i.Type.Code(), i.DetectedAtSecond, i.DisplayMinute, i.TeamId, i.Severity.ToString(),
                    i.Confidence.ToString(), i.ConfidenceScore, i.Importance, p?.Metric, p?.BaselineValue, p?.RecentValue, p?.Threshold,
                    p?.SampleSize ?? 0, i.InterpretationCode, i.SupportingEventIds.Count, matched));
                insightLines.AppendLine(JsonSerializer.Serialize(i, Json));
            }
            minutes.AddRange(analysis.Timeline.Select(t => new MinuteRow(m.Id, t.Minute, t.HomeControl, t.HomePressure, t.AwayPressure,
                t.Chaos, t.ChaosState.ToString().ToUpperInvariant(), t.HomeGoals, t.AwayGoals)));
        }

        var files = new List<string>();
        async Task Write(string name, string content)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, name), content, new UTF8Encoding(false), ct);
            files.Add(name);
        }

        await Write("matches.jsonl", matchLines.ToString());
        await Write("players.csv", players.ToString());
        await Write("ground_truth.jsonl", truthLines.ToString());
        await Write("insights.jsonl", insightLines.ToString());
        await Write("events.jsonl", string.Join('\n', events.Select(e => JsonSerializer.Serialize(e, Json))) + "\n");
        await Write("events.csv", ToCsv(events));
        await Write("insights.csv", ToCsv(insights));
        await Write("minute_metrics.csv", ToCsv(minutes));

        if (parquet)
        {
            await using (var stream = File.Create(Path.Combine(directory, "events.parquet")))
                await ParquetSerializer.SerializeAsync(events, stream, cancellationToken: ct);
            files.Add("events.parquet");
            await using (var stream = File.Create(Path.Combine(directory, "insights.parquet")))
                await ParquetSerializer.SerializeAsync(insights, stream, cancellationToken: ct);
            files.Add("insights.parquet");
        }

        var manifest = new ExportManifest(
            dataset.Name, SchemaVersion, DateTimeOffset.UtcNow, dataset.SeedStart, dataset.Matches.Count, events.Count, insights.Count,
            dataset.Matches.GroupBy(m => m.GroundTruth.ScenarioName).ToDictionary(g => g.Key, g => g.Count()),
            files,
            "ALL data in this dataset is synthetic: fictional clubs, players, matches and events generated by MatchShift.");
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(Json) { WriteIndented = true }), ct);
        return manifest;
    }

    private static double? R(double? v) => v is null ? null : Math.Round(v.Value, 1);

    /// <summary>CSV with snake_case headers derived from record properties.</summary>
    public static string ToCsv<T>(IEnumerable<T> rows)
    {
        var props = typeof(T).GetProperties();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', props.Select(p => Snake(p.Name))));
        foreach (var row in rows) sb.AppendLine(Csv(props.Select(p => p.GetValue(row)).ToArray()));
        return sb.ToString();
    }

    private static string Csv(params object?[] values) => string.Join(',', values.Select(v => v switch
    {
        null => "",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Quote(v.ToString() ?? ""),
    }));

    private static string Quote(string s) => s.IndexOfAny([',', '"', '\n']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    private static string Snake(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
