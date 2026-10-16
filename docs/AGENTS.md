# Agent workflow

MatchShift combines deterministic football analytics with an optional evidence-bound AI editorial workflow.
The same production protocol is available through Microsoft Foundry or an explicitly labelled offline mock.
There is no dependency on a live subscription for local development. See [FOUNDRY.md](FOUNDRY.md).

## Data and editorial responsibilities

| Component | Input and output | Failure behavior |
|---|---|---|
| Event analysis and pattern detection | Raw synthetic events → metrics → evidence-bearing insights | Sample floors, persistence, cooldown and deterministic replay gate |
| EvidenceGate | Insight and raw events → exact comparison with replayed detector output | Withholds inconsistent evidence before prose generation; no fallback from corrupt input |
| Rule-based football interpretation | Pattern and context → allow-listed cautious interpretation code | Unsupported interpretation withheld |
| Interpreter | Authoritative evidence and audience catalogue → typed proposed editorial treatment | Invalid proposal is sent to review or rejected |
| Skeptic | Original evidence plus proposal → approval or revision feedback | One bounded repair, then a second review; continued rejection uses a verified template |
| Editor | Accepted selection and audience → final treatment | Cannot bypass the application evidence gate |
| Personalization | Mode, language, club/player → filtering, deterministic wording and relevance ordering | Unknown player selection is cleared when matches change |
| Verification | Selected claim binding and canonical wording → final accepted narrative | Changed attribution, numbers, windows, references or arbitrary prose are rejected |
| Recap editor | Verified recap sections → section order | Invalid/missing/duplicate section references use the original recap |

The custom C# orchestration uses real, separate provider calls for interpreter, skeptic and editor when
Foundry is configured. These roles share typed state and revise on rejection. Deterministic analytics
stages are not presented as autonomous model agents. No Agent Framework, A2A or MCP dependency is claimed.
Model decisions are deliberately limited to editorial treatments and ordering; the model does not freely
invent tactical explanations.

## Verification coverage

`EvidenceGate` reconstructs default detector output from raw match events and compares the entire insight:
all evidence items, attribution, windows, facts, thresholds, event IDs and visual references. Replay is
memoized per immutable match. The replay engine is incremental; the candidate at each timestamp has no
access to future events. Reusing the detector establishes consistency, not independent validation of its
football assumptions or confidence calibration. Custom detector configurations require a matching gate.

The trace includes `evidenceAudit` (approved, checked, skipped, reason). The older Event Analyst window
checks remain additional diagnostics; unknown diagnostic metrics are covered by replay, not silently
accepted as independently recomputed. Numeric/intent phrase checks are additional defenses. Exact
canonical rendering protects against valid numbers attached to the wrong team or time window.

## Live execution and recovery

Two background workers share a queue of 64 requests. Each session caches up to 256 insight/profile tasks.
The cache is implicitly scoped to immutable match data, session epoch and the startup provider/prompt
version. Completed cards, WHY? and Agents use the same narrative. While queued, cards show verified
available templates. Queue saturation preserves those templates and is retried on a later snapshot.
Seeking, resetting or changing a match cancels older tasks and clears the cache; completed older work
cannot advance the new epoch. Profile keys include language, mode, team and player.

Sessions are random capability IDs, separate per browser tab, with an explicit shared ID for a broadcast
browser source. They are in memory (maximum 32; idle expiry 30 minutes). This is a bounded demo host,
not durable multi-node production infrastructure or authenticated tenant isolation.

## Measurements

`compare-agents` runs a single-call baseline and the collaborating workflow on the same seeded matches,
with clean output and injected wrong-team proposals. It records accepted selections, fallback, unsupported
published bindings, retained explanations, revisions, latency and request/character counts. Character
counts are not token counts. The mock has no billable cost; live token cost and human usefulness remain
unmeasured. See [UPGRADE_STATUS.md](UPGRADE_STATUS.md) and the JSON report in `docs/validation`.

Tests cover corrupt evidence, invented players, wrong teams, reversed values, windows, missing references,
unsupported causal wording, failures, cancellation, bounded queuing, cache reuse and session isolation.
