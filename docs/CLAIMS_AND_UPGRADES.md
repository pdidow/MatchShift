# MatchShift: claims compared with the implementation

Reviewed October 15, 2026 against the supplied project description and the current working tree.
Historical baseline: the tables below describe the pre-upgrade audit. Recommendations 1?6 have now
been implemented locally; see [UPGRADE_STATUS.md](UPGRADE_STATUS.md) for current behavior and validation.
The owner revised #1 to require configurable Foundry plus a credential-free mock, without a live deployment.
Current product copy is in [PROJECT_DESCRIPTION.md](PROJECT_DESCRIPTION.md).
Registration has been confirmed separately in [COMPLIANCE.md](COMPLIANCE.md).

**Assessment:** the central product idea is implemented. Explainability, synthetic match analysis and
distinct viewer modes are the strongest claims. Current AI operation, autonomous agent collaboration,
advanced passing analysis and production broadcast readiness need qualification.

## Claim-by-claim comparison

| Supplied claim | What exists | Accurate wording / recommended action |
|---|---|---|
| "Football intelligence that explains itself" | Structured insight evidence, WHY? and SHOW ME, with observation, inference and explanation fields | Keep the tagline. Explain that evidence makes an observation inspectable; it does not prove every tactical explanation. |
| "AI-powered football companion" | Foundry provider code and mocked tests exist. The running local API was checked and reports `template`, `aiAvailable=false`. | Currently: "An explainable football companion with optional Microsoft Foundry narration." Use an unqualified AI-powered claim after demonstrating successful model output in the submitted build. |
| "Analyzes synthetic match events in real time" | `DemoSession` feeds pre-generated synthetic events incrementally into the analyzer. SSE updates the UI. | "Analyzes synthetic events as they are replayed through a live pipeline." This is simulated live play, not a live football feed or demonstrated external event-ingestion service. |
| Detects changes in pressure | Pressure increase/decrease and shot-pressure detectors exist | Supported. Identify the Pressure Index as MatchShift-derived, based on attacking activity; it is not direct measurement of player proximity or an official statistic. |
| Detects territory changes | Field tilt, mean on-ball position, entries and a territorial-shift detector exist | Supported as event-based territorial indicators. Do not imply continuous positional tracking of both teams. |
| Detects attacking direction changes | Left/centre/right attack shares and left/right flank-shift detection exist | Prefer "changes in attacking channels" or "which flank a team attacks through." This is not inference about goal direction or changing ends. |
| Detects passing patterns | Backend computes completion, progressive/forward passes, long balls, passes per possession and tempo. Some pass statistics appear as evidence for other detectors. | Partial. Say "passing and possession metrics." There is no dedicated passing-network/motif change detector, and the main live metrics DTO exposes only a subset of backend metrics. |
| Detects match control changes | Composite Match Control index and a control-shift detector exist | Supported, with the derived-index label. It is an explanatory model of the events, not an official measure of dominance. |
| Detects dangerous turnovers | Event-based detector relates a loss to a subsequent threatening shot | Supported. It must wait for that subsequent event; it does not predict every dangerous loss at the instant of turnover. |
| Detects tactical behaviour | Detectors identify flank changes, pressure, possession, territory, tempo, counters and changes after substitutions | Supported for these specific cues. It does not infer formations, defensive shape, off-ball runs or coaching intent. |
| "Every generated insight is designed to be explainable" | Insights carry evidence, windows, thresholds, event references and confidence/importance fields | Supported as a design statement. Verification coverage is incomplete; avoid claiming all generated sentences are semantically proven. |
| WHY? shows supporting data | Evidence panel renders before/after values, windows, samples and thresholds | Supported. Keep. |
| SHOW ME visualizes relevant pitch events | SVG pitch renders referenced event positions/routes and selected visual layers | Supported. Describe this as an event visualization, not video playback or continuous player tracking. |
| Separates Fact, Inference, Possible explanation | Separate narrative fields; full cards and WHY? distinguish them. Compact/Studio modes intentionally show less. | Supported. Do not treat a labelled possible explanation as a proven causal account. |
| Teach Me Football | Plain-language teaching text is added, with its own filtering threshold | Supported. Keep. |
| Just Tell Me What Matters | Importance threshold filters the feed and cards use a compact presentation | Supported. Importance is calculated by project rules, not a learned preference model. |
| Tactical Analyst | More insights, evidence, metrics, charts and event routes | Supported for event-based analysis. "Spatial analysis" should refer to those routes/channels, not tracking-derived tactical geometry. |
| My Club / My Player | Club-aware filtering and perspective; selected-player involvement when cited events involve that player | Partial. There is no dedicated all-match player performance panel. Results are chronological; player preference is not a separate relevance-ranking model. |
| Six agents with distinct responsibilities | Six labelled stages in one sequential C# pipeline; a recap path runs separately. Most stages are deterministic. | Call this a "staged analysis and narration workflow" today. Do not present six autonomous, cooperating model agents as implemented. |
| "AI is used to explain, interpret, and personalize" | When enabled, Foundry rephrases supplied narrative/evidence. Interpretation codes, filter decisions and much personalization are deterministic. | "Deterministic analytics and rules select facts and interpretations; optional AI adapts their wording." Real model output still needs verification in the submitted environment. |
| "Broadcast-ready" | Studio wording, broadcast recap style, a second-screen UI, timestamps and JSON/SSE | Prefer "broadcast-oriented prototype" or "designed for studio workflows." No production video synchronization, dedicated overlay contract or operational acceptance test is demonstrated. |

