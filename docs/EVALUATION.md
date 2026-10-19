# Evaluation: three quality layers

MatchShift checks its AI explanations in three separate layers. They answer different questions and are never blended
into one score.

| Layer | What it is | What it answers | Runs |
|---|---|---|---|
| 1. **Deterministic validation** | Exact code checks (`GroundingValidator`, typed numeric grounding) against the authoritative `InsightContext` computed from match events | Is every number, minute, score, player, team and evidence ID in the text actually in the match data? | Every live workflow run, and every evaluation row |
| 2. **Verification Agent** | The production Microsoft Agent Framework verifier (`gpt-4.1-mini` on Microsoft Foundry) | Does a reviewer reading the facts find unsupported or overstated claims, and can it correct them? | Every live workflow run; in the evaluation also on each text on its own |
| 3. **Microsoft Foundry Evaluations** | Foundry cloud evaluation with the built-in AI-assisted evaluators `builtin.groundedness`, `builtin.relevance`, `builtin.coherence` | How does an independent, standard judge rate the published explanations? | Only when a developer runs `foundry-eval` |

Deterministic validation decides what can be published. The Verification Agent reviews and corrects. Foundry
Evaluations is an offline measurement of the published output; it never gates the live product. A passing judge score
is another model's opinion, not proof a claim is true, which is why layer 1 stays exact.

## Latest run

The full report is [evaluations/latest.md](evaluations/latest.md); machine-readable results are in
`artifacts/evaluations/latest.json` and the dataset in `artifacts/evaluations/dataset.jsonl`.

| | |
|---|---|
| Date | 2026-10-19 |
| Foundry eval / run | `eval_70ac43398d444304bc1706704752147f` / `evalrun_72ca538217034d0bb108a4bc5d2958a9`, status **completed** (after the validator fix below; initial run `evalrun_bcca1df81e6c4976b527c5e6846aa88c`) |
| Where to see it | Foundry portal, project `matchshift`, **Evaluations** (the run is stored in the project) |
| Judge | `gpt-4.1-mini` deployment; Foundry reported model `gpt-4.1-mini-2025-04-14` for all 117 judge calls |
| Dataset | 39 rows: 28 published outputs (audience + accessible variants) from 14 live LIVE_FOUNDRY workflow runs, 11 adversarial |
| Coverage | English, French, Spanish; Analyst, Studio, TeachMe, WhatMatters; 8 insight types; 2 synthetic matches |
| Model calls | 77 to generate the dataset (workflow agents + independent verifier), then 117 judge calls per Foundry run (two runs on the same texts) |
| Judge tokens (latest run) | 205,599 input, 14,947 output (reported by Foundry) |

### Layer 3: Foundry judges (scale 1 to 5, pass at 3 or more)

| Evaluator | Published outputs: mean | passing | Adversarial: mean | passing |
|---|---|---|---|---|
| Groundedness | 4.89 | 28/28 | 3.36 | 6/11 |
| Relevance | 4.93 | 28/28 | 5.00 | 11/11 |
| Coherence | 4.11 | 28/28 | 4.09 | 11/11 |

Relevance and coherence are not meant to catch factual defects, so adversarial rows pass them as expected. The judge
is not deterministic: on identical texts the initial run gave groundedness 4.86 / coherence 4.29 on published outputs
and caught 4 of 11 defects (the accessible-text value change scored 4 there and 2 here).

### Adversarial set: which layer caught each defect

Each adversarial row is a real published output with exactly one injected defect. A deterministic catch counts only
findings the defect introduced. A Verification Agent catch means a decision other than APPROVED; in all 11 the verifier
also quoted the injected sentence as unsupported.

| Defect | Deterministic | Verification Agent | Foundry groundedness |
|---|---|---|---|
| Wrong score | caught | caught (REJECTED) | caught (2) |
| Unsupported percentage | caught | caught (with changes) | missed (4) |
| Uninvolved squad player named | caught | caught (with changes) | caught (2) |
| Non-existent event ID | caught | caught (with changes) | missed (4) |
| Real event after the insight | caught | caught (with changes) | missed (5) |
| Ungrounded match minute | caught | caught (with changes) | missed (5) |
| Player count never computed | caught | caught (with changes) | missed (5) |
| Causal certainty | caught | caught (with changes) | missed (4) |
| Accessible text changes the key value | caught | caught (with changes) | caught (2) |
| French text changes the key value | caught | caught (REJECTED) | caught (2) |
| Other audience mode changes the key value | caught | caught (with changes) | caught (2) |
| **Total** | **11/11** | **11/11** | **5/11** (4/11 in the initial run) |

