# Microsoft integration

MatchShift can run locally with offline templates and no credentials. The current hosted app runs on Azure App Service; its normal mode is live-capable, while `?demo=judge` replays recorded real workflows without live Foundry calls.

**AI does not decide what happened in the match. The match data does.** Deterministic C# analytics produce a grounded `InsightContext`. Microsoft Agent Framework runs Tactical Analyst, then Audience Narrative and Accessibility Narrator in parallel, followed by deterministic grounding validation, Verification Agent, and final deterministic validation. Outcomes are APPROVED, APPROVED WITH CHANGES or REJECTED.

| Technology | Role in MatchShift | Where |
|---|---|---|
| **Microsoft Agent Framework** | Workflow graph (`WorkflowBuilder`, fan-out/fan-in, shared workflow state) and four `ChatClientAgent`s that explain each meaningful insight; see [AGENTS.md](AGENTS.md) | `src/MatchShift.Agents/Workflow` |
| **Microsoft Foundry** | Project model deployment (gpt-4.1-mini) behind the agents via `AIProjectClient.AsAIAgent`, Entra ID only; legacy editorial selection and recap ordering | `src/MatchShift.Infrastructure/Foundry` |
| **Microsoft Foundry Evaluations** | Cloud evaluation of the agents' published output with `builtin.groundedness`, `builtin.relevance` and `builtin.coherence`; a real run is stored in the Foundry project, reported separately from deterministic validation and the Verification Agent | `tools/MatchShift.Evaluation`, [EVALUATION.md](EVALUATION.md) |
| **Azure App Service (Free F1)** | Hosts the deployed app (API + React build); system-assigned managed identity calls Foundry with the Foundry User role | [DEPLOYMENT.md](DEPLOYMENT.md) |
| **Azure Container Apps** | Alternative, paid container deployment (not used for the hosted demo) | `infra/main.bicep`, `Dockerfile` |
| **Azure Container Registry** | Registry for the alternative Container Apps deployment, not the current host | `infra/main.bicep` |
| **Application Insights / Azure Monitor** | OpenTelemetry traces, requests and custom `MatchShift` metrics | `src/MatchShift.Api/Telemetry.cs` |
| **Microsoft Fabric** | Compatible export files and manual Lakehouse ingestion instructions; no automated/live Fabric connection | `matchshift export`, below |
| **GitHub** | CI (build, test, detector evaluation, UI build, container build), Copilot guidance | `.github/` |
| **Azure Developer CLI** | `azd up` provisions the alternative Container Apps deployment | `azure.yaml` |

## Microsoft Foundry

See [FOUNDRY.md](FOUNDRY.md) for complete template/mock/Foundry settings, endpoint and deployment formats,
authentication, timeouts, fallback and validation commands. No subscription, billing or secrets are
needed locally. Mock mode runs the same handoff protocol without model calls and is labelled accordingly.

The primary four-agent workflow uses the Foundry `gpt-4.1-mini` deployment and publishes only after code and Verification Agent checks. The older Interpreter/Skeptic/Editor editorial-selection path is **legacy and optional**, not the current Agents-page workflow. `/api/agents/status` reports configured server mode; inspect an individual execution to distinguish a live run, a previously recorded real run and an offline template. The hosted judge URL uses RECORDED DEMO even though the server remains live-capable.

The current App Service uses its system-assigned managed identity with the **Foundry User** role on the Foundry project for keyless live agent calls; see [DEPLOYMENT.md](DEPLOYMENT.md). The separate Bicep/Container Apps path is an alternative and must not be mistaken for the deployed App Service configuration.

## Azure deployment

The App Service deployment is described in [DEPLOYMENT.md](DEPLOYMENT.md). The following `azd` instructions concern an **alternative Container Apps experiment**, not the current hosted application; its definitions alone do not establish a working second deployment.

```bash
azd auth login
azd env set FOUNDRY_ENDPOINT https://<resource>.services.ai.azure.com/openai/v1   # optional
azd env set FOUNDRY_DEPLOYMENT <deployment>                                       # optional
azd env set FOUNDRY_RESOURCE_ID /subscriptions/.../accounts/<resource>             # optional
azd up
```

Provisioned: Log Analytics, Application Insights, Container Registry, Container Apps environment and app
(system-assigned identity, external ingress on 8080, liveness probe). The independent demo sessions are in-memory, so the app
runs one replica; scaling out would move playback state to a shared store and fan snapshots out through Azure
SignalR Service (the SSE contract maps directly onto SignalR groups per match).

## Application Insights

Set `APPLICATIONINSIGHTS_CONNECTION_STRING` (Bicep does this) and the API enables the Azure Monitor OpenTelemetry
distro: requests, dependencies (Foundry calls), logs, and the `MatchShift` meter (`matchshift.insights.raised`,
`matchshift.snapshots.served`, `matchshift.narratives.fallback`).

## Microsoft Fabric

```bash
dotnet run --project tools/MatchShift.Cli -- export --matches 200 --seed 1000 --out datasets/out/run1
```

| File | Lakehouse table | Grain |
|---|---|---|
| `events.parquet` / `events.csv` / `events.jsonl` | `events` | one row per event; team-relative **and** absolute coordinates |
| `matches.jsonl` | `matches` | one row per match (scenario, seed, teams, score) |
| `players.csv` | `players` | squad rows with positions |
| `ground_truth.jsonl` | `ground_truth` | expected patterns per match (kept apart from events, as in the engine) |
| `insights.parquet` / `insights.csv` / `insights.jsonl` | `insights` | detector output with primary evidence and `matches_ground_truth` |
| `minute_metrics.csv` | `minute_metrics` | per-minute control, pressure, chaos, score |
| `manifest.json` | - | dataset name, schema version, counts, synthetic notice |

Ingestion:

1. In a Fabric workspace create a **Lakehouse** (e.g. `matchshift_synthetic`).
2. Upload the export folder to `Files/` (or point a **Data pipeline** / OneLake shortcut at the storage you exported to).
3. *Load to Tables* for each Parquet/CSV file, or in a notebook:
   `spark.read.parquet("Files/run1/events.parquet").write.mode("overwrite").saveAsTable("events")`.
4. Example analyses: detector precision by scenario (`insights` ⨝ `ground_truth`), attack-side distributions by
   club, pressure curves around goals (`minute_metrics`), and a Power BI report over the semantic model.

Because the dataset is seeded and labelled, Fabric becomes an evaluation bench: regenerate with a new seed range,
re-run the detector, and compare precision/recall over time.
