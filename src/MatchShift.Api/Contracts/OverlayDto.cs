namespace MatchShift.Api.Contracts;

/// <summary>Versioned overlay contract. Timing is on the synthetic match clock, not wall time or video PTS.</summary>
public sealed record OverlayDto(string SchemaVersion, string SessionId, long Epoch, string MatchId, string InsightId,
    int MatchSecond, int ValidFromSecond, int ValidUntilSecond, int DisplayDurationMatchSeconds,
    double Priority, string Text, string Language, string Source, string Verification,
    IReadOnlyList<string> EvidenceReferences, bool Synthetic)
{
    public static OverlayDto? From(SnapshotDto snapshot)
    {
        const int duration = 120;
        var card = snapshot.Insights.Where(i => i.Second <= snapshot.Clock.Second && snapshot.Clock.Second < i.Second + duration)
            .OrderByDescending(i => i.Importance).ThenByDescending(i => i.Second).FirstOrDefault();
        return card is null ? null : new("matchshift.overlay.v1", snapshot.SessionId, snapshot.Epoch, snapshot.MatchId, card.Id,
            snapshot.Clock.Second, card.Second, card.Second + duration, duration, card.Importance,
            card.Narrative.StudioLine, card.Narrative.Language, card.Narrative.Source,
            card.Narrative.VerificationNote ?? "Deterministic evidence verified",
            // Evidence references exclude context-only events, which are drawn but are not supporting evidence.
            card.Visuals.SelectMany(v => v.EventIds).Except(card.ContextEventIds ?? []).Distinct().ToArray(), true);
    }
}
