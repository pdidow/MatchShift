# Agent workflow (Microsoft Agent Framework)

MatchShift explains each meaningful insight with a **Microsoft Agent Framework workflow** of four
specialised agents running on a **Microsoft Foundry** model deployment. Deterministic football analytics
remain the only source of match facts: the agents reason over structured facts, they never compute them.

```
SYNTHETIC MATCH EVENTS → DETERMINISTIC ANALYTICS (LiveMatchAnalyzer, detectors, EvidenceGate)
        → meaningful insight candidate (never raw events)
        → InsightContext (immutable, written once into Agent Framework shared workflow state)

 deterministic-grounding ─▶ tactical-analyst ─┬─▶ audience-narrative ─────┐   fan-out edge: same superstep, concurrent
                                               └─▶ accessibility-narrator ─┴─▶ fan-in   fan-in barrier edge
 fan-in ─▶ deterministic-validation ─▶ verification-agent ─▶ final-validation ─▶ APPROVED / APPROVED_WITH_CHANGES / REJECTED
                       ▲                                           │ REJECTED (once)
                       └──────────── constrained-repair ◀──────────┘
```

Code: `src/MatchShift.Agents/Workflow/` (graph, executors, agents, validation, trace),
`src/MatchShift.Infrastructure/Foundry/FoundryAgentFactory.cs` (Foundry agents),
`src/MatchShift.Api/Playback/AgentWorkflowService.cs` (budgets, caching, cancellation),
`src/MatchShift.Web/src/components/AgentWorkflow.tsx` (Agents tab).

## What is real

| Claim | Evidence in code / at runtime |
|---|---|
| Agent Framework orchestration | `WorkflowBuilder` with `AddEdge`, `AddFanOutEdge`, `AddFanInBarrierEdge`, typed `Executor<TIn,TOut>`s, run by `InProcessExecution.RunStreamingAsync` (`InsightWorkflow.cs`). The trace records the framework's own `SuperStepStartedEvent` / `ExecutorInvokedEvent` counts. |
| Real agents | Each agent is a `ChatClientAgent` created code-first with `AIProjectClient.AsAIAgent(ChatClientAgentOptions)` (package `Microsoft.Agents.AI.Foundry`) against `FOUNDRY_PROJECT_ENDPOINT`, model `AZURE_AI_MODEL_DEPLOYMENT_NAME`, structured output via `AIAgent.RunAsync<T>`. No portal-defined agents. |
| Keyless auth | `DefaultAzureCredential` (default), `AzureCliCredential` or `ManagedIdentityCredential` (`AgentWorkflow:Credential`). No API-key setting exists for the workflow. The token is acquired once at start-up (no model call). |
| Shared state | `InsightContext` is written once to workflow state (`QueueStateUpdateAsync`, scope `matchshift`) by the grounding executor and read by every agent (`ReadStateAsync`). Collections are `ImmutableArray`; a SHA-256 fingerprint is taken before and after the run. |
| Concurrency | Audience and Accessibility executors run in the same superstep; the trace records both supersteps and the measured overlap of their real start/end times. |
| Handoffs | Typed messages along edges (`GroundedInsight`, `TacticalHandoff`, `BranchOutput`, `NarrativeBundle`, …), recorded as handoffs. |

## Why four agents instead of one prompt

| Agent | Responsibility | Why separate |
|---|---|---|
| Tactical Analyst | What might the evidence mean? Facts used, inference, possible explanation, significance, confidence qualification, evidence IDs, limitations. | Interpretation is the hardest, most error-prone step; isolating it lets code validate it before anything is worded for viewers, and lets both narrators reuse one interpretation. |
| Audience Narrative | How should this be said to *this* viewer (TeachMe, WhatMatters, Analyst, MyTeam, Studio)? | Depth and tone change per audience; facts must not. A separate stage can be re-run (constrained repair) without re-interpreting. |
| Accessibility Narrator | The same intelligence for spoken, nonvisual and simplified use: TTS summary, pitch-zone description, plain explanation, replay audio description. | Not a paraphrase: different output contract (no symbols, zone-based description). Runs concurrently with the audience branch, and is skipped with no model call when the viewer does not need it. |
| Verification Agent | Are all generated claims grounded and safe to publish? Returns APPROVED / APPROVED_WITH_CHANGES / REJECTED with reasons, unsupported claims, corrections and evidence IDs checked. | An independent reviewer that sees every variant together can catch cross-variant inconsistencies (spoken vs visual) that a single generator would not police in itself. |

## Deterministic validation around the AI

Code checks what code can check exactly (`GroundingValidator.cs`), before and after the verifier:

