# Two-minute demo

Setup: flagship match loaded (default), **Tactical analyst**, **English**, speed **2x** (a half takes ~45 s).
Run `dotnet run --project src/MatchShift.Api --launch-profile http` with the UI built, open http://localhost:5002.
Rehearse with the scrubber: click anywhere on it to seek; markers are goals (white) and shifts (green).

| Time | Action | What the audience sees |
|---|---|---|
| 0:00 | Load the page | MatchShift splash - *Football intelligence that explains itself* - "Synthetic data" badge |
| 0:05 | **Play** | Live pitch trail, clock running, Match Control and Pressure bars moving |
| 0:15 | (~29') | "Northbridge turn up the pressure" card - 19 → 54 Pressure Index |
| 0:25 | (~27-35') | "Northbridge take control", goal 35'; momentum chart tilts blue |
| 0:30 | Speed **5x** | Westhaven fast-break equaliser 44' |
| 0:35 | (~63') pause | **MATCH SHIFT** - "Westhaven switch their attack down the right", **31% → 90%** |
| 0:45 | **WHY?** | Evidence: 0'-50' vs 50'-62', +59 pts vs 25-pt threshold, 10 attacks (min 8) passed, z 3.3, confidence High |
| 0:55 | **SHOW ME** | Pitch shows only Westhaven's attacks: every arrow lands in the shaded right flank |
| 1:05 | Back to live, play | "Tavian Brooks's arrival reshapes Westhaven's attack" (68'), Westhaven pressure, 72' costly turnover, 75' goal |
| 1:15 | Viewer → **Teach me football** | Same insight now carries a Football 101 explanation of attack channels |
| 1:25 | Viewer → **Tactical analyst** / **Studio** | Deep evidence / on-air line "Since 50', Westhaven have gone down the right…" |
| 1:35 | Language → **Français**, then **Español** | Narrative changes; numbers (31 %, 90 %) and evidence stay identical |
| 1:43 | **Recap** tab → Half-time / Full-time, Casual → Broadcast | Story-driven recap; 2-2 after the 87' equaliser |
| 1:50 | Select the shift card → **Agents** tab | Event Analyst re-derives 0.314/0.9 ✓ … Verification: approved, 9 numbers checked |
| 1:57 | End | MatchShift - *Football intelligence that explains itself.* |

Tips

- Deep links for recording: `/?why=MS-FLAGSHIP-001-I014` opens WHY?, `/?show=MS-FLAGSHIP-001-I014` opens SHOW ME
  (insight IDs are stable because the match is deterministic; check `/api/demo/insights`).
- `POST /api/demo/seek {"minute": 63}` jumps the session (the UI follows within a tick).
- `matchshift narrate --lang es --mode Studio` prints every card's studio line for a voice-over script.
