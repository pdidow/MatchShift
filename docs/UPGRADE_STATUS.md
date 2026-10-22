# Completed upgrades 1–6

> **HISTORICAL / ARCHIVED (October 2026).** This records an earlier development milestone, before the current Azure App Service deployment and four-agent judge path. It is retained for project history; use [README.md](../README.md), [AGENTS.md](AGENTS.md) and [DEPLOYMENT.md](DEPLOYMENT.md) for current behavior.

Implemented and validated October 15–16, 2026. Scope is the six recommendations in
[CLAIMS_AND_UPGRADES.md](CLAIMS_AND_UPGRADES.md), with #1 revised by the owner to require configurable
Foundry plus credential-free local development. No Azure subscription, billing information, secret,
live deployment or paid model call was used.

- [x] **1. Provider configuration and local demonstration.** Production Microsoft Foundry, explicit mock
  protocol simulation and offline templates. Endpoint/deployment/authentication and enablement are in
  [FOUNDRY.md](FOUNDRY.md). Mock insight and recap validation passes; sources remain honestly labelled.
- [x] **2. Strict evidence and claim validation.** Full insight consistency against deterministic event
  replay before narration; exact metric/team/player/window/value/event bindings; canonical rendering
  rejects unsupported prose. Corrupt data is withheld, never rewritten from the same invalid evidence.
- [x] **3. Live editorial output.** Two background workers, queue capacity 64, per-session/profile cache
  capacity 256, shared Live/WHY/Agents result and epoch cancellation on seek/reset/match changes.
- [x] **4. Passing and player focus.** Analyst passing metrics; long-ball-share shift with adjacent
  eight-minute windows, minimum 20 passes each and 0.30 change; labelled scenario; player totals with
  per-metric event IDs, full observed-event timeline and player-related insight priority.
- [x] **5. Broadcast and sessions.** Transparent `/overlay`, original synthetic `/broadcast` composite,
  `matchshift.overlay.v1` contract, match-clock expiry and reset handling. Separate viewing sessions,
  explicit sharing by UUID, 32-session limit and 30-minute idle expiry. No professional video-sync claim.
- [x] **6. Purposeful collaboration and comparison.** Typed Interpreter → Skeptic → Editor calls,
  independent application verification, one repair attempt with a second review, retained rejected
  handoffs on bounded failure, and a repeatable single/collaborative comparison command.

## Validation

- **218 backend tests pass:** Core 23, SyntheticData 44, Analytics 77, Agents 62, API 12.
  The final API suite was rerun after stopping the test server, which had held a Windows build-file lock.
- `npm run build` passes (TypeScript + Vite).
- Microsoft Edge/Playwright browser checks pass: live mock cards, shared trace wording, WHY?, player
  summary, French teaching, broadcast composite, transparent overlay, reset clearing, offline clearing
  and reconnect recovery, mock fulltime recap and independent browser session. No page exceptions. Header wrapping was corrected; the
  player view fits a 1440px viewport without horizontal overflow.
- [Mock provider validation](validation/mock-validation.json): insight and fulltime recap accepted;
  four simulated provider requests. The captured 950ms includes cold deterministic validation.
- [Browser report](validation/browser-smoke.json).
- [Eight-viewer local burst](validation/local-load.json): 40 snapshots, no errors, p50 19.4ms / p95 44.1ms.
  Immediate templates keep output responsive while bounded editorial work runs. This is not a sustained
  load test or a cloud capacity claim.
- [Full comparison data](validation/agent-comparison.json): eight held-out match seeds 81000–81007,
  each tested with clean and injected wrong-team proposals in both workflows.

| Workflow / case | Accepted selections | Template fallbacks | Unsupported published bindings | Median latency | Calls, total |
|---|---:|---:|---:|---:|---:|
| Single / clean | 8/8 | 0 | 0 | 34.5 ms | 8 |
| Single / wrong team | 0/8 | 8 | 0 | 31 ms | 8 |
| Collaborative / clean | 8/8 | 0 | 0 | 93 ms | 24 |
| Collaborative / wrong team | 8/8 | 0 | 0 | 155.5 ms | 40 |

These are **mock measurements**, including deliberately simulated request delays. Both workflows
prevented unsupported publishing; collaboration recovered the editorial selection at additional call
and latency cost. Billable mock cost is zero. Character counts are recorded; live token usage, dollar
cost, model quality and human usefulness are unmeasured. Retaining an explanation is only a proxy for
usefulness. No claim of live-model superiority follows from this benchmark.

The existing [120-match evaluation](validation/detectors-120.txt) retains channel precision 0.62 / recall
0.57 and post-substitution precision 0.16 / recall 0.35, with seven negative-scenario violations.
These are detector errors, not rule violations. Passing-style [held-out seeds 82000–82011](validation/passing-12.txt)
recall 12/12 expected changes. Nine additional passing detections in the original mixed dataset are
unlabelled; this is not a measured passing precision score. Detector quality recommendation #7 was
outside the requested 1–6 scope and remains a limitation.

## Practical limits

The model selects controlled editorial treatments and recap order. It does not freely generate new
football facts or tactical prose. Replay verifies consistency with the implemented detector, not the
correctness of every tactical assumption. Confidence remains heuristic. The custom orchestration does
not claim Agent Framework, MCP or A2A integration. Sessions/cache are in-memory and single-process;
random session IDs are sharing capabilities, not authenticated tenant access. Overlay timing is relative
to the synthetic match clock (120 match seconds), not a measured professional video transport sync.

Live Foundry availability and deployment, public judge access, the public repository/video, eligibility
and rights checks remain separate submission matters in
[chatGPTOutstandingFindings.txt](../chatGPTOutstandingFindings.txt). They do not block local development.

## Reproduce the browser checks

With a mock-mode API on localhost:5002 and Microsoft Edge installed:

```powershell
npm install --prefix artifacts/browser --no-save --package-lock=false playwright
node tools/validation/browser-smoke.cjs
```

Set `MATCHSHIFT_TEST_URL` to use another host/port. The check creates its own session IDs.
Playwright is an optional validation tool installed under ignored `artifacts`, not an application runtime
dependency. Browser screenshots are saved there; the small validation report is retained in this repository.
