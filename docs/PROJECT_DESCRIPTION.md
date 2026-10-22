# MatchShift

**Football intelligence you can question, verify, and replay.**

MatchShift turns synthetic, football-realistic events from fictional clubs and players into grounded insights about meaningful changes in a match. The [Azure-hosted app](https://matchshift-pd.azurewebsites.net/) lets viewers inspect **WHY?** an insight was flagged, **SHOW ME** its supporting events, and use **Evidence Replay** to visualize selected evidence. The [judge demo](https://matchshift-pd.azurewebsites.net/?demo=judge) replays previously recorded real workflows without starting a live Foundry run.

## Evidence before narrative

**AI does not decide what happened in the match. The match data does.** Deterministic C# analytics compute match state, statistics and insight detections, then freeze a grounded `InsightContext`. Microsoft Agent Framework orchestrates a Tactical Analyst Agent, parallel Audience Narrative and Accessibility Narrator Agents, deterministic grounding validation, a Verification Agent, and final deterministic validation. Live agent execution uses a Microsoft Foundry `gpt-4.1-mini` deployment via the App Service's managed identity. Publication outcomes are **APPROVED**, **APPROVED WITH CHANGES**, or **REJECTED**. Offline templates use no model and are not AI-verified.

The flagship 63' Westhaven attack-channel insight shows a change from **31% to 90%** right-side attacks. **WHY?** gives comparison windows, threshold, sample and confidence. **SHOW ME** distinguishes **30 supporting events** from **1 context event**, which is not evidence. Evidence Replay shows a selected clip of **5 of those 30 supporting events**. Event positions, interpolation and formation-based estimates are labelled; illustrative positioning is not tracking data, and replay animation creates no new evidence. Camera, crowd and audio change presentation only.

## Viewers and outputs

Viewer modes include teaching, concise, tactical, club/player and studio perspectives. The interface and narration support English, French, Spanish, German, Italian and European Portuguese. Accessibility Center offers spoken insights, timeline-aware narration, audio description, captions, contrast and motion settings. Highlights and half-time/full-time recaps assemble the match story.

**Broadcast Preview** (`/broadcast`) presents timed, broadcast-style graphics over synthetic event animation. The transparent `/overlay` route supplies the clean graphics output. Neither route streams or transmits live video; this is a broadcast-oriented prototype, not a production video integration.

The current host is **Azure App Service**. The repository also contains alternative Azure Container Apps deployment definitions and Fabric-compatible data exports; neither is the current hosted path. Microsoft Foundry Evaluations tested the latest committed set of 39 EN/FR/ES rows; [evaluation details](EVALUATION.md) distinguish those results from six-language product support.
