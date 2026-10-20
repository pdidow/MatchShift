# MatchShift: submission draft and judge testing instructions

## Short pitch

MatchShift turns synthetic football events into explainable match stories for fans and studio presenters.
Its .NET analytics engine detects tactical changes and attaches the before/after statistics and supporting
events to each insight. A React web app lets viewers inspect WHY?, replay SHOW ME evidence, switch between
casual, analyst, teaching and studio modes, and follow a fictional club or player in English, French,
Spanish, German, Italian or Portuguese. Microsoft Foundry is the configurable production provider for evidence-bound editorial agents and recap
ordering, with strict application validation and template fallback. An explicitly labelled mock exercises
the same workflow locally without credentials; it does not demonstrate a real model call. The repository includes Azure Container Apps deployment
definitions, Azure Monitor instrumentation, GitHub Actions CI and Fabric-compatible dataset exports.

Only describe Foundry/Azure as running after verifying them in the submitted build. Fabric export
compatibility does not mean a connected Fabric workspace.

## Links to fill before submitting

| Artifact | Current status |
|---|---|
| Public GitHub code repository | Exact URL required; the `pdidow` profile alone is insufficient |
| Demo or downloadable test build | Current localhost demo is not reachable by remote judges |
| Demonstration video | Public URL required; duration must be less than 120 seconds |
| Category | Select after verifying the relevant integration |

## Intended platform and access

The intended platform is a desktop web browser connected to the ASP.NET Core API, which serves the React
UI. The local demo needs no login, payment or AI keys. Initial source setup downloads dependencies.
Fonts currently load from Google Fonts; system-font fallbacks keep the UI usable without that connection.
Foundry mode requires an entrant-provided configured backend. Do not ask judges to buy service access or
publish cloud credentials. Keep the submitted demo/build available through the end of judging.

Each browser tab gets its own random playback session ID. Sharing a URL with `session=<UUID>` deliberately
shares its controls and clock; the Broadcast link does this for an overlay. Sessions are bounded and
in-memory, and expire after 30 minutes without requests. Restarting the host loses session state.

## Run from source

Prerequisites: .NET SDK 10 and Node 22.12+ with npm. Starting in the repository root:

```text
cd src/MatchShift.Web
npm ci
npm run build
cd ../..
dotnet run --project src/MatchShift.Api --launch-profile http
```

Open `http://localhost:5002/?lang=en&mode=Analyst`. For a hosted demo use the supplied hosted URL.
`/health` should return `status: ok` and `synthetic: true`. No user account is required in this build.

## 60-Second Judge Demo

Choose **Guided demo · 60 s** in the top bar, or open `/?demo=judge` (for example
`https://matchshift-pd.azurewebsites.net/?demo=judge`). Each step performs one real product action and explains what
is on screen:

