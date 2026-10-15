# Narratives, personalisation and languages

## Semantic insight, rendered language

The detector stores a language-neutral `Insight` (type, facts, evidence, interpretation code). Narratives are
rendered per request from that record, so switching language or viewer mode never changes the facts - it
re-renders them. `NarrativeTests.Facts_carry_identical_numbers_in_every_language` checks the set of numbers in
the English, French and Spanish FACT sentences is identical for every flagship insight.

## Template engine

`Lexicon` (one per language: `EnglishLexicon`, `FrenchLexicon`, `SpanishLexicon`) holds templates keyed by
`headline|fact|inference|teach|ticker|studio . TYPE [. variant]`, plus `explain.<interpretation>`, metric labels,
recap and highlight wording. Templates contain **no statistics** - only `{slots}`.

`NarrativeSlots` builds the slot values **only** from the insight's evidence, facts and the roster:
`{team}`, `{opponent}`, `{from}`, `{to}`, `{diff}`, `{window}`, `{recentMinutes}`, `{Shots.to}`, `{channelPhrase}`,
`{substitute}`, `{scorer}`, `{assistPhrase}` … Numbers are formatted per culture: `90%` (en), `90 %` (fr, es, with
a non-breaking space), `0.36` vs `0,36`. IDs and numbers are never translated - names come from the roster.

Variants handle grammar and meaning: `TEMPO_SHIFT.up/down`, `POST_SUBSTITUTION_CHANGE.channel/pressure/possession`,
and goals by what they did to the score (`goal_opener`, `goal_equaliser`, `goal_go_ahead`, `goal_extends_lead`,
`goal_pulls_one_back`).

Adding a language = one lexicon class with the same keys; a test fails if any slot or key is missing.

## Viewer modes

| Mode | Shows | Rendering |
|---|---|---|
| **Teach me football** | insights with importance ≥ 55 and goals | fact + inference + **Football 101** concept explanation |
| **Just tell me what matters** | importance ≥ 70 only | headline + one sentence |
| **Tactical analyst** | everything | fact, inference, possible explanation, full evidence |
| **My club / my player** | focus team's insights, opponent insights ≥ 65, insights involving the player | adds "Good news for…/A warning sign for…" and the player's involvement count |
| **Studio** | importance ≥ 50 | key number, on-air line, talking point |

Live narrative forms: **ticker** one-liner, **commentator/studio** line, **analyst** notes (evidence lines),
**fan** explanation (Teach me).

## AI wording (optional)

`IAiInsightProvider` has two implementations: `TemplateInsightProvider` (offline, default) and
`FoundryInsightProvider` (Microsoft Foundry). The Foundry prompt contains the already-verified template text and
the evidence, and asks the model to rephrase for the viewer and language **without adding facts or numbers**,
returning JSON. `NarrationService` verifies the result; anything rejected or failing is replaced by the template.
