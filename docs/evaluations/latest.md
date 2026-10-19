# MatchShift evaluation: latest run

Generated 2026-10-19 20:21 UTC. Model deployment `gpt-4.1-mini` on the Foundry project at `matchshift-foundry-pd.services.ai.azure.com`.

Dataset: 39 rows (28 real agent outputs from 14 live workflow runs, 11 adversarial). Languages: en, es, fr. Audience modes: Analyst, Studio, TeachMe, WhatMatters. Insight types: ATTACK_CHANNEL_SHIFT, CONTROL_SHIFT, DANGEROUS_TURNOVER, GOAL, PASSING_STYLE_SHIFT, PRESSURE_INCREASE, TERRITORIAL_SHIFT, TURNOVER_SPIKE.

Model calls to generate the dataset (workflow agents plus the independent verifier): 77. Estimate printed before running: 39 rows; 117 Foundry judge calls; about 202 model calls in total (gpt-4.1-mini)

## History

- The first evaluation run exposed locale-specific numeric-parsing false positives in the deterministic validator (compact z-scores, French and Spanish match minutes, Spanish sample sizes, compound pass counts). They were corrected and the dataset re-validated; the initial report is kept as 2026-10-19-initial.md.
- Deterministic findings were recomputed offline with the corrected validator (no model calls): raw agent texts with findings fell from 6/28 to 1/28, and all 11 adversarial defects remain caught. Foundry run evalrun_72ca538217034d0bb108a4bc5d2958a9 then re-scored the same 39 texts; the initial run was evalrun_bcca1df81e6c4976b527c5e6846aa88c.

MatchShift reports three separate quality layers. They are not blended into one score.

1. **Deterministic validation**: exact code checks against the authoritative match data.
2. **Verification Agent**: the production Agent Framework verifier, run here on each text on its own.
3. **Microsoft Foundry Evaluations**: built-in AI-assisted judges, run in the Foundry project.

## Layer 3: Microsoft Foundry Evaluations