## Correct the example

The supplied Chelsea / 31% to 68% / eight-minute example is not the implemented flagship demonstration.
The app uses fictional clubs; its reproducible attack-channel example is:

> **MATCH SHIFT DETECTED**
>
> Westhaven's attacking play has shifted toward the right flank.
>
> In the 50'-62' window, 90% of Westhaven's attacks went down the right, compared with 31% earlier in the match.
>
> Possible explanation: the shift follows a substitution and may reflect a tactical adjustment. The events
> do not establish the coach's intent or prove that the substitution caused the change.

The shift is shown at approximately the 63rd minute, with ID `MS-FLAGSHIP-001-I015`. The channel detector's
default recent window is 12 minutes, not eight. The illustration above paraphrases the explanation;
use the exact current UI output when recording. Do not add real clubs merely to match the proposed copy.

## What the named agents actually do

| Role in the supplied description | Current implementation |
|---|---|
| Event Analyst calculates facts | `MatchState` and `MetricsCalculator` calculate the original facts. The trace's Event Analyst recomputes a supported subset from past events to compare with cited values. |
| Pattern Detection finds shifts | `MatchShiftDetector` runs during incremental analysis. The trace's Pattern Detection step summarizes an already detected pattern rather than running an independent model. |
| Football Reasoning generates interpretations | Detectors assign interpretation codes; the pipeline checks them against an allow-list. This is rule-based selection, not an autonomous reasoning model. |
| Storytelling writes narratives | `NarrationService` uses templates or the optional Foundry provider, with fallback. |
| Personalization adapts the output | Profile-aware templates, lexicons and `NarrativeFilter` control wording and visibility; the trace reports the result. Foundry can rephrase for a requested audience/language. |
| Verification checks the match data | Numeric allow-list and intent-phrase checks, plus supported Event Analyst recomputations. Unknown entities and relationships between claims and facts are not comprehensively checked. |

Live cards call `TemplateInsightProvider` directly. Opening Agents invokes the pipeline; it is not evidence
that every live card already passed through every traced stage. A mismatch can currently fall back to
wording from the same inconsistent insight, which needs correction before claiming that the workflow
blocks all unsupported evidence.

## Recommended upgrade order

The public repository, working judge-access route and demonstration video remain submission priorities.
The upgrades below strengthen the product; they do not replace those requirements.

### 1. Prove the existing AI integration works (original recommendation; scope revised)

Configure Foundry in the submitted environment and run an insight and a full-time recap. Check the actual
narrative `source`, verification outcome and fallback reason. Keep a sanitized record of deployment/model,
latency and successful output. Verify timeout, rejected response and service-outage behavior separately.

Completion evidence: a real accepted response displayed in Agents and Recap, with no secrets in the
recording. `/api/meta` reporting a configured provider alone is insufficient.

### 2. Make evidence validation stricter

Keep factual sentences and numeric values under deterministic control. Give generated interpretations
explicit references to allowed evidence items, team/player IDs and time windows. Reject invalid IDs,
swapped before/after values, incorrect attribution and unsupported causal assertions. Validate evidence
before requesting prose; when recomputation fails, withhold the affected claim rather than regenerate it
from the same bad input.

Add regression cases for a correct number attributed to the wrong team, an invented player, a fabricated
causal claim, corrupted evidence and a missing evidence reference. Report which metrics were checked,
skipped or rejected. Avoid describing the current heuristic confidence as a calibrated probability that
the tactical story is true.

For a compatible deployment, schema-constrained output can enforce the response shape and required
evidence-reference fields. It still needs application-side factual checks; JSON/schema compliance is
not factual correctness. [Microsoft structured-output documentation](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs)

Completion evidence: invalid examples cannot reach viewer-facing output, and a trace distinguishes a
data failure from a model failure. Use verified templates only when the underlying evidence is valid.

### 3. Bring verified AI wording into the live experience

Generate wording asynchronously when a new insight appears. Keep metrics/playback responsive while it
runs. Use a bounded queue and cache keyed by match/evidence version, insight, viewer profile and model/prompt
version. Reuse the same accepted narrative in Live, WHY? and Agents so the trace describes what the viewer
actually saw. Reject stale responses after a seek, reset, match change or profile change.

