# Explainability

The current four-agent workflow is described in [AGENTS.md](AGENTS.md). Older pipeline names in historical test references below are not stages of the current Agents page.

Every insight answers **WHY?** and **SHOW ME** from data that existed before any words were written.

## The evidence model

```jsonc
{
  "metric": "RightAttackShare",          // stable key; renderers localise the label
  "unit": "Share",                       // formatting only - never changes the value
  "baselineValue": 0.314, "recentValue": 0.9, "difference": 0.586,
  "baselineWindow": { "fromSecond": 0, "toSecond": 3000 },     // 0'-50'
  "recentWindow":   { "fromSecond": 3000, "toSecond": 3720 },  // 50'-62'
  "threshold": 0.25,
  "sampleSize": 10, "baselineSampleSize": 35, "minimumSampleSize": 8, "sampleRequirementMet": true,
  "comparison": "last 12 vs match so far",
  "isPrimary": true
}
```

An `Insight` also carries `supportingEventIds`, `visuals` (layers telling SHOW ME what to draw), structured
`facts` (channel, substitute, scorer…), `severity`, `confidence` (+ score) and an `interpretationCode` - a
pointer to a cautious explanation, never prose.

## FACT, INFERENCE, POSSIBLE EXPLANATION

Rendered narratives keep three separate fields:

| Field | Example (63', Westhaven) | Source |
|---|---|---|
| FACT | "90% of Westhaven's attacks in the 50'-62' spell went down the right, up from 31% earlier in the match." | evidence values only |
| INFERENCE | "Their attacking play has shifted decisively towards the right flank." | what the pattern means |
| POSSIBLE EXPLANATION | "The change follows Tavian Brooks's introduction, which may point to a deliberate adjustment - but the data cannot confirm intent." | allow-listed interpretation code, always hedged |

The grounded `InsightContext` constrains interpretation. The current Tactical Analyst labels inference and possible explanation; deterministic grounding validation and the Verification Agent check generated claims against the match data, including unsupported assertions of intent.

## WHY?

The WHY? panel (and `GET /api/demo/insights/{id}/evidence`) shows the primary change (old → new, windows,
difference), detection threshold, sample requirement (n / minimum, passed), confidence, supporting-event count,
and a table of every evidence item - including statistical strength (z-score) and the key player's involvement.

## SHOW ME

`GET /api/demo/insights/{id}/events` returns the visual layers and the events in absolute pitch coordinates.
The pitch draws supporting events plus separately marked context where useful: one arrow per attack for channel shifts (each arriving in the shaded flank),
the losing action plus the punishing move for a dangerous turnover, shots sized by outcome, high recoveries as
points. De-emphasised layers (e.g. attacks on other flanks) are drawn muted for context and are **not counted as supporting evidence**. The flagship I015 has 30 supporting events and 1 context event. Its Evidence Replay clip shows 5 of the 30 supporting events, with no context event; animation does not create new evidence.

## Evidence integrity, tested

- `DetectorTests.Every_insight_has_traceable_evidence` - every supporting/visual event ID exists.
- `DetectorTests.Channel_evidence_can_be_recomputed_from_events` - cited shares equal a fresh computation.
- `AgentPipelineTests.Event_analyst_recomputes_every_cited_metric_from_raw_events` - the Event Analyst agent
  re-derives every windowed metric of every flagship insight (share, pressure, control, possession, field tilt,
  shots, turnovers, transitions, chaos) and they match.
- `NarrativeTests.Template_narratives_pass_verification` - every number in every rendered sentence, in every
  language and mode, is found in the evidence.
