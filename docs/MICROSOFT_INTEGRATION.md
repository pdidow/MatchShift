# Microsoft integration

MatchShift runs fully offline; each Microsoft service below *strengthens* the product without becoming a
dependency of the demo.

| Technology | Role in MatchShift | Where |
|---|---|---|
| **Microsoft Agent Framework** | Workflow graph (`WorkflowBuilder`, fan-out/fan-in, shared workflow state) and four `ChatClientAgent`s that explain each meaningful insight; see [AGENTS.md](AGENTS.md) | `src/MatchShift.Agents/Workflow` |
| **Microsoft Foundry** | Project model deployment (gpt-4.1-mini) behind the agents via `AIProjectClient.AsAIAgent`, Entra ID only; legacy editorial selection and recap ordering | `src/MatchShift.Infrastructure/Foundry` |
| **Azure App Service (Free F1)** | Hosts the deployed app (API + React build); system-assigned managed identity calls Foundry with the Foundry User role | [DEPLOYMENT.md](DEPLOYMENT.md) |
| **Azure Container Apps** | Alternative, paid container deployment (not used for the hosted demo) | `infra/main.bicep`, `Dockerfile` |
| **Azure Container Registry** | Image registry, pulled via managed identity (AcrPull) | `infra/main.bicep` |
| **Application Insights / Azure Monitor** | OpenTelemetry traces, requests and custom `MatchShift` metrics | `src/MatchShift.Api/Telemetry.cs` |
| **Microsoft Fabric** | Compatible export files and manual Lakehouse ingestion instructions; no automated/live Fabric connection | `matchshift export`, below |
| **GitHub** | CI (build, test, detector evaluation, UI build, container build), Copilot guidance | `.github/` |
| **Azure Developer CLI** | `azd up` provisions and deploys | `azure.yaml` |

## Microsoft Foundry

See [FOUNDRY.md](FOUNDRY.md) for complete template/mock/Foundry settings, endpoint and deployment formats,
authentication, timeouts, fallback and validation commands. No subscription, billing or secrets are
needed locally. Mock mode runs the same handoff protocol without model calls and is labelled accordingly.

The Foundry workflow uses separate Interpreter, Skeptic and Editor requests. It returns typed selections;
strict application checks retain factual wording and reject unsupported references. Live snapshots use
a bounded background queue and share accepted results with WHY? and Agents. Recaps use complete verified
sections. `/api/meta` distinguishes the configured mode; inspect each narrative's `source` and
`verificationNote` to distinguish accepted Foundry output, mock output and template fallback.

For keyless access, pass `foundryResourceId` to the Bicep deployment: it grants the container app's managed
identity **Cognitive Services OpenAI User** on the Foundry account (`infra/foundry-access.bicep`).

## Azure deployment

These are deployment definitions, not evidence of a completed cloud deployment. Validate provisioning,
image deployment, managed identity permissions and model availability in the target subscription before
claiming the Azure category or supplying a hosted judging URL.

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