Completion evidence: an accepted model narrative replaces the initial template without pausing playback;
repeated SSE snapshots do not trigger repeated billable calls; timeouts preserve usable output. Measure
and publish observed latency rather than promising unmeasured real-time performance.

### 4. Make passing-pattern and player-focus claims concrete

Expose the already computed passing metrics in an analyst view. Add a narrowly defined passing-style
shift, such as a substantial change in progressive-pass share or long-ball share between comparable
windows, with minimum samples and exact supporting events. Avoid claiming that a pass network has been
analyzed unless such a network is actually constructed and evaluated.

For player focus, add a selected-player timeline and summary of passes, progression, shots, recoveries and
turnovers from all relevant events. Rank related insights explicitly and keep the player's selection
valid when switching matches. Event-derived performance is useful without pretending to have continuous
movement data.

Completion evidence: a labelled synthetic passing change is detected, stationary controls remain quiet,
and every displayed player total can be reproduced from its listed events.

### 5. Demonstrate a broadcast output and independent viewing sessions

Add a clean overlay route and an explicit output contract containing match/insight ID, match timestamp,
display duration, priority, text, language, evidence references and actual narrative source. Demonstrate
the overlay over an original synthetic animation or another authorized feed. Handle reconnects,
duplicates and seeks so old insights do not reappear at the wrong time.

For a public demo, isolate each viewer's playback state or provide separate test instances. Load-test the
intended audience and monitor model latency/fallbacks. These upgrades support a production direction;
they do not by themselves establish enterprise readiness.

Completion evidence: two viewers can operate independently, an overlay aligns with the demonstrated
timeline, and the deployment recovers predictably after a connection interruption.

### 6. Add purposeful agent collaboration only if that is the chosen category

Keep numerical analytics deterministic. A useful agent split would be an evidence-constrained football
interpreter, a skeptical reviewer that requests missing evidence or rejects unsupported hypotheses, and
an audience editor. Give each typed input/output and shared evidence references; implement bounded
revision and failure recovery. The final verifier remains an application-controlled gate.

Microsoft Agent Framework's .NET workflow APIs support typed executors, routing, events and checkpointed
execution. Those capabilities could replace trace-only stage labels with inspectable execution and
recovery, but adoption should follow the need for that behavior.
[Workflow concepts](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/),
[checkpoints](https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints)

Completion evidence: compare the collaborating design with a single-model baseline on held-out matches,
measuring unsupported claims, useful explanations, latency and cost. More agents alone is not an
improvement. A working Foundry explanation feature has higher immediate value than a broad rewrite.

### 7. Improve detection quality on held-out scenarios

Prioritize the post-substitution detector, whose audited precision was 0.16 (recall 0.35), before expanding
the detector count. The channel-shift detector measured precision 0.62 and recall 0.57. Keep calibration
and evaluation seed sets separate; report false alarms, missed changes and detection delay across scenario
families. Do not tune solely for the flagship story or imply that all unlabelled natural variation is an error.

Completion evidence: reproducible evaluation on seeds not used for tuning, with before/after metrics and
remaining weaknesses documented. Avoid claiming causation from timing around a substitution.

## Suggested copy and implementation references

[PROJECT_DESCRIPTION.md](PROJECT_DESCRIPTION.md) contains a replacement description that fits the current
implementation, including the default template mode and optional Foundry path.

- [Playback engine](../src/MatchShift.Api/Playback/DemoSession.cs) and [snapshot cards](../src/MatchShift.Api/Contracts/SnapshotBuilder.cs).
- [Metrics](../src/MatchShift.Analytics/Metrics/MetricsCalculator.cs) and [available passing metrics](../src/MatchShift.Analytics/Metrics/TeamWindowMetrics.cs).
- [Detector pipeline](../src/MatchShift.Analytics/Detection/MatchShiftDetector.cs), [channel detector](../src/MatchShift.Analytics/Detection/Detectors/AttackChannelShiftDetector.cs) and [window defaults](../src/MatchShift.Analytics/Detection/DetectionOptions.cs).
- [Agent stages](../src/MatchShift.Agents/Agents/AgentPipeline.cs), [narration service](../src/MatchShift.Agents/Agents/NarrationService.cs), [Foundry provider](../src/MatchShift.Infrastructure/Foundry/FoundryInsightProvider.cs) and [verifier](../src/MatchShift.Agents/Agents/VerificationAgent.cs).
- [Viewer filtering and wording](../src/MatchShift.Agents/Narrative/TemplateInsightProvider.cs), [insight cards](../src/MatchShift.Web/src/components/InsightCard.tsx), [WHY?](../src/MatchShift.Web/src/components/WhyPanel.tsx) and [pitch visualization](../src/MatchShift.Web/src/components/Pitch.tsx).
