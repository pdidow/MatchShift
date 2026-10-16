# Analytics

> All data is synthetic. Every metric below is a **MatchShift-derived** metric, not an official Premier League statistic.

## MatchState (incremental)

`MatchState.Ingest(event)` maintains, as events arrive in sequence order:

| Derived record | Definition |
|---|---|
| **Possession** | chain of events sharing `PossessionId`; start reason (kick-off, regain, set piece, restart), end reason (shot, lost, offside, period end…), first final-third and first shot times |
| **Possession loss** (turnover) | possession ends in open play with the opponent in control after a misplaced pass or a dispossession - not after a shot or offside. Located where the ball was lost |
| **Attack** | a spell of a possession in the opponent's half (on-ball actions beyond X = 55). Recycling the ball back below X = 45 and coming forward again starts a new attack |
| **Attack channel** | the channel (left < 33.3 ≤ centre < 66.7 ≤ right, attacking team's perspective) where most of the attack's actions happened; ties go to where it arrived |
| **Final-third entry** | completed pass/carry from outside to inside X ≥ 66.7 |
| **Box entry** | completed pass/carry ending in the penalty area from outside it |
| **Shot record** | location, header flag, outcome and MatchShift Chance Quality |

Nothing looks ahead. The one consequence-based definition - *dangerous* turnover - becomes true only when the
opponent's shot has actually happened.

## Windows and comparisons

`AnalysisWindow` is a half-open span `[from, to)` of match seconds. Comparisons used by detectors:

| Comparison | Used by |
|---|---|
| last 12 min vs **match so far** | attack-channel shift (stable baseline) |
| last 8 min vs previous 15 | pressure, shot pressure, counter-attack threat |
| last 10 min vs previous 15 | control, possession, territory, tempo, chaos |
| last 5 min vs previous 10 | turnover spike |
| before vs after a substitution | post-substitution change |

## Team window metrics

`MetricsCalculator.Compute(state, window)` returns both teams' metrics. Every countable metric also returns the
**event IDs** that produced it, so any number can be shown on the pitch.

Possession share is **time-based**: the gap to the next event (capped at 20 s, same period) is credited to the
team in possession. Other metrics: passes attempted/completed, forward passes (≥ 10 units gained), progressive
passes, long balls, final-third passes (for field tilt), entries, box entries, attacks by channel, shots, on
target, goals, chance quality, big chances, corners, turnovers, dangerous turnovers, recoveries, high recoveries
(X ≥ 60), tackles, interceptions, fouls, average on-ball X (territory), field tilt, possessions started, passes
per possession, fast transitions (regain → attacking third within 15 s), counter-attack shots, tempo
(passes + carries per minute of own possession).

## MatchShift Chance Quality (MSCQ)

Location-only scoring probability: distance *d* in metres to the goal centre (pitch 105 × 68), visible goal
angle *a*:

`MSCQ = clamp(0.06·e^(−d/30) + 0.9·e^(−d/9)·√(0.4 + 0.6·a/(π/2)), 0.01, 0.85)`, headers × 0.65.
Penalty spot ≈ 0.25, six-yard box ≈ 0.5, edge of the area ≈ 0.11. Big chance ≥ 0.30.

## MatchShift Pressure Index (0-100, per team)

Each component is a per-minute rate (or a position) scaled linearly between a floor and ceiling, weighted, and
summed - components always add up to the index, so the UI can show *why* it moved.

| Component | Floor | Ceiling | Weight |
|---|---|---|---|
| Attacking-third entries / min | 0.3 | 2.2 | 0.25 |
| Penalty-area entries / min | 0 | 0.8 | 0.15 |
| Shots / min | 0 | 0.5 | 0.20 |
| High recoveries / min | 0 | 0.6 | 0.15 |
| Corners / min | 0 | 0.3 | 0.05 |
| Average on-ball X | 38 | 68 | 0.20 |

## Match Control (0-100, home perspective; away = 100 − home)

Weighted sum of the home team's **share** of each quantity (0.5 when neither team registers any). No ML.

| Component | Weight |
|---|---|
| Possession time | 0.25 |
| Field tilt (attacking-third passes) | 0.20 |
| Attacking-third entries | 0.15 |
| Penalty-area entries | 0.10 |
| Chance quality | 0.15 |
| Recoveries (turnovers forced) | 0.15 |

A team "has control" at ≥ 57 (balanced band ±7); momentum periods in recaps require ≥ 60 for 6+ minutes.

## Control vs Chaos (0-100, both teams)

| Component | Floor | Ceiling | Weight |
|---|---|---|---|
| Possession changes / min | 1.5 | 5.0 | 0.25 |
| Open-play turnovers / min | 1.2 | 4.6 | 0.20 |
| Fast transitions / min | 0 | 1.2 | 0.20 |
| Shots / min | 0 | 0.45 | 0.10 |
| Long-ball share of passes | 0.05 | 0.35 | 0.10 |
| Short possessions (5+ passes = 0, ≤ 1.5 = 1) | 0 | 1 | 0.15 |

**CONTROLLED** < 25 ≤ **BALANCED** < 55 ≤ **CHAOTIC**. Calibrated on synthetic stationary matches (p10 ≈ 10,
median ≈ 30, p90 ≈ 50) versus scripted chaotic finishes (≈ 85-90); `matchshift calibrate` prints the rolling timeline.

## Live timeline

`LiveMatchAnalyzer` records one `TimelinePoint` per minute: Match Control (10-min window), both Pressure Indices
(5-min window), Chaos (10-min window) and the score. It powers the momentum charts and recap momentum periods.

## Passing and player upgrades (October 16, 2026)

`PassingStyleShiftDetector` compares long-ball share (LongBall-tagged passes / all attempted passes)
in adjacent eight-minute windows. Each needs at least 20 passes and a difference of 0.30. The standard
persistence and cooldown rules apply. Evidence includes both windows, both samples and all pass IDs
used in the comparison. This measures a passing mix, not a pass network or pass difficulty.

`PlayerPerformance.Calculate` uses only observed events: passes, completed passes, progressive passes
(the same predicate as team analytics), shots, recoveries and derived possession losses. Each total
has a separate event-ID list. The timeline includes primary and related-player events; totals count
primary actions, so receiving a pass is not counted as making one. Losses may only be recognized once
the possession has ended. Selected-player insights rank ahead of other chronological insights.