| Step | On screen | What it shows |
|---|---|---|
| 1 | Flagship insight at 63' (`MS-FLAGSHIP-001-I015`) | A change detected by deterministic analytics: facts, AI interpretation and verification are separate layers |
| 2 | WHY? | The metric (31% → 90%), windows, threshold, sample and confidence, from match events |
| 3 | SHOW ME | The 30 supporting source events (the card's count), plus 1 grey context event that is not counted as evidence |
| 4 | Evidence Replay | 3D reconstruction before and after the shift, with observed / interpolated / estimated positions |
| 5 | Agents | The Microsoft Agent Framework graph with parallel branches, deterministic and AI stages kept distinct, and live status chips |
| 6 | Trust | The verifier verdict and the Microsoft Foundry Evaluations proof |

The recommended insight is the one documented below: the flagship attack-channel shift. It has the clearest metric
change, 30 supporting events and before/after replay clips. Live workflows on it were approved in every evaluation
run. When the server is offline or a run falls back, the panel says OFFLINE TEMPLATE and shows no verified claim.

## Reproducible walkthrough

1. Select the flagship match, **Northbridge v Westhaven**, and reset to kick-off. Press Play. All clubs,
   players and events are fictional. 1x advances 30 match seconds per real second; use the scrubber to
   jump while presenting rather than depending on wall-clock timing.
2. Seek to **63:00** and pause. Select the attack-channel shift: Westhaven's right-side attacks rise from
   **31% to 90%**. Open **WHY?** for the baseline, recent window, threshold and event evidence.
3. Use **SHOW ME** to display the supporting routes on the synthetic pitch, then return to the live view.
4. Select **Teach me football** and compare with **Tactical analyst** and **Studio**. Switch to French
   or Spanish; the facts remain the same. In **My club / my player**, choose a club and a player to see
   perspective, ranked related insights and the player's observed-event summary with exact event references.
5. Select the shift and open **Agents**. Inspect the returned narrative, its actual source, and the stage
   trace. Sources distinguish `template`, `mock` and `microsoft-foundry`. Mock mode demonstrates handoffs
   and validation locally; it does not demonstrate a live AI call. Enable it using [FOUNDRY.md](FOUNDRY.md).
6. Seek to **90:00**, open **Recap**, choose Full-time and Broadcast. The flagship ends **2-2**. A half-time
   recap is available after the first half; before then it is locked.
7. Select the passing-style scenario and seek to minute 40 to inspect long-ball-share evidence.
8. Open Broadcast from the header at minute 63 in the flagship. `/broadcast?session=<same UUID>`
   combines the original synthetic pitch and timed overlay. `/overlay?session=<same UUID>` renders only
   the transparent overlay. Seeking to kick-off clears it; it expires at its declared match-clock time.

Optional repeatable PowerShell setup on a local test instance:

```powershell
$matchShiftSession = [guid]::NewGuid().ToString()
Invoke-RestMethod "http://localhost:5002/api/demo/reset?session=$matchShiftSession" -Method Post -ContentType 'application/json' -Body '{"matchId":"MS-FLAGSHIP-001"}'
Invoke-RestMethod "http://localhost:5002/api/demo/seek?session=$matchShiftSession" -Method Post -ContentType 'application/json' -Body '{"minute":63}'
"http://localhost:5002/?session=$matchShiftSession&lang=en&mode=Analyst&why=MS-FLAGSHIP-001-I015"
```

Open the printed URL to view that exact session. The attack-channel shift retains ID `MS-FLAGSHIP-001-I015`.

## Optional validation when Foundry access is available

This is not required for local development. The entrant configures the backend using environment variables documented in
[MICROSOFT_INTEGRATION.md](MICROSOFT_INTEGRATION.md). A `.env` file is not loaded automatically.
Restart the backend after configuring it. Never put service keys in a recording, source control or the UI.

1. `/api/meta` should show `aiProvider: microsoft-foundry` and `aiAvailable: true`. This proves
   configuration only, not a successful model response.
2. Seek to 63:00 and open the shift's **Agents** panel, or request
   `/api/demo/insights/MS-FLAGSHIP-001-I015/agents?session=<same UUID>&lang=en&mode=Analyst`.
3. Confirm `narrative.source` is `microsoft-foundry`, the verification note starts with `approved`, and
   the final verification step is Approved. Record the rendered narrative and its evidence.
4. At full-time, generate a recap and inspect its actual source too. `template` plus an unavailable or
   rejected note is a fallback result, not a successful AI demonstration.
5. Retain dated, sanitized evidence of the real run. The existing mocked provider test is useful software
   validation but cannot establish model availability, quota, permissions or actual response quality.

Live cards show immediate verified templates and asynchronously adopt accepted editorial selections.
WHY? and Agents reuse the same cached narrative. Per-card source labels reveal fallback or mock output.
See [UPGRADE_STATUS.md](UPGRADE_STATUS.md) for tested behavior and [PROJECT_DESCRIPTION.md](PROJECT_DESCRIPTION.md)
for current submission wording.
