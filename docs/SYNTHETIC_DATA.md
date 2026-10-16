# Synthetic data

> **ALL football data in MatchShift is synthetic.** Clubs, players, matches, events, scores and patterns are
> fictional and generated in-process. No real Premier League data, statistics, branding or feeds are used.

## Fictional clubs and players

`ClubCatalog` defines **Northbridge FC** (4-3-3) and **Westhaven Athletic** (4-2-3-1) by hand - the flagship
fixture - plus six dataset clubs (Ravensmoor Rovers, Stonebury Town, Larkfield United, Ashvale City,
Glenmarrow Albion, Copperhythe FC) whose 18-man squads are generated deterministically from invented name
fragments. Every player has an ID (`NBR-07`), name, shirt number, position, team and a **behaviour profile**:

| Tendency | Used for |
|---|---|
| passing involvement | who receives passes |
| forward-pass tendency | how vertical their passes are |
| preferred side, average position | where their actions happen |
| shot frequency, attacking aggressiveness | who shoots |
| pressing activity, recovery tendency | who wins the ball back |
| turnover tendency | who loses it |

## The simulator

`MatchSimulator` is possession-based, not physics. Each step, the team in possession performs one action
(pass, carry, shot) or is fouled or dispossessed. Probabilities come from the current **scenario phase**
(`TeamBehaviour`: control, pass accuracy, progression, press, channel weights, shot propensity, tempo,
counter-attack and directness) and from player profiles. The ball is tracked in the possessing team's frame and
flipped on every change of possession. Typical output: ~1,300 events, ~420 passes per team at ~80% completion,
10-20 shots, ~170-240 possessions - see `EventIntegrityTests.Match_volume_is_football_realistic`.

### Reproducibility

`System.Random`'s seeded algorithm is a runtime detail, so MatchShift owns its PRNG: **xoshiro256\*\*** seeded via
SplitMix64. The first outputs for seed 42 are pinned in a test; the same seed produces byte-identical event
streams everywhere (`ReproducibilityTests`).

### Scripted scenarios: behaviour, not labels

Scripts change *behaviour* and inject a few fixed sequences (goals, substitutions). They never write a "shift
happened" flag. The analytics engine has to **discover** every pattern from events alone.

## Flagship match: Northbridge FC v Westhaven Athletic (seed 20260327)

| Minutes | Script |
|---|---|
| 0-15 | balanced opening |
| 15-30 | Northbridge gain territorial control |
| 30-38 | sustained Northbridge pressure - **goal 34'**: Westhaven try to play out from the back, Northbridge's press intercepts, and they score straight through the left channel (the 3D replay demo sequence) |
| 38-45 | Westhaven counter-attack - **goal 43'** (1-1 at half-time) |
| 46-55 | balance returns |
| 55 | Westhaven substitution: **Tavian Brooks** (right winger) for Noah Penrose |
| 56-65 | Westhaven attack overwhelmingly down the right |
| 65-72 | Westhaven pressure rises |
| 72 | Northbridge lose the ball in their own third → big chance |
| 74 | **Westhaven goal** from the right, assisted by Brooks |
| 75-82 | Northbridge respond (substitutions at 76', 80') |
| 82-90 | high-tempo chaotic finish - **Northbridge equaliser 87'** (2-2) |

What the live detector discovers (from events only): Northbridge **control** shift 27', **pressure** 29', goal
35'; Westhaven equaliser on a fast break 44'; **ATTACK_CHANNEL_SHIFT** for Westhaven at **63'** (right-side
attacks 31% → 90%, z = 3.3), **POST_SUBSTITUTION_CHANGE** 68' linking it to Brooks, Westhaven control and
pressure ~70', the 72' dangerous turnover, Westhaven goal 75', Northbridge pressure response 84', equaliser 88'.

The seed was chosen with `matchshift seed-search` so the dice express the script cleanly; the script itself is
validated across 20 *other* seeds (`matchshift robustness`): attack-channel shift precision 0.92, recall 0.60.

## Labelled scenario library

`ScenarioLibrary.Generate(kind, seed)` returns a `LabelledMatch` = `Match` + `GroundTruth`.

| Kind | Behaviour change | Ground truth |
|---|---|---|
| `Stationary` | none (routine substitutions only) | forbidden: channel shift, post-substitution change - **negative control** |
| `AttackChannelShift` | one team commits 83% of attacks to one flank for 14-19 min | `ATTACK_CHANNEL_SHIFT`, team, window |
| `SustainedPressure` | 12-minute siege | `PRESSURE_INCREASE` |
| `CounterAttackSwing` | 12 minutes of fast breaks | `COUNTER_ATTACK_THREAT` |
| `ChaoticFinish` | frantic last 14 minutes | `CHAOS_INCREASE` |
| `SubstitutionImpact` | substitution followed by a flank overload | `POST_SUBSTITUTION_CHANGE`, `ATTACK_CHANNEL_SHIFT` |

Ground truth is a separate object; `MatchState` has no reference to it (`FlagshipScenarioTests.Ground_truth_is_not_part_of_the_event_stream`).

## Dataset expansion

```csharp
SyntheticMatchGenerator.GenerateMatch(seed: 12345);
SyntheticMatchGenerator.GenerateDataset(matchCount: 200, seedStart: 1000, ScenarioMix.Default);
```

Seeds are `seedStart … seedStart + matchCount − 1`; each match's scenario is drawn deterministically from its seed
using the mix weights. Uses: analytics tests, detector evaluation, agent evaluation, Fabric demonstrations and
model experiments. Export: `matchshift export --matches 200 --seed 1000` (see [Microsoft integration](MICROSOFT_INTEGRATION.md#microsoft-fabric)).

## Adapting another schema

The engine consumes `MatchEvent` only. To use an official hackathon synthetic schema, write an adapter that maps
each record to `MatchEvent`:

1. Map event types/outcomes to `EventType` / `EventOutcome`.
2. Convert coordinates to team-relative 0-100 (flip X and Y for the team attacking towards X = 0).
3. Provide `PossessionId` / `PossessionTeamId` (derive from changes of team in control if absent).
4. Emit in `SequenceNumber` order. Everything downstream - metrics, detectors, narratives - is unchanged.

## Passing-style scenario

`ScenarioKind.PassingStyleShift` keeps a short-passing home baseline for 30 minutes, then raises the
simulator's long-ball tendency. It emits ordinary pass events and a separate evaluation label. The
analytics project never reads that label. Seed 7001 is available in the demo match selector.