The Verification Agent did not reject any of the 4 unmodified control texts (4/4 APPROVED).

**What this shows.** The generic groundedness judge catches changed headline facts (score, a named player, the main
value), but misses the precise defects that matter in sport: future events, invented minutes, invented IDs, counts and
overclaimed causes. That is the reason MatchShift does not replace exact validation with generic AI scoring.

### Layer 1 on the real outputs

| | Result |
|---|---|
| Raw agent texts (before verification) with no findings | 27/28 (22/28 before the validator fix) |
| Published texts with no findings | 28/28 |
| Multilingual groups (same insight in EN/FR/ES) consistent | 2/2 |
| Audience-mode group (same insight, several modes) consistent | 1/1 |
| Workflow runs published as verified | 14/14 |

**Validator fix.** The initial run exposed locale-specific numeric-parsing false positives. Manual review of the 9
findings on 6 raw texts (all reworded by the workflow before publishing) showed:

- **1 true positive.** "8 key players" turns a key-player-involvement score into a player count, which MatchShift
  rejects by policy.
- **8 false positives on grounded numbers.** These are coverage gaps in the validator's patterns:
  - "z=3.3" (twice);
  - the French ordinal minutes "la 50e et la 62e minute" and "à la 35e";
  - the Spanish "a los 35 minutos";
  - a sample in "muestra de 10 ataques";
  - a baseline and current sample written as "45 and 30 passes".

  No incorrect text was published because of them. The cost was wording: the repair step rephrased correct numbers,
  for example "muestra de 10 ataques" became "muestra suficiente".

These were corrected in `NumericGrounding.cs` with narrow, category-typed rules: z-score rounding, French and Spanish
match-minute patterns, a Spanish sample-size pattern, same-noun compound counts, and "long-ball share" samples counted
as passes. Each rule has a paired negative test (`LocaleNumericGroundingTests`). Re-validating the same dataset offline
(`--phase revalidate`, no model calls) leaves only the true positive. All 11 adversarial defects are still caught by
the finding each one introduces. The initial report is kept as
[evaluations/2026-10-19-initial.md](evaluations/2026-10-19-initial.md).

## Run it

Developer-triggered only. Nothing is scheduled, and nothing runs on user traffic.

```powershell
# Prints DATASET SIZE, EVALUATORS, ESTIMATED EVALUATOR CALLS, ESTIMATED TOTAL MODEL CALLS, EXPECTED SCALE, then stops.
dotnet run --project tools/MatchShift.Cli -- foundry-eval

# Live: 14 workflow runs + 15 verifier calls, then one Foundry cloud evaluation (39 rows x 3 judges). Needs `az login`
# and FOUNDRY_PROJECT_ENDPOINT / AZURE_AI_MODEL_DEPLOYMENT_NAME in the environment.
dotnet run --project tools/MatchShift.Cli -- foundry-eval --confirm true

# Phases: generate (dataset only), evaluate (Foundry only, reuses dataset.jsonl),
# fetch --eval ID --run ID (re-read a completed run, GET only), render (rebuild reports from latest.json, no calls),
# revalidate (recompute deterministic findings on dataset.jsonl with the current validator, no calls).
dotnet run --project tools/MatchShift.Cli -- foundry-eval --confirm true --phase evaluate
```

Authentication is Microsoft Entra ID: `AzureCliCredential` locally (`--credential Default` for
`DefaultAzureCredential`). No API key is used. The caller needs the **Foundry User** role on the project, the same role
the hosted app's managed identity already has.

## How it works

