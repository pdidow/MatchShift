# MatchShift

Football intelligence you can question, verify, and replay.

MatchShift transforms synthetic, football-realistic match events into explainable intelligence for fans, analysts and people presenting the game. It identifies meaningful changes in pressure, territory, attacking channels, passing patterns, match control and tactical behaviour, alongside key moments such as dangerous turnovers. Rather than stopping at a statistic or an AI-written headline, it lets a viewer inspect why a change was flagged and which events support it.

**AI does not decide what happened in the match. The match data does.** Deterministic C# analytics detect changes, compute the numbers and retain the source events. Microsoft Agent Framework agents use those grounded facts to interpret the football significance, adapt the wording, create accessible narration and verify the result.

[Hosted application](https://matchshift-pd.azurewebsites.net/) · [60-second judge demo](https://matchshift-pd.azurewebsites.net/?demo=judge) · [Source repository](https://github.com/pdidow/MatchShift) *(currently private; to be made public before submission)*

## From an insight to its evidence

**INSIGHT → WHY? → SHOW ME → EVIDENCE REPLAY** is the central experience.

- **WHY?** explains the measured change, comparison windows, threshold, sample sufficiency and confidence. It separates **Fact**, **Inference** and **Possible explanation**, so a plausible tactical interpretation is never presented as an observed fact.
- **SHOW ME** identifies the supporting match events on the synthetic pitch. Events shown only for comparison are marked as context and are not counted as evidence.
- **Evidence Replay** reconstructs selected source sequences in 3D. It visualizes evidence; its animation does not create new evidence.

The flagship insight, **`MS-FLAGSHIP-001-I015`**, is raised at **63′**: **“Westhaven switch their attack down the right.”** Right-channel attacks rise from **31% to 90%**, a change of **+59 percentage points**. MatchShift rates its confidence **High** and importance **84**. It has **30 supporting events** and **1 context event**; the context event is not evidence. The flagship replay clip shows **5 of the 30 supporting events**, not the entire evidence set, and does not include the context event.

Player positions in replay may come from event data, interpolation between observations or formation-based estimates. Illustrative positioning is **not continuous tracking data**. Camera views change presentation only. The crowd and its sound add atmosphere, never match facts or supporting events.

## How the explanation is built and checked

The current workflow is:

```text
Synthetic match events → deterministic C# analytics → grounded InsightContext
→ Tactical Analyst Agent
→ Audience Narrative Agent + Accessibility Narrator Agent (parallel)
→ deterministic grounding validation → Verification Agent
→ final deterministic validation → APPROVED / APPROVED WITH CHANGES / REJECTED
```

The **Tactical Analyst** explains why a detected change may matter tactically while keeping inference distinct from fact. **Audience Narrative** adapts the same insight for the selected viewer. **Accessibility Narrator** prepares spoken and nonvisual wording from that evidence. The **Verification Agent** checks the generated explanation against the grounded match context. Code checks factual claims before that review and validates the final result again afterward. The wording changes; the evidence does not.

This verification is visible in the flagship recording. In **RECORDED DEMO**, I015 shows the real outcome **APPROVED WITH CHANGES** from a previously completed Microsoft Foundry / Agent Framework workflow. The initial generated wording contained unsupported numeric claims. Deterministic grounding flagged and removed an unsupported optional fact; the Verification Agent identified unsupported claims and supplied corrections; final deterministic validation passed. The fingerprint of the authoritative match facts remained unchanged. The result is intentionally shown with changes, rather than presented as perfect on its first attempt.

The execution labels have different meanings:

- **`LIVE_FOUNDRY`** — normal hosted use can run a real four-agent workflow against Microsoft Foundry using the App Service's managed identity.
- **`RECORDED_DEMO`** — `?demo=judge` replays previously recorded real workflow results. Viewing them starts no live Foundry run. If an insight has no recording, the page says so and does not fall back to live execution.
- **`OFFLINE_TEMPLATE`** — deterministic templates keep the app usable without a model call; their output is not labelled AI-verified.

## One match, different viewers

Fans can follow a clear match story; analysts can inspect measurements and events; studio presenters can use concise talking points. Club and player focus change what is emphasized without altering the match evidence. The interface and narration support **English, French, Spanish, German, Italian and European Portuguese (PT-PT)**.

The **Accessibility Center** offers spoken insights, audio match mode, Evidence Replay audio description, captions and transcript, high contrast, colour-vision options, reduced motion and flashing, text-size controls, simplified insights and keyboard support. Speech follows the authoritative match or replay timeline at a human rate: stale cues are dropped and important match moments take priority. Evidence Replay pauses the underlying match, takes exclusive narration focus, and restores the prior play state when closed. Crowd sound is decorative and ducks beneath narration.

The optional replay crowd is hardware-aware: **OFF**, **LOW** and **NORMAL** tiers match device capability. NORMAL uses simplified low-poly spectators and staggered reaction groups; some cheer or stand while others remain neutral. LOW uses a lighter version. Reduced Motion makes the crowd static. Neither the crowd nor its audio changes evidence, agent reasoning or event classification. The goal sound is a documented [CC0 crowd-recording excerpt](docs/THIRD_PARTY_ASSETS.md); chance and murmur sounds were synthesized for MatchShift.

## Graphics output for presentation

**Broadcast Preview** (`/broadcast`) is a broadcast-oriented graphics prototype: it presents the score and match clock, synthetic pitch and recent events, and a timed lower-third insight using Studio wording. The cleaner transparent **Overlay output** (`/overlay`) exposes a browser-source-style graphics surface backed by a machine-readable overlay contract.

These routes preview and expose graphics. MatchShift does **not** ingest, encode, synchronize or stream live video, and does not claim production broadcast integration. The value for broadcasters and streaming platforms is a way to present the same grounded match intelligence in a compact visual format.

## Microsoft technology and measured quality

The application runs on **Azure App Service** as an **ASP.NET Core / .NET** API serving a **React / TypeScript** web client. **Three.js** renders Evidence Replay. **Microsoft Agent Framework** orchestrates the four agents; live execution uses a **Microsoft Foundry** `gpt-4.1-mini` deployment through a system-assigned **managed identity**. **Microsoft Foundry Evaluations** assesses output quality separately from the application's deterministic validator and Verification Agent.

The latest committed evaluation contains **39 rows in EN / FR / ES**, including **28 published outputs**. Those outputs averaged **4.89/5 groundedness**, **4.93/5 relevance** and **4.11/5 coherence**. In the latest adversarial run, MatchShift's deterministic validator and Verification Agent each caught **11/11 injected defects**; generic Foundry groundedness caught **5/11**. The product supports six languages, while this Foundry evaluation covered **EN / FR / ES**. Details and limits are in the [evaluation report](docs/evaluations/latest.md).

## What a judge should try

Open the [judge demo](https://matchshift-pd.azurewebsites.net/?demo=judge), or choose **Guided demo · 60 s** in the app. Both start the same eight-step path: **Intro → Built for every viewer → Flagship insight → WHY? → SHOW ME → Evidence Replay → Agents → Trust / Verification**. Follow I015 from its 31% → 90% change through the supporting events and selected replay clip, then choose **How was this verified?** to inspect the recorded four-agent workflow and its **APPROVED WITH CHANGES** decision. This judge path consumes no live Foundry run; the normal hosted app remains live-capable.

MatchShift does not ask:

> “Do you trust the AI?”

It asks:

> “Do you want to see why?”
