using System.Runtime.CompilerServices;
using System.Text.Json;
using MatchShift.Analytics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Agents.Agents;

public sealed record EvidenceAudit(bool Approved, int Checked, int Skipped, string Reason);

/// <summary>Replays the deterministic detector over raw events. No unknown metric is silently approved.
/// The replay engine processes only the event prefix available at each detection time.</summary>
public static class EvidenceGate
{
    private sealed record ExpectedInsight(string Json, bool MetricsMatch);
    private static readonly ConditionalWeakTable<Match, Lazy<IReadOnlyDictionary<string, ExpectedInsight>>> Replays = new();

    public static EvidenceAudit Check(Insight insight, Match match)
    {
        var expected = Replays.GetValue(match, m => new(() => LiveMatchAnalyzer.AnalyzeFull(m).Insights
            .ToDictionary(i => i.Id, i => new ExpectedInsight(JsonSerializer.Serialize(i),
                AgentPipeline.EventAnalyst(i, m).All(f => f.Matches))))).Value;
        var valid = expected.TryGetValue(insight.Id, out var replay) && replay.MetricsMatch && replay.Json == JsonSerializer.Serialize(insight);
        return new(valid, valid ? insight.Evidence.Count : 0, 0,
            valid ? "All evidence, attribution, windows, event references and detector facts match deterministic replay."
                : "Claim withheld: evidence or attribution differs from deterministic event replay.");
    }

    public static void Require(Insight insight, Match match)
    {
        var audit = Check(insight, match);
        if (!audit.Approved) throw new InvalidDataException(audit.Reason);
    }
}
