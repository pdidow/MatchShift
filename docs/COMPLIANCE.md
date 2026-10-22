# Hackathon compliance audit

> **HISTORICAL / ARCHIVED (October 15–16, 2026).** This rules audit describes an earlier local build. Its technical status, deployment and repository-availability statements are not current. See [README.md](../README.md), [JUDGING.md](JUDGING.md) and [DEPLOYMENT.md](DEPLOYMENT.md) for the current app. Eligibility, rights and submission requirements still require the entrant's own review.

Audited 2026-10-15 against the Official Rules supplied by the entrant and
[Microsoft's published copy](https://github.com/microsoft/insidethegamehackathon/blob/main/OFFICIAL%20RULES.md).
Baseline reviewed: `1bf6488`; the historical audit below is retained for traceability.
**October 16 update:** local upgrades now include mock/Foundry editorial agents, strict evidence binding,
asynchronous live cards, passing/player views, isolated sessions and broadcast overlays.
[UPGRADE_STATUS.md](UPGRADE_STATUS.md) supersedes the technical limitations below. Live cloud validation
is not a development prerequisite and remains unverified; no Azure subscription or billing is required locally.

**Verdict: strong challenge alignment, but not ready to declare the submission compliant.**
The local product works and registration is confirmed. Public submission artifacts, actual AI execution, eligibility and rights
confirmations remain open. A working prototype does not resolve those entry requirements.

The entrant supplied GitHub account `pdidow` and local demo
`http://localhost:5002/?lang=en&mode=Analyst`. Both the page and `/health` respond successfully.
`/api/meta` reports `aiProvider: template`, `aiAvailable: false`. No Git remote is configured in this
checkout. A lookup of the likely `pdidow/MatchShift` repository returned 404; it may be private, absent or
named differently, so public availability is unverified. An account/profile URL is not the required
public project repository URL. A localhost URL
cannot give remote judges access to this machine.

## Required submission and entrant checks

Statuses describe evidence available in this audit, not a decision by the organizer.

| Rule | Status | Evidence / action needed |
|---|---|---|
| 1, 4: register on time | Confirmed | Microsoft's October 15, 2026 registration confirmation explicitly confirms registration for Inside the Game: Developer Hackathon. Private email details are not copied into the repository. |
| 4: platform profile and project submission | Pending | The confirmation says Innovation Studio profile setup instructions will arrive within 48 hours. Complete setup and submit all required project fields by the deadline. |
| 3, 4.10: eligible entrant and representative | Owner confirmation needed | Confirm age of majority, eligible residence, team of at most four, authorized representative if applicable, and absence of excluded employment, family/household, judge or other conflicts. Do not infer residence from the computer timezone. Government employees must also obtain employer clearance as specified in the overview. |
| 4.1: AI-powered match intelligence | Partial | The Foundry client/provider exists and has a mocked HTTP test. The running local demo uses templates. Configure a real deployment and show accepted AI output; a successful fallback is not proof of a successful model call. |
| 4.1(i): ingest, interpret, explain, render, personalize | Implemented | See stage-by-stage evidence below. |
| 4.2, 6.1: category and featured technology fit | Conditional | Choose a defensible category and demonstrate its technology. Azure/Foundry code alone does not prove a deployed integration. |
| 4.3: functionality on the declared platform | Locally verified | .NET API and React web app build; backend tests pass; local HTTP walkthrough succeeds. Desktop browser is the intended platform. Cloud/Docker installation and mobile layouts were not validated in this audit. |
| 4.3: new project after registration starts | Supporting evidence | First local commit `c354ea2` is dated 2026-10-15 06:19:26 -06:00, after 2026-09-29 09:00 PT. Git dates do not prove originality or exclude an earlier derivative; owner must confirm. |
| 4.4: short project pitch with Microsoft/Azure technologies | Draft ready | Copy and tailor the truthful pitch in [JUDGING.md](JUDGING.md); distinguish implemented, configured and planned services. |
| 4.4: public demo video, strictly under 120 seconds | Not supplied | A script and screenshots exist; no published video URL was supplied or found in project documentation. Record actual working behavior and publish on an allowed service. |
| 4.4: public GitHub code repository URL | Not established | `github.com/pdidow` is an account URL. Confirm the exact repository and anonymous code access, then add its URL to the submission. |
| 4.4: no previous contest win | Owner confirmation needed | Cannot be established from source code. |
| 4.4: permitted submission content | No issue found in reviewed material | Reviewed UI/code/docs concern fictional football analytics. Review the final video, audio, images and description too; do not add unlicensed music, club crests or broadcast footage. |
| 4.5: multiple submissions substantially different | Owner confirmation if applicable | Confirm any other entries are substantially different. |
| 4.3, 4.6, 4.7, 4.11: rights, originality, authorized integrations | Incomplete evidence | Simulator, fictional rosters and SVG UI are in source. Dependencies and externally loaded fonts require their applicable terms/notices. See [THIRD_PARTY.md](THIRD_PARTY.md). Owner must confirm rights to contributions, screenshots, voice and any added media. |
| 4.7: synthetic football-realistic data | Verified in implementation | Seeded simulator; fictional clubs/players; event integrity, reproducibility and ground-truth tests. No real match data or footage dependency found in the reviewed pipeline. |
| 4.8: unrestricted, free judging access through end of judging | Not established | Provide a reachable deployment or downloadable working test build, with [English setup instructions](JUDGING.md). Judges should not need their own paid AI account. Localhost alone is insufficient. |
| 4.9: English submission or translations | Repository ready | English README, docs and UI exist; French and Spanish are additional features. Final video and submitted text still need review. |
| 4.12: no disqualifying sponsor support | Owner confirmation needed | Confirm no prohibited funding, contractual development, preferential support or derivative project. Resolve any ambiguity with the organizer before the deadline. |
| 5: post-deadline modifications | Release process needed | Tag/archive the submitted version. Do not assume substantive post-deadline edits are allowed. |
| 7, 9-14: submission terms and personal data | Owner action | Read and accept the actual rules; keep third-party permissions and contributor consents. Do not put team members' personal data in public materials. Provide it only when requested through the organizer's proper process, with the required notification. |

## Challenge implementation

| Stage / feature | Assessment | Repository evidence |
|---|---|---|
| Ingest events as they happen | Strong for a synthetic demo | `DemoSession` feeds generated events in clock order; `LiveMatchAnalyzer` never looks ahead. This is simulated live playback, not a real match feed. |
| Interpret into statistics and patterns | Strong | Rolling metrics, pressure/control/chaos indices, shift and moment detectors, scenario evaluation. |
| Explain why a moment matters | Strong, with verification limits | Structured evidence, before/after comparisons, sample floors, thresholds, supporting event IDs, separated observation/inference/explanation. |
| Render alongside a match | Implemented prototype | Pitch visualization, insight cards, WHY?, SHOW ME, charts, timestamps and JSON/SSE. No production broadcast compositing or rendering-partner integration is demonstrated. The personalized audience route is also supported by the rules. |
| Personalize for different audiences | Strong | TeachMe, WhatMatters, Analyst, MyTeam and Studio modes; filtering and wording differ. Club/player focus includes involvement in supporting events, not continuous tracking of every player's movement. |
| Narratives and automated recaps | Implemented | Deterministic live cards, ranked highlights, generated halftime/fulltime recaps on request; optional Foundry in Agents and Recap. Recap availability is clock-gated. |
| Data innovation | Strong | Seeded possession simulator, six labelled scenario families, ground truth separate from analytics, JSONL/CSV/Parquet export. |
| Multiple languages | Implemented | English/French/Spanish/German/Italian/Portuguese lexicons, UI and accessibility strings, and number consistency tests. Some diagnostic labels and recap-style names remain English. |
| Player tags, pass difficulty, ball/shot speed, video auto-eventing | Partial / absent | Events carry player identity and location. No full pass-quality suite, ball/shot speed or video inference. Section 4.1(ii) presents these as features to consider; their absence is not automatically a disqualification. |

## Category and judging assessment

| Criterion (20% each) | Current strength | Main limitation |
|---|---|---|
| Technological implementation | Strong local architecture, reproducible synthetic data, tests and evidence | Actual Azure/Foundry deployment is unverified; Fabric is export compatibility and manual import instructions. |
| Agentic design and innovation | Clear stage responsibilities and fallback behavior | Custom sequential workflow, mostly deterministic, with one optional model provider. No Agent Framework, A2A or MCP integration; no demonstrated advantage against a single-agent baseline. |
| Real-world impact | Credible studio/fan explanation use case | One global in-memory playback session; visitors can change each other's match/clock. No demonstrated isolation, load handling, operational SLO or production evaluation. |
| UX and presentation | Functional UI, evidence drilldown and distinct viewer modes | Public demo and video still missing; live cloud latency and browser interaction need recording/rehearsal. |
| Category adherence | Plausible Foundry or Azure path | Choose based on a demonstrated integration, not the number of technology names in the README. |

Foundry is a reasonable category to develop next because a provider already exists, but needs a real model
run and evidence of how the platform improves development or operation. Azure Cloud Native needs an actual
deployment and operational evidence. The current implementation is weak evidence for **Best Multi-Agent
System** or **Best Enterprise Solution**. This is an engineering assessment, not a prediction of judges' scores.
The rules do not require every listed Microsoft technology or an MCP server.

## Verification limits that must remain explicit

- Live cards are template-generated even when Foundry is configured. Agents and Recap can call the model.
- Numeric verification checks membership in an allow-list; it does not bind every number to the correct
  metric, club or sentence. Definitional constants are allowed. This is not proof of semantic correctness.
- The default verifier has no other-club-name list, and intent checks use a finite phrase list.
- Event Analyst recomputes supported metrics only. On a mismatch, the current fallback uses the same
  insight's evidence; it is not evidence repair. Do not claim every unsupported fact is removed.
- Recap headings now pass through the same verifier as titles and paragraphs, with a regression test.

## Validation evidence

- Release backend tests after the audit fixes: **200 passed** across Core (23), SyntheticData (44),
  Analytics (73), Agents (52) and API (8), including the new recap-heading regression test. The API suite
  was run after the frontend build finished because both builds access the generated `wwwroot` assets.
- `npm run build`: passed (TypeScript and Vite).
- Local HTTP walkthrough: homepage/health 200, seek to 63 minutes, attack-channel insight `I014`, French
  TeachMe agent trace approved, seek to 90 minutes, broadcast recap reporting 2-2.
- `evaluate --matches 120`: attack-channel recall **0.57**, precision **0.62**; post-substitution recall
  **0.35**, precision **0.16**. Seven "forbidden-pattern violations" are detector false-positive scenario
  results, not hackathon-rule violations. This reproduces the limitations in MATCH_SHIFT_DETECTION.md.
- No actual Foundry response, Azure deployment, Fabric import, public video or judge-accessible build
  was verified. Cloud tooling `az`/`azd` was not available on PATH during this audit.

Audit fixes also make the returned model/template narrative visible in Agents, refresh personalizations
when the followed club/player changes, ignore stale agent requests, refresh recaps when matches change,
keep insight deep links pending until the requested insight exists, and correct the single-process
quick-start working directory. Documentation now distinguishes implementation from deployed services.

## Release checklist

- [x] Confirm registration: verified from Microsoft's October 15, 2026 confirmation email.
- [ ] Complete Innovation Studio profile setup after the invitation arrives.
- [ ] Confirm entrant/team eligibility privately.
- [ ] Confirm original/new work, ownership, no prior contest win, and no disqualifying sponsor support.
- [ ] Finish dependency and asset permissions/notices review; review final recording for third-party material.
- [x] Implement and validate configurable Foundry integration with credential-free mock and template fallbacks.
- [ ] If making live AI/category claims, retain a real model demonstration; mock results do not establish this.
- [ ] Choose and substantiate the category; keep production and agent claims within demonstrated behavior.
- [ ] Publish the project code to a public GitHub repository and verify access while signed out.
- [ ] Supply a reachable free demo or downloadable test build; test from another machine with the English instructions.
- [ ] Record and publish a video shorter than 120 seconds showing the actual desktop web app.
- [ ] Put pitch, exact repository URL, video URL, demo/build link and testing instructions on the project page.
- [ ] Submit before the deadline, retain confirmation, and tag the submitted version.
- [ ] Maintain judge access until November 10, 2026, 11:59 PM Pacific Time.

## Dates from the supplied rules

| Milestone | Pacific Time | Edmonton time |
|---|---|---|
| Registration deadline | October 20, 2026, 12:00 PM | October 20, 2026, 1:00 PM |
| Submission deadline | October 27, 2026, 11:59 PM | October 28, 2026, 12:59 AM |
| Keep testing available through | November 10, 2026, 11:59 PM | November 11, 2026, 12:59 AM |

The rules list judging starting before submissions close; plan to make the demo available before the
submission deadline. The overview's top-six notification and prize-pool wording do not replace the
Official Rules. Recheck the organizer's rules before submitting; changes or ambiguities require the
organizer's clarification, not assumptions recorded in this audit.
