# Match shift detection

`MatchShiftDetector` is deterministic. It runs every shift detector at each one-minute tick, runs the moment
detector as each event arrives, and applies noise control before anything becomes an `Insight`.

## Detectors

| Type | Fires when (defaults in `DetectionOptions`) |
|---|---|
| `ATTACK_CHANNEL_SHIFT` | a flank's share of attacks (last 12 min vs match so far) rises ≥ 25 pts to ≥ 55%, ≥ 8 recent / 10 baseline attacks, one-sided two-proportion **z ≥ 2.7**, held 2 ticks |
| `POST_SUBSTITUTION_CHANGE` | 7-15 min after a substitution, vs the 15 min before: flank share +22 pts (z ≥ 2.4), or Pressure Index ±32 (after 9+ min), or possession ±22 pts |
| `PRESSURE_INCREASE` / `DECREASE` | Pressure Index (8 vs previous 15) +18 to ≥ 45 and ≥ 12 above the opponent / falls from ≥ 55 to ≤ 25 |
| `CONTROL_SHIFT` | Match Control (10 vs 15) moves ≥ 20 towards a team now ≥ 62 |
| `TERRITORIAL_SHIFT` | field tilt +28 pts to ≥ 70% |
| `POSSESSION_SHIFT` | possession share ±18 pts |
| `TEMPO_SHIFT` | actions per possession-minute ±40% |
| `CHAOS_INCREASE` | Chaos index enters CHAOTIC from ≤ 45, +15 |
| `SHOT_PRESSURE` | ≥ 5 shots in 8 min, chance quality ≥ 0.6, rate ≥ 2× before |
| `TURNOVER_SPIKE` | ≥ 9 turnovers in 5 min, rate ≥ 2.2× before |
| `COUNTER_ATTACK_THREAT` | ≥ 3 fast breaks in 8 min, ≥ 1 ending in a shot, rate ≥ 2× before |
| `GOAL` · `BIG_CHANCE` · `DANGEROUS_TURNOVER` | goal; non-scoring shot with MSCQ ≥ 0.30; own-half loss punished by a shot within 25 s (own third: any attempt ≥ 0.12; elsewhere ≥ 0.25) |

## Noise control

1. **Warm-up** - shift detectors are silent for the first 18 minutes.
2. **Minimum samples** - every detector checks its own sample floor; evidence records whether it passed.
3. **Persistence** - a shift must hold for 3 consecutive ticks (2 for channel and substitution).
4. **Active-until-cleared** - a shift that persists is announced once; it can fire again only after its condition clears.
5. **Cooldown** - same type/team/variant: 12 min; same type/team: 6 min.
6. **Redundancy suppression** - possession/territorial shifts are dropped right after a control or pressure shift
   for the same team; a pressure *decrease* is dropped right after the opponent's pressure increase.
7. **Moment spacing** - one non-goal moment of a kind per team per 150 s; a chance created by a dangerous turnover
   is told once, as part of the turnover.

## Severity and confidence

- **Severity** from how far the change exceeds its threshold: ≥ 1.75× High, ≥ 1.3× Medium, else Low.
- **Confidence** for proportion tests = Φ(z) × sample factor (min(1, 0.7 + 0.3·n/12)); for index tests it grows
  with threshold exceedance and sample size. ≥ 0.90 High, ≥ 0.75 Medium.
- **Importance** (0-100) = type base + severity ± confidence + late-game and close-score bonuses; goals = 100.

## Why attacks, not actions, are the sample

Actions inside one possession are strongly correlated - a team that enters down the right tends to stay there.
Testing actions overstated the evidence and produced frequent false alarms in stationary matches, so the unit is
the **attack**. At realistic volumes (≈ 0.7 attacks per team-minute) a 0.33 → 0.71 flank shift needs ~16 attacks
to separate from noise at a 1% false-alarm level, which is why the detector tests flanks only, uses a strict z and
a match-so-far baseline, and reports confidence rather than over-claiming.

## Measured performance (labelled synthetic scenarios)

`matchshift evaluate --matches 120` (default scenario mix, seeds 1000-1119). The analytics engine sees only
events; ground truth is compared afterwards. A detection counts if raised between 2 min before and 8 min after
the labelled window.

| Type | Recall | Precision | False alarms per negative-control match |
|---|---|---|---|
| ATTACK_CHANNEL_SHIFT | 0.57 | 0.62 | 0.16 |
| POST_SUBSTITUTION_CHANGE | 0.35 | 0.16 | 0.40 |
| PRESSURE_INCREASE | 1.00 | n/a* | - |
| CHAOS_INCREASE | 1.00 | n/a* | - |
| COUNTER_ATTACK_THREAT | 0.50 | n/a* | - |

\* Pressure, control and chaos changes occur naturally in football, so unlabelled detections are reported as a
background rate (pressure 1.1, control 1.3, chaos 0.13 per match), not as errors.

Flagship script re-simulated with 20 other seeds (`matchshift robustness`): channel shift recall 0.60 /
**precision 0.92**, dangerous turnover 1.00, chaos 0.85, Northbridge/Westhaven pressure 0.67.

Honest reading: the channel detector is tuned for precision; post-substitution change is the noisiest detector -
it states a factual before/after difference and never claims the substitution *caused* it. `matchshift
sweep-channel` reproduces the precision/recall trade-off used to choose the thresholds.

## Tests

`AttackChannelShiftTests` (30% → 70% triggers; stable mix, small fluctuation and too-few-attacks do not; once
per cooldown; away team / left flank), `DetectorTests` (determinism, **no look-ahead** at 30'/63'/75', flagship
patterns, warm-up, cooldown, evidence re-computation, negative-control rate).

## Passing-style addition

PASSING_STYLE_SHIFT compares long-ball share across adjacent eight-minute windows, with at least 20
passes per window, an absolute 0.30 change and the shared persistence/cooldown logic. Unit tests include
a labelled change, stationary control, small samples and exact event references. A new seeded scenario
changes directness after minute 30. Held-out seeds 82000?82011 produced 12/12 recalled labels; the
120-match existing mix produced nine unlabelled passing detections, not an established precision score.
See [upgrade validation](UPGRADE_STATUS.md) for limits and commands.
