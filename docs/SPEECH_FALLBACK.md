# Narration fallback

MatchShift uses browser speech first. French local voices are selected in order: `fr-CA`, `fr-FR`, then another French voice. Other languages use the selected locale, then a voice of the same language. An English voice never reads French as a fallback.

The shared speech queue waits briefly for asynchronous browser voices. If no compatible voice appears, the engine reports a failure, or speech does not start within 2.5 seconds, it requests audio from `POST /api/speech`. Failed local voices are avoided for the rest of the page session. The selected application locale accompanies the original localized text; digits are synthesized in that language.

The ASP.NET backend uses Azure Speech REST with Microsoft Entra authentication. Azure App Service uses its system-assigned managed identity; local development uses the signed-in Azure CLI identity. The browser receives MP3 audio only. Credentials and bearer tokens are never sent to the browser. No Foundry agent workflow is involved.

## Server configuration

App Service settings:

- `Speech__Enabled=true`
- `Speech__Region=eastus`
- `Speech__ResourceId`: resource ID of the existing Azure AI Services account.

The App Service identity requires **Cognitive Services Speech User**, scoped to that account. The existing `matchshift-foundry-pd` account supports the configured voices. This is an existing S0 resource: fallback synthesis is metered, not promised free. No new resource is needed. Unconfigured environments return HTTP 503; local browser speech continues to work.

Server-selected voices: en-GB Sonia, fr-FR Denise, es-ES Elvira, de-DE Katja, it-IT Elsa, pt-PT Raquel (all Neural). Clients cannot choose an arbitrary voice, endpoint or SSML document. Text is XML-escaped.

## Bounds and privacy

- 800 characters per synthesis; existing 4 KiB API body limit.
- Existing API admission limits plus 20 requests per client per minute.
- One synthesis at a time; waiting admission expires after two seconds.
- At most 120 uncached requests/hour, 500/rolling day and 60/client/hour. Maximum text admission is consequently 400,000 characters/day per process.
- A 64-entry server audio cache and 16-entry browser audio cache avoid repeat synthesis. Caches are memory-only; response caching is disabled.
- Rolling global quota timestamps use the existing private App Service data directory, in a separate speech quota file. Per-client counters are in memory. As with the existing agent limiter, this is a **single-instance admission control**, not a distributed billing guarantee. Coordinate quotas before scaling to multiple workers. Quota persistence failures are logged; in-memory limits remain active.
- Requests time out after 12 seconds. Diagnostics record failure types, not narration text, tokens or upstream response bodies.
- The fallback sends narration text to Azure Speech only when browser speech is unavailable or fails.

## Playback and failure behavior

An ordinary pointer or keyboard interaction unlocks Web Audio before synthesis finishes. Cloud speech shares the existing queue, priority, transcript, rate, volume, pause and cancellation controls. Language changes, seeks and replay-focus changes cancel pending requests/playback. Responses are checked for timeline relevance before playback; stale descriptions are discarded, not spoken late. A failure in both paths produces a visible narration error, with text retained in the transcript.

Browser APIs cannot determine whether a user has muted a tab, the operating-system mixer or physical speakers. Engine errors and missing start events are detected; physical output silence is not measurable by the application.

References: [Speech REST authentication](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech), [Microsoft Entra setup and roles](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/how-to-configure-azure-ad-auth).
