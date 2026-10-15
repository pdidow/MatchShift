# Agents

MatchShift's agents are not text-forwarding wrappers: each has one responsibility, an explicit input and output,
and a defined failure behaviour. The pipeline (`AgentPipeline`) runs per insight and returns a trace shown in the
UI's **Agents** tab (`GET /api/demo/insights/{id}/agents`).

| # | Agent | Input | Output | Failure behaviour | Implementation |
|---|---|---|---|---|---|
| 1 | **Event Analyst** | raw synthetic events up to detection time + cited evidence | re-derived facts (`cited = recomputed ✓/✗`) | any mismatch marks the step Rejected and forces verified template text | deterministic |
| 2 | **Pattern Detection** | rolling metrics, detector output | structured pattern: type, team, severity, confidence, strength (z / rate ratio) | n/a (detector already applied thresholds & samples) | deterministic (`MatchShiftDetector`) |
| 3 | **Football Reasoning** | verified facts + pattern + interpretation code | a cautious interpretation, allow-listed per pattern | withholds any interpretation not permitted for that pattern | deterministic rules |
| 4 | **Storytelling** | facts + interpretation | headline, fact, inference, explanation, ticker, studio line | AI timeout/error → template text | template provider or **Microsoft Foundry** |
| 5 | **Personalization** | viewer profile (mode, language, club, player) | wording/visibility per mode, Football 101 note, club perspective, player involvement | n/a | lexicons + `NarrativeFilter` |
| 6 | **Recap** | verified timeline (`RecapFacts`) | half-time / full-time recap in 4 styles | AI recap failing verification → template recap | `RecapBuilder` + `RecapNarrator` / Foundry |
| 7 | **Verification** (mandatory) | every generated sentence + evidence | Approved / Rejected with issues | rejected text is replaced by verified template text | deterministic (`VerificationAgent`) |

## Verification rules

1. **Numbers** - every number in the text must match an evidence value, a slot derived from evidence (minutes,
   windows, sample sizes, percentages), or a definitional constant (0, 1, 45, 50, 90, 100 - index scales and match
   length). Rounded presentations are accepted within rounding tolerance; `75%` when the evidence says `90%` is not.
2. **Clubs** - no club outside the two teams in the match.
3. **Intent** - phrases claiming intent or instruction are rejected (EN/FR/ES lists).

Template text is verified too: every insight × 3 languages × 5 modes, and every recap × 4 styles, pass in tests.

## Example trace (63', French, My Club = Northbridge)

```
Completed  Event Analyst Agent      RightAttackShare: cited 0.9 = recomputed 0.9 ✓; baseline 0.314 = 0.314 ✓
Completed  Pattern Detection Agent  ATTACK_CHANNEL_SHIFT · team WHA · severity High · confidence High (0.95) · z 3.29
Completed  Football Reasoning Agent interpretation 'channel_after_substitution' (hedged)
Completed  Storytelling Agent       "Westhaven attaque désormais par le côté droit"
Completed  Personalization Agent    shown in this mode · "Signal d'alerte pour Northbridge."
Approved   Verification Agent       approved - 9 numbers checked, no unsupported claims
```

## Tests

`AgentPipelineTests`: evidence re-derivation for all flagship insights; a provider that invents a number and
claims intent is rejected and replaced; a faithful rephrasing is accepted; an outage falls back; the Foundry
provider sends verified facts in the prompt, requests JSON, and its output is verified.
