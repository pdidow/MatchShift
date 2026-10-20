# MatchShift

**Football intelligence that explains itself.**

MatchShift is an explainable football companion that turns synthetic match events into tactical
observations, personalized stories and match recaps. Its deterministic analytics detect changes in
attacking pressure, territory, attacking channels, possession, tempo, long-ball share and match control, alongside goals,
big chances and dangerous turnovers.

All clubs, players and events are fictional. The demo replays generated events through an incremental
analysis pipeline, showing only the events available at each point in the match.

## Evidence behind the story

Each insight carries structured evidence. **WHY?** shows the statistics, comparison windows, sample sizes
and thresholds behind an observation. **SHOW ME** draws the referenced events on a synthetic pitch.

The narrative model separates observed facts, inferences and possible explanations. A flank shift can be
measured; the coach's intent cannot be established from those events alone.

In the flagship match, Westhaven's right-side attack share rises from **31% earlier in the match to 90%
in the 50'-62' window**. The viewer can inspect the comparison and visualize the supporting attacks.

## Different experiences from the same evidence

- **Teach Me Football:** plain-language observations with short explanations of football concepts.
- **Just Tell Me What Matters:** a filtered feed of the highest-importance developments.
- **Tactical Analyst:** more insights, before/after metrics, evidence and event visualizations.
- **My Club / My Player:** club-aware filtering, player-related insight ranking, and a full observed-event player summary with a timeline and evidence IDs.
- **Studio:** concise talking points and on-air wording.

Narratives are available in English, French, Spanish, German, Italian and Portuguese. Highlights and halftime/fulltime recaps help
turn individual moments into a match story.

## Deterministic analysis with optional AI narration

The .NET analytics engine computes statistics and identifies patterns. A deterministic replay gate
withholds inconsistent evidence before narration. Microsoft Foundry is the configurable production AI
provider: an Interpreter proposes an editorial treatment, a Skeptic reviews its evidence binding, and
an Editor finalizes it after at most one repair. Models choose supported treatments and recap section
order; deterministic code retains control of facts, wording and translations. This intentionally
constrains AI output rather than claiming free-form tactical reasoning.

Live cards, WHY? and Agents share the same asynchronously reviewed narrative. Playback stays responsive
while verified templates are available immediately. Invalid output, outages and timeouts preserve those
templates; corrupted underlying evidence is withheld. Modes are explicit: offline templates, a local
mock of the production protocol, or real Foundry. The mock requires no account, billing or secrets and
is visibly labelled. Live Foundry deployment and model quality have not yet been demonstrated.

## Built for second-screen and studio exploration

The React and TypeScript interface combines live event visualization, playback controls, insights, charts
and evidence drilldowns. Separate viewing sessions isolate playback controls. A versioned, timed
overlay contract drives a transparent `/overlay` route and `/broadcast` synthetic compositing demo. The repository includes a Foundry provider, Azure Container Apps deployment
definitions, Azure Monitor instrumentation, GitHub Actions CI and Fabric-compatible dataset exports.

MatchShift is a second-screen and broadcast-oriented prototype: it helps viewers understand what changed
and explore why that change may matter. It does not yet claim production broadcast integration, continuous
player tracking, unrestricted model reasoning, or a demonstrated advantage in live model quality.
