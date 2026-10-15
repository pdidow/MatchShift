# Copilot instructions for MatchShift

- ALL football data is synthetic. Never add real clubs, players, Premier League branding, scraped data or paid feeds.
- The LLM never calculates statistics. New metrics belong in `MatchShift.Analytics` (deterministic C#, with tests),
  must return the event IDs that produce them, and must be documented in `docs/ANALYTICS.md`.
- Evidence before narrative: detectors create `Insight` records with `EvidenceItem`s; wording is rendered later.
- Templates in `MatchShift.Agents/Narrative/Lexicons` contain no statistics - only `{slots}`. Add every key to the
  English, French and Spanish lexicons; `NarrativeTests` fails otherwise.
- Explanations are cautious ("may", "could") and never claim intent.
- Coordinates are team-relative (attack towards X = 100, Y = 0 is the team's left); the API converts to absolute.
- The analytics engine must not reference `MatchShift.SyntheticData` scenario scripts or ground truth.
- Run `dotnet test` and `dotnet run --project tools/MatchShift.Cli -- evaluate` before changing detector thresholds.