- every cited evidence ID exists, is a supporting event, and is not after the insight (`UNKNOWN_EVIDENCE_ID`, `NOT_SUPPORTING_EVIDENCE`, `FUTURE_EVIDENCE`);
- every number equals an authoritative value (metric values, baselines, changes, thresholds, samples, windows, score, minute), within rounding (`UNSUPPORTED_NUMBER`);
- scores and times are correct and not in the future (`WRONG_SCORE`, `FUTURE_TIME`);
- named players are involved in the evidence; other clubs are not mentioned (`UNKNOWN_PLAYER`, `FOREIGN_TEAM`);
- no intent claims, no un-negated certainty words, no causes inside facts; confidence and sample wording match the deterministic inputs.

Failing optional claims are removed (removal never adds an unsupported claim); a failing inference is replaced
by the deterministic one. Final validation re-checks verifier corrections, overrides a verifier APPROVED if code
found blocking problems, and never labels anything verified unless the verifier approved **and** code re-validated.

## Failure recovery (all recorded in the trace)

| Failure | Behaviour |
|---|---|
| Tactical Analyst timeout / error | Bounded retry (timeout, throttling, 5xx, malformed output: max 2 attempts), then the deterministic insight is preserved and downstream agents are **not** called. |
| Audience Narrative failure | Deterministic lexicon wording for that mode. |
| Accessibility Narrator failure | Branch marked DEGRADED; the visual insight continues; client-side deterministic descriptions remain. |
| Verifier failure | `FALLBACK_UNVERIFIED`: no AI text published or labelled verified. |
| Verifier REJECTED | One constrained repair of the audience text → deterministic validation → verifier again → otherwise `REJECTED` (deterministic insight shown). |
| Throttling (429) | Retry with back-off honouring `Retry-After` up to 4 s; longer cool-downs fall back. SDK-internal retries are disabled so every retry is visible. |
| Malformed structured output | Treated as a failed attempt; retried once. |
| Seek / reset / match change | The session's epoch token cancels in-flight executions; cancelled runs are discarded, never published. |
| Duplicates | Executions are cached by match, insight content hash, audience mode, language, focus and accessibility; repeat requests reuse them. |

## Cost controls

Only insight candidates raised by the deterministic engine are eligible — never raw events. Automatic runs
require a fresh (≤ 2 match minutes), meaningful candidate (goal, big chance, or importance ≥ 70 with
confidence ≥ Medium and all sample minimums met) while the match is playing, capped at 4 per session; any
other insight runs only when a viewer opens it in the Agents tab. Process-wide: 40 executions per hour,
2 concurrent, 8 model calls per execution, per-agent output token caps (350–600), compact prompts (≤ 12
source events). A typical execution is 4 calls, ~7–12k input and ~1k output tokens.

## Execution modes

| Mode | When | What runs |
|---|---|---|
| `LIVE_FOUNDRY` | `FOUNDRY_PROJECT_ENDPOINT` and `AZURE_AI_MODEL_DEPLOYMENT_NAME` configured (`AgentWorkflow:Mode=Auto` or `LiveFoundry`) | Agent Framework workflow + Foundry agents. |
| `OFFLINE_TEMPLATE` | Not configured, or `AgentWorkflow:Mode=OfflineTemplate` (tests force this) | The same Agent Framework graph; every agent stage uses deterministic templates; no model call; outcome `DETERMINISTIC_ONLY`; nothing labelled verified. |
| `RECORDED_DEMO` | Static GitHub Pages demo | Replays `public/demo/agent-recordings.json`, recorded from real backend runs with `MatchShift.Cli agent-workflow --recording`. Always relabelled RECORDED_DEMO with `recordedFrom`; never shown as live. |

## Observability

`GET /api/demo/insights/{id}/agents` starts (on demand, within budget) or returns an execution; poll while
`status` is `RUNNING`. `GET /api/agents/status` reports mode, provider, model deployment, project host,
credential type, package versions, warm-up and budgets. Traces contain stage, agent, responsibility,
execution type/mode, provider, deployment, timings, superstep, status, handoffs, evidence IDs, input/output
summaries, model calls, retries, failure codes, recovery actions, verifier decision and token counts.
They never contain prompts, agent instructions, hidden reasoning, tokens or keys.

## The older deterministic narration pipeline

`AgentPipeline` (event analyst, pattern detection, football reasoning, storytelling, personalization,
verification) is deterministic C# — not AI agents — and still renders card wording. Its trace is available at
`/api/demo/insights/{id}/narration-trace`. The legacy optional `Narration:Provider=foundry` editorial
selection (`FoundryInsightProvider`, raw HTTP) is unchanged and off by default; it is not part of the
Agent Framework workflow.

## Try it

```powershell
# LIVE: uses the Foundry project from the environment; AzureCli is fastest locally after `az login`.
dotnet run --project tools/MatchShift.Cli -- agent-workflow --count 1 --credential AzureCli
dotnet run --project src/MatchShift.Api --launch-profile http -- --AgentWorkflow:Credential=AzureCli
# OFFLINE: no Azure contact.
dotnet run --project tools/MatchShift.Cli -- agent-workflow --offline true
```
