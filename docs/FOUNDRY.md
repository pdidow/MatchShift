# Microsoft Foundry and credential-free development

Microsoft Foundry is MatchShift's production AI provider. Running or developing the app does **not**
require an Azure subscription, payment information, credentials, or network access to a model.
Initial dependency installation requires the usual package downloads.

## Agent Framework workflow (primary)

The four-agent Microsoft Agent Framework workflow ([AGENTS.md](AGENTS.md)) reads the standard variables and needs no key:

```powershell
$env:FOUNDRY_PROJECT_ENDPOINT = 'https://<resource>.services.ai.azure.com/api/projects/<project>'
$env:AZURE_AI_MODEL_DEPLOYMENT_NAME = 'gpt-4.1-mini'
az login
dotnet run --project src/MatchShift.Api --launch-profile http -- --AgentWorkflow:Credential=AzureCli
```

`AgentWorkflow:Credential` is `Default` (DefaultAzureCredential), `AzureCli` (fastest locally) or `ManagedIdentity`.
The identity needs data-plane access to the Foundry project (for example the **Azure AI User** role). Without the two
variables, or with `AgentWorkflow:Mode=OfflineTemplate`, the same workflow runs with deterministic templates and no
model calls. `/api/agents/status` reports which mode is active.

The provider settings below configure the older, optional editorial-selection path only.

## Choose a provider

| `Narration__Provider` | Behavior | Output source |
|---|---|---|
| `template` | Immediate deterministic English/French/Spanish narratives | `template` |
| `mock` | Offline simulation of the production interpreter/skeptic/editor protocol, with simulated delays | `mock` |
| `foundry` | Calls the configured Microsoft Foundry deployment; verified templates cover missing configuration, failures and timeouts | `microsoft-foundry` on acceptance; `template` on fallback |

No provider setting defaults to templates, unless `Foundry__Enabled=true` selects the legacy Foundry
configuration path. Environment variables override appsettings. A `.env` file is **not** auto-loaded.
Provider/workflow configuration is read at startup; restart the API after changing it.

From the repository root, after building the frontend as described in README:

```powershell
# Offline mock demonstration; no secrets and no Azure contact.
dotnet run --project src/MatchShift.Api --launch-profile http -- --Narration:Provider=mock

# Fully deterministic alternative.
dotnet run --project src/MatchShift.Api --launch-profile http -- --Narration:Provider=template
```

The mock simulates the JSON handoffs and validation, not model intelligence. Cards and overlays label
it explicitly. `/api/meta` reports the selected/requested provider, status and prompt version.
`aiAvailable=false` in mock mode. In Foundry mode, `aiAvailable=true` indicates configuration only;
a successful response must have `source=microsoft-foundry` and an approved verification note.

## Enable Foundry when credentials are available

Use an existing authorized resource and deployment. No provisioning or billing step is part of local setup.
The deployment must support Chat Completions, JSON-object responses, `temperature`, and `max_tokens`.
The configured `Deployment` is the deployment name, not necessarily the underlying model name.
Reasoning models with incompatible parameter requirements need a corresponding client adapter.

```powershell
$env:Narration__Provider = 'foundry'
$env:Foundry__Enabled = 'true'
$env:Foundry__Endpoint = 'https://YOUR-RESOURCE.services.ai.azure.com/openai/v1'
$env:Foundry__Deployment = 'YOUR-DEPLOYMENT'
# Leave Foundry__ApiKey absent for DefaultAzureCredential / managed identity.
# Alternatively provide Foundry__ApiKey through your private environment or secret store.
dotnet run --project src/MatchShift.Api --launch-profile http
```

Accepted endpoint forms end in `/openai/v1`, on either `services.ai.azure.com` or `openai.azure.com`.
Use the inference resource base, not a Foundry project URL or a complete `/chat/completions` URL.
The client appends `/chat/completions`; it sends the deployment name as `model`. The v1 API does not
require a dated `api-version`. Entra authentication uses `DefaultAzureCredential` and the default
`Foundry__TokenScope=https://ai.azure.com/.default`; `Foundry__ApiKey`, when present, takes precedence.
The identity needs the relevant resource access, such as Cognitive Services OpenAI User.
See Microsoft's [v1 API and authentication documentation](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle).

Optional settings: `Foundry__TimeoutSeconds=20`, `Foundry__MaxOutputTokens=4096`,
`Foundry__Temperature=0.4`, `Narration__Workflow=collaborative` (default) or `single` for comparison.
The total insight workflow has a deadline, including its bounded revision. A timeout, malformed response,
rejected selection, authentication error or outage falls back to verified templates. Exception types,
not provider response bodies or keys, appear in fallback notes.

## What the model controls

Deterministic C# owns factual sentences, numbers, teams/players, windows, evidence IDs and translations.
The model chooses an audience treatment (`analysis`, `context`, `brief`) bound to those facts; it cannot
publish arbitrary factual prose. Brief treatment uses the ticker headline and omits the optional
explanation; context uses the studio headline. Recap AI orders complete verified sections without
altering or dropping their content. This is constrained AI editorial selection, not unrestricted
natural-language generation or autonomous tactical discovery.

The interpreter proposes a typed selection. The skeptic compares it with authoritative evidence.
A rejection triggers one repair and a second review. The editor chooses the final treatment; an
application-controlled gate still validates it. JSON mode is not a factual guarantee. Schema-constrained
output could further constrain syntax on supported models, but this implementation uses JSON mode plus
strict application validation. See Microsoft's [structured-output guidance](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs).

## Repeatable validation

```powershell
dotnet run --project tools/MatchShift.Cli -- verify-ai --provider mock --out artifacts/mock-validation.json
dotnet run --project tools/MatchShift.Cli -- compare-agents --provider mock --matches 8 --seed 81000 --out artifacts/agent-comparison.json
```

These commands never contact Azure in mock mode. The saved reports in `docs/validation` are mock results.
When access exists, explicitly choosing `--provider foundry` runs the same checks against the configured
service. That is optional and has not been run for this upgrade. No live availability, token cost,
model quality or production scalability is inferred from the offline tests.
