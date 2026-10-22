# Hosting MatchShift on Azure App Service (Free F1)

MatchShift deploys as one ASP.NET Core 10 process that serves the API and the production React build from
`wwwroot`. The Microsoft Agent Framework workflow runs in that process and calls the Microsoft Foundry project
with the App Service's **system-assigned managed identity**: no API keys, secrets or user credentials.

```
Browser ──HTTPS──▶ Azure App Service (Free F1, Windows, .NET 10)
                     ├── React build (wwwroot, SPA fallback)
                     └── ASP.NET Core API ──▶ Microsoft Agent Framework workflow (4 agents)
                                                 │ system-assigned managed identity (Entra ID token, audience https://ai.azure.com)
                                                 ▼
                                Microsoft Foundry project ──▶ gpt-4.1-mini deployment
```

| Concern | Local development | Azure hosted |
|---|---|---|
| Authentication to Foundry | Azure CLI (`az login`), `AgentWorkflow:Credential=AzureCli` (or `Default`) | System-assigned managed identity, `AgentWorkflow__Credential=ManagedIdentity` |
| AI orchestration | Microsoft Agent Framework | Microsoft Agent Framework |
| AI platform / model | Microsoft Foundry project, `gpt-4.1-mini` | same |
| Frontend | Vite dev server (`:5173`, proxies `/api` to `:5002`) | Built into `wwwroot`, same origin as the API |

## Execution modes

| Mode | Meaning |
|---|---|
| `LIVE_FOUNDRY` | Agent Framework agents called the Foundry model. `/api/agents/status` reports the *configured* mode; only an execution trace whose agent stages show `MICROSOFT_AGENT_FRAMEWORK`, model calls and token counts proves a live call. Failed calls show `FALLBACK`/`FAILED` stages, an HTTP status in `attemptFailures`, and outcome `FALLBACK_UNVERIFIED`, never "verified". |
| `OFFLINE_TEMPLATE` | Same workflow graph, deterministic templates, no model calls (`AgentWorkflow__Mode=OfflineTemplate`). |
| `RECORDED_DEMO` | Static Pages build or hosted `?demo=judge` replaying recorded real traces; no live workflow or fallback to Foundry. |

## Required Azure role

The managed identity needs **Foundry User** (role ID `53ca6127-db72-4b80-b1b0-d745d6d5456d`) on the Foundry
**project** (or account). Code-first agents created with `AIProjectClient.AsAIAgent` call the model through the
project's Responses API, which is a project data action. **Foundry Agent Consumer / Foundry Project Runtime User**
are *not* sufficient: they only cover published agent endpoints (verified: HTTP 403 from Azure with only that role).
Do not use `Cognitive Services *` roles for Foundry projects (Microsoft guidance).

## App settings (non-secret)

| Setting | Value |
|---|---|
| `FOUNDRY_PROJECT_ENDPOINT` | `https://<resource>.services.ai.azure.com/api/projects/<project>` |
| `AZURE_AI_MODEL_DEPLOYMENT_NAME` | `gpt-4.1-mini` |
| `AgentWorkflow__Mode` | `LiveFoundry` |
| `AgentWorkflow__Credential` | `ManagedIdentity` |
| `AgentWorkflow__MaxRunsPerHour` | `10` (public site: caps model spend) |
| `AgentWorkflow__MaxRunsPerDay` | `30` (rolling 24 h) |
| `AgentWorkflow__MaxRunsPerClientPerHour` | `4` |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` (HTTPS terminates at the App Service front end) |

## Deploy

```powershell
# Plan and app (Free F1; this subscription had F1 quota only in some regions)
az appservice plan create -g <rg> -n <plan> -l centralus --sku F1 --is-linux false
az webapp create -g <rg> -p <plan> -n <app> --runtime "dotnet:10" --assign-identity "[system]" --https-only true
az role assignment create --assignee-object-id <app-principal-id> --assignee-principal-type ServicePrincipal `
  --role 53ca6127-db72-4b80-b1b0-d745d6d5456d --scope <foundry-project-resource-id>
az webapp config appsettings set -g <rg> -n <app> --settings FOUNDRY_PROJECT_ENDPOINT=... AZURE_AI_MODEL_DEPLOYMENT_NAME=gpt-4.1-mini `
  AgentWorkflow__Mode=LiveFoundry AgentWorkflow__Credential=ManagedIdentity AgentWorkflow__MaxRunsPerHour=10 AgentWorkflow__MaxRunsPerDay=30 AgentWorkflow__MaxRunsPerClientPerHour=4 ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

# Build: React into src/MatchShift.Api/wwwroot, then publish and zip-deploy
cd src/MatchShift.Web; npm ci; npm run build; cd ../..
dotnet publish src/MatchShift.Api/MatchShift.Api.csproj -c Release -o publish
Compress-Archive publish/* matchshift.zip
az webapp deploy -g <rg> -n <app> --src-path matchshift.zip --type zip --restart true
```

## Verified hosted run

A workflow triggered through the public HTTPS endpoint of the F1 deployment executed all four agents through
Microsoft Agent Framework against the Foundry project with the managed identity: `LIVE_FOUNDRY`, provider
`microsoft-foundry`, model `gpt-4.1-mini`, 4 model calls, Audience and Accessibility branches in the same superstep
overlapping ~3.1 s, deterministic validation removed one unsupported tactical fact, Verification Agent `APPROVED`,
final validation re-checked, authoritative-facts fingerprint unchanged. Before the Foundry User role existed the same
request ended `FALLBACK_UNVERIFIED` with `Authentication (HTTP 403)` and nothing was labelled verified.

Notes from this subscription (Free Trial): F1 quota was 0 in East US for both Linux and Windows, and available in
Central US (Windows). The web app therefore runs in Central US and calls the East US Foundry project.

## Hosted judge demonstration

Open [the judge path](https://matchshift-pd.azurewebsites.net/?demo=judge) and follow the eight-step guide. Select flagship insight `MS-FLAGSHIP-001-I015`, then **How was this verified? → Agents**. The page shows **RECORDED DEMO** and the actual **APPROVED WITH CHANGES** result of a previously recorded real four-agent Foundry workflow, including historical timings and validation. Viewing it makes no live workflow or model call. An insight without a recording shows “No recorded workflow is available for this insight.” and does not fall back to live Foundry. Normal hosted mode outside `?demo=judge` remains live-capable; `/api/agents/status` reports that server configuration, not the mode of the recorded judge client. See [JUDGING.md](JUDGING.md) for the full path.

Live workflow executions on the public site are capped at 10 per hour, 30 per rolling day and 4 per client per hour. Beyond that, requests get an `OFFLINE_TEMPLATE` execution with "Live AI analysis is temporarily unavailable; deterministic match evidence remains available." and no Foundry call (see [AGENTS.md](AGENTS.md#cost-controls)). `/api/agents/status` shows `liveAgents` and the run counts.

## Free tier limits

F1: 60 CPU minutes per day, 1 GB memory, 32-bit worker, no Always On (the app sleeps after idle and cold-starts),
single instance. Demo sessions and the agent-run cache are in memory and reset on restart; the hourly and daily run caps are persisted in `%HOME%\data` and survive restarts (verified on the F1 host); the per-client cap is in memory.
The web host is free; **Foundry model inference is billed separately** per token (about 4 calls and ~10k tokens per
agent workflow execution with gpt-4.1-mini).

The repository's `azure.yaml` / `infra/*.bicep` (Azure Container Apps + ACR) describe an alternative, paid
deployment and are not used for the Free F1 host.
