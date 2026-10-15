using System.Text.Json;
using MatchShift.Infrastructure.Export;
using MatchShift.SyntheticData;

namespace MatchShift.Agents.Tests;

public class ExportTests
{
    [Fact]
    public async Task Dataset_export_writes_fabric_ready_tables()
    {
        var dir = Path.Combine(Path.GetTempPath(), "matchshift-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dataset = SyntheticMatchGenerator.GenerateDataset(2, 500);
            var manifest = await DatasetExporter.ExportAsync(dataset, dir);

            foreach (var file in new[] { "matches.jsonl", "players.csv", "events.csv", "events.jsonl", "events.parquet", "ground_truth.jsonl", "insights.csv", "minute_metrics.csv", "manifest.json" })
                Assert.True(File.Exists(Path.Combine(dir, file)), file);

            var events = dataset.Matches.Sum(m => m.Match.Events.Count);
            Assert.Equal(events, manifest.Events);
            Assert.Equal(events + 1, File.ReadAllLines(Path.Combine(dir, "events.csv")).Length);
            Assert.Equal(2, File.ReadAllLines(Path.Combine(dir, "matches.jsonl")).Length);
            Assert.Equal(2 * 90, File.ReadAllLines(Path.Combine(dir, "minute_metrics.csv")).Length - 1);
            Assert.Contains("synthetic", manifest.Notice);

            var firstMatch = JsonDocument.Parse(File.ReadLines(Path.Combine(dir, "matches.jsonl")).First()).RootElement;
            Assert.True(firstMatch.GetProperty("synthetic").GetBoolean());
            Assert.StartsWith("match_id,scenario,seed,seq,event_id", File.ReadLines(Path.Combine(dir, "events.csv")).First());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Csv_quotes_values_with_commas()
    {
        var csv = DatasetExporter.ToCsv([new { Name = "a,b", Value = 1.5 }]);
        Assert.Equal("name,value", csv.Split('\n')[0].TrimEnd('\r'));
        Assert.Contains("\"a,b\",1.5", csv);
    }
}
