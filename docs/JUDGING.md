# MatchShift judge walkthrough

**Football intelligence you can question, verify, and replay.** MatchShift analyzes synthetic, football-realistic events from fictional teams. Deterministic C# analytics identify meaningful changes and attach evidence. **AI does not decide what happened in the match. The match data does.** Microsoft Agent Framework agents interpret, adapt and verify the grounded result.

- [Hosted app](https://matchshift-pd.azurewebsites.net/)
- [Judge demo — recorded workflow](https://matchshift-pd.azurewebsites.net/?demo=judge)
- [Source repository](https://github.com/pdidow/MatchShift)

The recommended `?demo=judge` path is **RECORDED DEMO**. It reads previously recorded real Microsoft Foundry / Agent Framework results and starts **no live Foundry workflow**. Flagship `MS-FLAGSHIP-001-I015` retains its actual **APPROVED WITH CHANGES** outcome. If an insight has no recording, the page says so and never falls back to live execution. Normal hosted use outside judge mode remains live-capable; a live demonstration is optional and separate from this walkthrough.

## Eight guided steps

Choose **Guided demo · 60 s** in the top bar or open the judge link above.

| Step | Screen | What to inspect |
|---|---|---|
| 1 | Intro | What MatchShift does and which fictional match is shown |
| 2 | Built for every viewer | Language and Accessibility Center; the product supports EN, FR, ES, DE, IT and PT-PT |
| 3 | Flagship insight | At 63', Westhaven switch their attack down the right; right-side share rises from 31% to 90% |
| 4 | WHY? | Before/after windows, threshold, sample, statistical strength, confidence, and Fact / Inference / Possible explanation |
| 5 | SHOW ME | 30 supporting events and 1 visually distinct context event; context is not evidence |
| 6 | Evidence Replay | A selected clip showing 5 of 30 supporting events; context is not in this clip |
| 7 | Agents | “How MatchShift built this insight”; Tactical Analyst, parallel Audience Narrative and Accessibility Narrator, deterministic checks, Verification Agent; RECORDED DEMO and APPROVED WITH CHANGES |
| 8 | Trust / Verification | The grounded publication decision and evaluation evidence |

Evidence Replay uses positions from event data, interpolation and formation-based estimates; illustrative positioning is not tracking data. Replay animation creates no new evidence. Cameras change presentation only, while crowd and crowd sound provide atmosphere only.

## Other outputs

**Broadcast Preview** (`/broadcast`) previews timed graphics over synthetic match animation. **Overlay output** (`/overlay`) is the cleaner transparent graphics surface. Neither route streams video. A shared `session=<UUID>` shares playback state deliberately; separate browser sessions are otherwise isolated. The judge-mode link preserves its recorded/demo route.

## Run locally without credentials

Install .NET SDK 10 and Node 22.12+ with npm. From the repository root:

```text
cd src/MatchShift.Web
npm ci
npm run build
cd ../..
dotnet build
dotnet test
dotnet run --project src/MatchShift.Api --launch-profile http
```

Open `http://localhost:5002/?demo=judge` for the recorded judge path. Without Foundry configuration, ordinary local use runs offline templates; these make no model call and are not AI-verified. Optional live Foundry configuration is in [FOUNDRY.md](FOUNDRY.md). Web checks run from `src/MatchShift.Web` with `npm test` and `npx tsc -b --noEmit`.

The latest committed Foundry evaluation covers **39 EN/FR/ES rows**, including **28 published outputs** and **11 adversarial defects**. Published-output averages are groundedness **4.89/5**, relevance **4.93/5**, coherence **4.11/5**. The deterministic validator and Verification Agent each caught **11/11** injected defects; generic Foundry groundedness caught **5/11** in the latest adversarial run. Six-language product support does not mean the evaluation covered all six languages; see [EVALUATION.md](EVALUATION.md).