Eval `eval_70ac43398d444304bc1706704752147f`, run `evalrun_72ca538217034d0bb108a4bc5d2958a9`: **completed**. Judge deployment `gpt-4.1-mini`. Rows submitted: 39. Result counts: total 39, passed 34, failed 5, errored 0, skipped 0.
Judge model reported by Foundry: gpt-4.1-mini-2025-04-14. Judge calls with usage reported: 117; judge tokens: 205,599 input, 14,947 output.
Portal report (subscription segment removed; open the project's Evaluations page and find the eval ID): https://ai.azure.com/nextgen/r/<subscription>,rg-matchshift-hackathon,,matchshift-foundry-pd,matchshift/build/evaluations/eval_70ac43398d444304bc1706704752147f/run/evalrun_72ca538217034d0bb108a4bc5d2958a9

| Evaluator | Good rows: mean (1-5) | Good rows passing | Adversarial rows: mean | Adversarial rows passing |
|---|---|---|---|---|
| groundedness | 4.89 | 28/28 (100%) | 3.36 | 6/11 (55%) |
| relevance | 4.93 | 28/28 (100%) | 5.00 | 11/11 (100%) |
| coherence | 4.11 | 28/28 (100%) | 4.09 | 11/11 (100%) |

## Layer 1: deterministic validation of the real outputs

- Raw agent texts (before the Verification Agent and repair) with no findings: 27/28 (96%)
- Published audience texts with no findings: 14/14 (100%)
- Published accessible spoken summaries with no findings: 14/14 (100%)
- Multilingual consistency (same insight in several languages, every variant clean): 2/2 (100%)
- Audience-mode consistency (same insight and language in several modes, every variant clean): 1/1 (100%)
- Live workflow runs whose AI output was published as verified: 14/14 (100%)

## Adversarial set: which layer caught each defect

Caught by deterministic validation 11/11 (100%); by the Verification Agent alone 11/11 (100%); by Foundry groundedness 5/11 (45%); by at least one layer 11/11 (100%).

Verification Agent on unmodified good controls: not rejected 4/4 (100%).

| Defect | Deterministic | Verification Agent | Foundry groundedness |
|---|---|---|---|
| WRONG_SCORE | caught | caught (REJECTED) | caught (2.00) |
| UNSUPPORTED_PERCENTAGE | caught | caught (APPROVED_WITH_CHANGES) | missed (4.00) |
| INVENTED_PLAYER | caught | caught (APPROVED_WITH_CHANGES) | caught (2.00) |
| INVENTED_EVENT_ID | caught | caught (APPROVED_WITH_CHANGES) | missed (4.00) |
| FUTURE_EVENT | caught | caught (APPROVED_WITH_CHANGES) | missed (5.00) |
| INVENTED_MINUTE | caught | caught (APPROVED_WITH_CHANGES) | missed (5.00) |
| PLAYER_COUNT | caught | caught (APPROVED_WITH_CHANGES) | missed (5.00) |
| CAUSAL_CERTAINTY | caught | caught (APPROVED_WITH_CHANGES) | missed (4.00) |
| ACCESSIBILITY_CONTRADICTS | caught | caught (APPROVED_WITH_CHANGES) | caught (2.00) |
| LANGUAGE_VALUE_CHANGE | caught | caught (REJECTED) | caught (2.00) |
| AUDIENCE_FACT_CHANGE | caught | caught (APPROVED_WITH_CHANGES) | caught (2.00) |

## Every case

| Case | Kind | Lang | Mode | Insight | Workflow | Findings (raw / shown / introduced) | Groundedness | Relevance | Coherence |
|---|---|---|---|---|---|---|---|---|---|
| G01-Audience | Good | en | Analyst | ATTACK_CHANNEL_SHIFT | ApprovedWithChanges | 1 / 0 / - | 5.00 | 5.00 | 4.00 |
| G01-Accessibility | Good | en | Analyst | ATTACK_CHANNEL_SHIFT | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G02-Audience | Good | fr | Analyst | ATTACK_CHANNEL_SHIFT | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G02-Accessibility | Good | fr | Analyst | ATTACK_CHANNEL_SHIFT | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G03-Audience | Good | es | Analyst | ATTACK_CHANNEL_SHIFT | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G03-Accessibility | Good | es | Analyst | ATTACK_CHANNEL_SHIFT | ApprovedWithChanges | 0 / 0 / - | 4.00 | 5.00 | 4.00 |
| G04-Audience | Good | en | Studio | ATTACK_CHANNEL_SHIFT | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G04-Accessibility | Good | en | Studio | ATTACK_CHANNEL_SHIFT | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G05-Audience | Good | en | TeachMe | ATTACK_CHANNEL_SHIFT | Approved | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G05-Accessibility | Good | en | TeachMe | ATTACK_CHANNEL_SHIFT | Approved | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G06-Audience | Good | en | Analyst | GOAL | Approved | 0 / 0 / - | 5.00 | 5.00 | 5.00 |
| G06-Accessibility | Good | en | Analyst | GOAL | Approved | 0 / 0 / - | 4.00 | 5.00 | 4.00 |
| G07-Audience | Good | fr | Analyst | GOAL | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 5.00 |
| G07-Accessibility | Good | fr | Analyst | GOAL | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 5.00 |
| G08-Audience | Good | es | Analyst | GOAL | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G08-Accessibility | Good | es | Analyst | GOAL | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G09-Audience | Good | en | Analyst | DANGEROUS_TURNOVER | Approved | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G09-Accessibility | Good | en | Analyst | DANGEROUS_TURNOVER | Approved | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G10-Audience | Good | en | WhatMatters | PRESSURE_INCREASE | Approved | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G10-Accessibility | Good | en | WhatMatters | PRESSURE_INCREASE | Approved | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G11-Audience | Good | fr | Analyst | CONTROL_SHIFT | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G11-Accessibility | Good | fr | Analyst | CONTROL_SHIFT | ApprovedWithChanges | 0 / 0 / - | 5.00 | 4.00 | 4.00 |
| G12-Audience | Good | es | TeachMe | TERRITORIAL_SHIFT | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G12-Accessibility | Good | es | TeachMe | TERRITORIAL_SHIFT | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G13-Audience | Good | en | Analyst | TURNOVER_SPIKE | Approved | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G13-Accessibility | Good | en | Analyst | TURNOVER_SPIKE | Approved | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G14-Audience | Good | en | Analyst | PASSING_STYLE_SHIFT | ApprovedWithChanges | 0 / 0 / - | 5.00 | 5.00 | 4.00 |
| G14-Accessibility | Good | en | Analyst | PASSING_STYLE_SHIFT | ApprovedWithChanges | 0 / 0 / - | 4.00 | 4.00 | 4.00 |
| A-WRONG_SCORE | Adversarial | en | Analyst | ATTACK_CHANNEL_SHIFT | - | - / 1 / 1 | 2.00 fail | 5.00 | 4.00 |
| A-UNSUPPORTED_PERCENTAGE | Adversarial | en | Analyst | ATTACK_CHANNEL_SHIFT | - | - / 1 / 1 | 4.00 | 5.00 | 4.00 |
| A-INVENTED_PLAYER | Adversarial | en | Analyst | ATTACK_CHANNEL_SHIFT | - | - / 1 / 1 | 2.00 fail | 5.00 | 4.00 |
| A-INVENTED_EVENT_ID | Adversarial | en | Analyst | ATTACK_CHANNEL_SHIFT | - | - / 1 / 1 | 4.00 | 5.00 | 5.00 |
| A-FUTURE_EVENT | Adversarial | en | Analyst | ATTACK_CHANNEL_SHIFT | - | - / 1 / 1 | 5.00 | 5.00 | 4.00 |
| A-INVENTED_MINUTE | Adversarial | en | Analyst | ATTACK_CHANNEL_SHIFT | - | - / 1 / 1 | 5.00 | 5.00 | 4.00 |
| A-PLAYER_COUNT | Adversarial | en | Analyst | ATTACK_CHANNEL_SHIFT | - | - / 1 / 1 | 5.00 | 5.00 | 4.00 |
| A-CAUSAL_CERTAINTY | Adversarial | en | Analyst | ATTACK_CHANNEL_SHIFT | - | - / 2 / 2 | 4.00 | 5.00 | 4.00 |
| A-ACCESSIBILITY_CONTRADICTS | Adversarial | en | Analyst | ATTACK_CHANNEL_SHIFT | - | - / 1 / 1 | 2.00 fail | 5.00 | 4.00 |
| A-LANGUAGE_VALUE_CHANGE | Adversarial | fr | Analyst | ATTACK_CHANNEL_SHIFT | - | - / 1 / 1 | 2.00 fail | 5.00 | 4.00 |
| A-AUDIENCE_FACT_CHANGE | Adversarial | en | Studio | ATTACK_CHANNEL_SHIFT | - | - / 1 / 1 | 2.00 fail | 5.00 | 4.00 |

## Good cases with a finding (raw or published) or a failed judge

### G01-Audience

> Westhaven shift their attack strongly down the right after substitution. From 50' to 62', Westhaven directed 90% of attacks down the right, up from 31% earlier. This shift, with 9 right-side attacks and key player involvement count of 8, shows a statistically strong change after introducing Tavian Brooks.

- Raw agent text before verification: "Westhaven's attacks shift strongly right after substitution, z=3.3. From 50' to 62', Westhaven directed 90% of attacks down the right, up from 31% earlier. This shift, with 9 right-side attacks and 8 key players involved, shows a statistically strong change (z=3.3 > 2.7) after introducing Tavian Brooks."
  - Deterministic (raw): UNSUPPORTED_NUMBER: '8 key players' is not a grounded count of players.

