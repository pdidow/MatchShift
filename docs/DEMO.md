# Demo video: target 1 minute 55 seconds

> **HISTORICAL / ARCHIVED video outline.** This storyboard predates the current recorded judge path and Broadcast Preview label. Follow [JUDGING.md](JUDGING.md) for the current eight-step demo and mode safety; do not treat the live-run rehearsal below as judge instructions.

The submitted video must be **strictly shorter than two minutes**, including titles and credits.
Publish a publicly viewable URL on YouTube, Vimeo, Facebook Video or Youku and put it on the project page.
Show the functioning desktop web app. Use original or authorized voice/music/images; no real match footage
is needed because the pitch and football events are synthetic.

Before recording, follow [JUDGING.md](JUDGING.md). Confirm the exact public repository and demo/build links.
If presenting AI, configure and rehearse a successful Foundry run first; the local default is templates.
Check `narrative.source`, not just `/api/meta`. Do not show keys or account details in the recording.

Use explicit seeks so the story does not depend on playback speed or elapsed presentation time.
The flagship attack-channel insight is raised at 63 minutes and has ID `MS-FLAGSHIP-001-I015`.

| Video time | Action / narration | Evidence shown |
|---|---|---|
| 0:00-0:07 | "MatchShift explains what changed in a football match, why it matters, and who it matters to." | App title, synthetic-data badge, fictional teams |
| 0:07-0:15 | Play a few seconds, then seek to 63:00 and pause | Event trail, live clock, changing metrics |
| 0:15-0:27 | Select the attack-channel shift | Westhaven right-side attack share: 31% to 90% |
| 0:27-0:42 | Open WHY? | Windows, sample sizes, threshold and distinction between facts and interpretation |
| 0:42-0:53 | SHOW ME, then back to live | Exactly the supporting routes on the synthetic pitch |
| 0:53-1:05 | Switch TeachMe, Analyst, then Studio | Different levels of detail and an on-air line from the same evidence |
| 1:05-1:13 | Switch French/Spanish and return to English | Same numbers, different narrative language |
| 1:13-1:35 | Select the shift, open Agents | Returned narrative, actual source and verification result. Show a real accepted Foundry result if claiming AI. |
| 1:35-1:50 | Seek to 90:00; Recap, Full-time, Broadcast | Automatically generated 2-2 recap and source/fallback status |
| 1:50-1:55 | Close with the value and submitted links | Explainable, personalized match intelligence |

### 3D replay segment (about 12 seconds)

Seek past 35:00, open the **Key moment** card "Westhaven lose it in their own half - Northbridge shoot" (35',
`MS-FLAGSHIP-001-I005`) and press **3D REPLAY**. Westhaven play out from the back, Northbridge's press intercepts
(caption `HIGH PRESS INTERCEPTION · #4 CASTELLANE`), and they score straight through the highlighted left channel.
Switch Tactical → Behind goal during the cutback. For a "see the moment the match changed" beat, open the 63'
**Match shift** card's 3D REPLAY instead: BEFORE SHIFT then AFTER SHIFT. Mention that faint rings are estimated
positions - the replay is a reconstruction of synthetic events. Deep link for rehearsal:
`/?session=<UUID>&replay=MS-FLAGSHIP-001-I005&replayAt=6&replayCam=tactical`.

Rehearse model latency. A service timeout can consume 20 seconds. If the recording is edited, keep the
demonstration faithful to actual behavior and do not imply a model responds faster than measured. A
template or mock recording must label that mode honestly. Live AI operation remains unverified; it is
not a prerequisite for local development. See FOUNDRY.md for the credential-free mock workflow.

Seek examples for the recording operator: set `$matchShiftSession` to the session UUID from the
Broadcast link in your browser. These requests then control the session being recorded:

```powershell
Invoke-RestMethod "http://localhost:5002/api/demo/seek?session=$matchShiftSession" -Method Post -ContentType 'application/json' -Body '{"minute":63}'
Invoke-RestMethod "http://localhost:5002/api/demo/seek?session=$matchShiftSession" -Method Post -ContentType 'application/json' -Body '{"minute":90}'
```

Deep links should include that same `session=<UUID>`. `/?why=MS-FLAGSHIP-001-I015` opens WHY? and `/?show=MS-FLAGSHIP-001-I015` opens SHOW ME once
that insight exists. They do not seek the match automatically.