- **Dataset** (`tools/MatchShift.Evaluation/DatasetGenerator.cs`). 14 cases run the production workflow
  (`InsightWorkflowRunner`, same agents and deployment as the app). A delegating chat client records each agent's
  structured output. Each row keeps:
  - the case ID and kind (good or adversarial), language, audience mode and insight type;
  - the readable authoritative facts, the metrics, the supporting evidence IDs and the source events;
  - the tactical, audience and accessibility outputs, and the raw agent text;
  - the published text, the workflow outcome, and the deterministic findings;
  - for adversarial rows, the mutation and its notes.
- **Adversarial set** (`Adversarial.cs`). Eleven single-defect mutations of real published text.
- **Foundry cloud evaluation** (`FoundryEvaluationClient.cs`). Calls the project's Evals API
  (`/openai/v1/evals`, `/runs`, `/output_items`) with an Entra bearer token. Rows are sent inline. Each row sends only
  four fields: case ID, query, authoritative facts as `context`, and the published text as `response`.
- **Report** (`EvaluationReport.cs`). Keeps the three layers separate and lists every case.

## Cost controls

- One manual command, with no background or recurring evaluation, and nothing triggered by the app.
- The command prints the estimate first. Without `--confirm true` it makes no call.
- Bounded size: 14 workflow runs (each capped by the workflow's model-call budget), 15 verifier calls and 117 judge
  calls. Estimate printed: about 202 calls. Actual: 77 + 117 = 194.
- The existing `gpt-4.1-mini` deployment is the only model used, with no new deployment or resource. At published
  pay-as-you-go rates for gpt-4.1-mini, the judge tokens above come to roughly US$0.11; the portal's deployment metrics
  are authoritative.
- Unit tests never call Azure. `FoundryEvaluationTests` uses a fake HTTP handler and a fake credential.

## Privacy

Exported files contain no credentials, tokens, keys, tenant, subscription or principal IDs, agent instructions or
hidden reasoning. The portal link Foundry returns encodes the subscription. It is stored with that segment replaced by
`<subscription>`, and a test enforces this. The judges receive only the four row fields listed above.

## Limitations

- **Small dataset.** 39 rows on 2 synthetic matches. It is a regression check, not a statistically powered benchmark.
- **Shared judge model.** The judge is the same model family as the agents, so it may favour their wording. The
  deterministic layer is independent of any model.
- **Generic judges.** Groundedness is not football-aware. The misses above are expected, and are a finding, not a defect
  in Foundry.
- **No content-safety evaluators.** No violence, hate or indirect-attack evaluators were run. MatchShift's inputs are
  generated match events, not user text, but this is a gap to add if user-supplied text is ever accepted.

## Comparison with PitchLens (another entry, read-only review)

Both projects use Foundry cloud evaluation with `builtin.groundedness`, `builtin.relevance` and `builtin.coherence`.
The differences:

| | MatchShift | PitchLens |
|---|---|---|
| Rows sent to Foundry | 39: 28 published outputs + 11 adversarial | 4 retained answers in its latest documented run |
| Judge | `gpt-4.1-mini`, the existing deployment | `gpt-5-mini`, a separate judge deployment |
| What is evaluated | Output of a Microsoft Agent Framework multi-agent workflow | Single analyst plus a verifier call; Agent Framework is not used |
| Adversarial focus | Factual defects in sport: score, minute, player, event ID, counts, causal certainty, cross-language and cross-audience value changes, with a per-layer catch table | Planted-instruction (prompt-injection) answers, scored by Foundry safety evaluators |
| Content-safety evaluators | Not run | Violence, sexual, self-harm, hate/unfairness, indirect attack |
| Audiences | Analyst, Studio, TeachMe, WhatMatters, plus cross-mode value consistency | Casual and Analyst, with audience-pair conflict checks |
| Languages evaluated | English, French, Spanish, plus cross-language value consistency | English only documented in its evaluation |
| Accessible (spoken) variant evaluated | Yes | Not documented in its evaluation |
| Offline harness | Deterministic validator + adversarial mutations in unit tests | 36-case offline suite (18 scenarios x 2 audiences) with malformed-answer and planted-instruction cases |

PitchLens's content-safety evaluators and its larger offline failure-mode suite are where it leads. MatchShift leads
on:
- a per-layer adversarial catch table on real published text;
- multilingual and accessible-variant evaluation;
- a larger set scored by Foundry;
- evaluating an Agent Framework multi-agent workflow.
