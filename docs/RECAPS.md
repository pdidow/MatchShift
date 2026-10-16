# Highlights and recaps

## Highlight ranking (`HighlightRanker`)

Candidates are the match's insights (goals, big chances, dangerous turnovers, tactical shifts, control swings,
substitution impact, counter-attack threat, chaotic spells) plus **pressure peaks** (Pressure Index ≥ 60, best
per team per half). Each highlight has `Second`, `Kind`, `ImportanceScore`, `WhyItMattersCode`, `Evidence` and
`RelevantEventIds`.

- Goals score 92, +4 after 80', +4 for an equaliser or go-ahead goal (the 87' equaliser ranks first).
- Other kinds use the insight's importance (type base, severity, confidence, late-game and close-score bonuses).
- Highlights within 90 s for the same team are merged into the most important one (events are kept), so one
  sequence is one highlight. A goal or tactical shift is never folded into another highlight.

Highlights appear on the playback scrubber, in the Highlights tab (each with SHOW ME), and in recaps.

## Recap facts (`RecapBuilder`, deterministic)

`RecapFacts` for half-time (events before 45:00 only - tested) or full-time:

- score and every goal (minute, scorer, assist, how it came, running score);
- **momentum periods**: runs of ≥ 6 minutes with 10-minute Match Control ≥ 60;
- key shifts (top 5 by importance), control changes, pressure peaks;
- team stats (possession, shots, on target, chance quality, attacks and attack sides, turnovers, high recoveries,
  average and peak pressure); share of minutes rated chaotic;
- **key players** with a transparent score: goals×10 + assists×6 + key passes×2 + shots + chance quality×5 +
  recoveries×0.5 + positive insight mentions×3;
- ordered **story beats** (momentum, goals, shifts, the most important dangerous turnover).

## Recap styles (`RecapNarrator`, EN/FR/ES)

| Style | Shape |
|---|---|
| **Casual fan** | result, "here's how it unfolded", the story beats in plain language, player to watch |
| **Tactical analyst** | result + key numbers + attack sides, phases of control + pressure peaks, key shifts (headline: fact), goals, players |
| **Broadcast script** | `[OPEN]`, time-coded `[63']` on-air lines, result, `[CLOSE]` |
| **Social** | one post ≤ 280 characters with #MatchShift #SyntheticFootball |

Every recap is verified against `RecapFacts` (`NarrativeTests.Recaps_render_in_every_style_and_pass_verification`).
With Microsoft Foundry enabled, the Recap agent turns the verified template recap into flowing prose; if any
number fails verification the template recap is served.

Example (full time, casual, English):

> Northbridge and Westhaven share the points in a 2-2 draw. … Owen Blackwood scored for Northbridge on 35
> minutes, assisted by Samir Haddadi. … Westhaven switch their attack down the right. Their attacking play has
> shifted decisively towards the right flank. … All data in this recap is synthetic.
