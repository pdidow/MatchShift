using System.Net;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MatchShift.Agents.Workflow;

/// <summary>One specialised agent. Instructions stay server-side: they are never returned by an API or written to a trace.</summary>
public sealed record AgentDefinition(AgentRole Role, string Name, string Responsibility, string Instructions, int MaxOutputTokens);

/// <summary>
/// The four MatchShift agents. Each has one responsibility that a single combined prompt would blur:
/// interpretation (Tactical Analyst), audience communication (Audience Narrative), nonvisual and plain-language
/// communication (Accessibility Narrator), and independent claim checking (Verification).
/// </summary>
public static class MatchShiftAgents
{
    private const string Shared =
        "You are part of MatchShift, a Microsoft Agent Framework workflow over SYNTHETIC football data. " +
        "insightContext is produced by deterministic code and is authoritative: never invent, recalculate, round differently or contradict " +
        "any number, team, player, score, minute, event or metric. Use values exactly as written in its display fields. " +
        "Never mention anything after insightContext.matchTime. Cite evidence only with IDs listed in insightContext.sourceEvents. " +
        "No claims of intent (what a coach or player decided or wanted) and no certainty words (definitely, proves). " +
        GroundingRules + " " +
        "Write all viewer-facing text in the language code insightContext.language. Return only the requested JSON object.";

    /// <summary>
    /// Shared by all four agents (and therefore by audience, spoken, accessible and replay text). The deterministic validator
    /// enforces the same rules; stating them here prevents the claims instead of removing them afterwards.
    /// </summary>
    public const string GroundingRules =
        "PLAYER COUNTS: never state or imply how many players were involved (no '8 key players', 'three attackers', 'several " +
        "players', 'most of the midfield'); insightContext has no player-count fact, so do not count players, IDs or listed events " +
        "to make one. Name only players listed in insightContext, and describe patterns without a count (e.g. 'right-sided activity " +
        "increased'). MATCH TIMES: use only times that appear in insightContext (matchTime, metric window minutes, sourceEvents " +
        "clock values); never estimate, round or approximate a minute ('around the 67th minute'). If timing matters otherwise, " +
        "say 'during this phase' or 'in the recent sequence'. Never invent numbers, counts, players, times or event IDs.";

    public static AgentDefinition TacticalAnalyst { get; } = new(AgentRole.TacticalAnalyst, "MatchShift Tactical Analyst",
        "Interprets the deterministic evidence tactically: facts used, inference, possible explanation, significance, limits.",
        Shared + " ROLE: TACTICAL_ANALYST. Decide what the evidence might mean tactically. Keep three levels apart: " +
        "factsUsed restates facts only (no causes); inference says what the facts suggest; possibleExplanation is a hedged hypothesis " +
        "(may, might, could) or null when nothing is supported. Qualify the reading with insightContext.confidenceInputs and each metric's " +
        "sample size; if a sample requirement is not met, say so in limitations. Each field under 300 characters.",
        MaxOutputTokens: 500);

    public static AgentDefinition AudienceNarrative { get; } = new(AgentRole.AudienceNarrative, "MatchShift Audience Narrative",
        "Communicates the same grounded insight for the viewer's selected audience mode without changing facts.",
        Shared + " ROLE: AUDIENCE_NARRATIVE. You receive insightContext and the Tactical Analyst's result. Communicate the SAME insight " +
        "for insightContext.selectedAudienceMode. You may change depth, terminology, tone and emphasis, never the facts. Modes: " +
        "TeachMe = newcomer, plain words, explain the football idea briefly; WhatMatters = one short sentence with only the key point; " +
        "Analyst = precise, name the metric, both windows, threshold and sample; MyTeam = frame it for insightContext.focusTeam " +
        "(good news or a warning) when present; Studio = punchy broadcast talking point. Headline at most 90 characters, summary at most 280.",
        MaxOutputTokens: 350);

    public static AgentDefinition AccessibilityNarrator { get; } = new(AgentRole.AccessibilityNarrator, "MatchShift Accessibility Narrator",
        "Transforms the same grounded intelligence for spoken, nonvisual and simplified use (screen readers, audio match mode, replay description).",
        Shared + " ROLE: ACCESSIBILITY_NARRATOR. You receive insightContext and the Tactical Analyst's result. Do not paraphrase a visual " +
        "card: produce output for people who cannot see the pitch or prefer simple language. spokenSummary: natural for text-to-speech, " +
        "short sentences, no symbols or arrows, say 'per cent' instead of '%'. visualDescription: where on the pitch the supporting events " +
        "happened, using only sourceEvents zone fields, or null. simplifiedExplanation: at most two short plain sentences. " +
        "replayAudioDescription: what the evidence replay shows in event order using only listed events, or null. " +
        "Spoken and replay text follow the same PLAYER COUNTS and MATCH TIMES rules: spell no count or time that is not in insightContext.",
        MaxOutputTokens: 450);

    public static AgentDefinition Verifier { get; } = new(AgentRole.Verification, "MatchShift Verification Agent",
        "Independent semantic gate: checks every generated claim against the authoritative context and decides whether it may be published.",
        Shared + " ROLE: VERIFICATION. You are an independent reviewer. You receive insightContext, deterministicFindings from code, and the " +
        "candidate outputs of the Tactical Analyst, Audience Narrative and Accessibility Narrator. Check numbers, evidence IDs, players, score, " +
        "minute, metric values, baseline versus current direction, threshold and sample-size wording, facts versus inference, causal or " +
        "speculative language, unsupported tactical assertions, and consistency between the variants including spoken versus visual text. " +
        "decision: APPROVED if every claim is supported; APPROVED_WITH_CHANGES if small corrections make it supported (then correctedNarrative " +
        "is required and may only remove or soften claims, never add facts); REJECTED otherwise. List evidenceIdsChecked. " +
        "Treat as unsupported, however plausible: any player count (exact, approximate or inferred, in words or digits) and any " +
        "match time or minute not present in insightContext, including rounded or approximate times; correct them by removal.",
        MaxOutputTokens: 600);

    public static IReadOnlyList<AgentDefinition> All { get; } = [TacticalAnalyst, AudienceNarrative, AccessibilityNarrator, Verifier];

    public static AgentDefinition For(AgentRole role) => All.First(d => d.Role == role);

    /// <summary>Agent Framework options for a definition. <paramref name="modelId"/> is the Foundry deployment name.</summary>
    public static ChatClientAgentOptions Options(AgentDefinition d, string? modelId) => new()
    {
        Id = d.Role.ToString(),
        Name = d.Name,
        Description = d.Responsibility,
        ChatOptions = new ChatOptions
        {
            ModelId = modelId,
            Instructions = d.Instructions,
            MaxOutputTokens = d.MaxOutputTokens,
            Temperature = 0,
        },
    };
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<AgentFailureKind>))]
public enum AgentFailureKind { Timeout, Throttled, ProviderError, MalformedOutput, Authentication, BadRequest, Cancelled, BudgetExhausted, Unknown }

/// <summary>Creates the Agent Framework agents for one execution mode. OFFLINE_TEMPLATE returns no agents.</summary>
public interface IInsightAgentFactory
{
    ExecutionMode Mode { get; }

    /// <summary>"microsoft-foundry" or "offline-template" (or a test double's name).</summary>
    string Provider { get; }

    string? ModelDeployment { get; }

    /// <summary>The agent for a definition, or null when this mode runs no model.</summary>
    AIAgent? Create(AgentDefinition definition);

    /// <summary>Maps a provider exception to a failure kind and an optional server-requested retry delay. No message text is kept.</summary>
    (AgentFailureKind Kind, TimeSpan? RetryAfter) Classify(Exception exception) => AgentFailures.Classify(exception);

    /// <summary>A safe diagnostic suffix such as "HTTP 403" (status code only: never a message, header or body).</summary>
    string? FailureDetail(Exception exception) =>
        exception is HttpRequestException { StatusCode: { } code } ? $"HTTP {(int)code}" : null;
}

public static class AgentFailures
{
    public static (AgentFailureKind Kind, TimeSpan? RetryAfter) Classify(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }: return (AgentFailureKind.Throttled, null);
                case HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }: return (AgentFailureKind.Authentication, null);
                case HttpRequestException { StatusCode: HttpStatusCode.BadRequest }: return (AgentFailureKind.BadRequest, null);
                case HttpRequestException: return (AgentFailureKind.ProviderError, null);
                case TimeoutException: return (AgentFailureKind.Timeout, null);
                case JsonException: return (AgentFailureKind.MalformedOutput, null);
                case BudgetExhaustedException: return (AgentFailureKind.BudgetExhausted, null);
            }
        }
        return (AgentFailureKind.Unknown, null);
    }
}

/// <summary>Creates real Agent Framework <see cref="ChatClientAgent"/>s over any <see cref="IChatClient"/>.</summary>
public sealed class ChatClientAgentFactory(IChatClient client, ExecutionMode mode, string provider, string? modelDeployment) : IInsightAgentFactory
{
    public ExecutionMode Mode => mode;
    public string Provider => provider;
    public string? ModelDeployment => modelDeployment;
    public AIAgent? Create(AgentDefinition definition) => new ChatClientAgent(client, MatchShiftAgents.Options(definition, modelDeployment));
}

/// <summary>OFFLINE_TEMPLATE: the workflow still runs on Agent Framework, but no agent (and no model) exists.</summary>
public sealed class OfflineTemplateAgentFactory : IInsightAgentFactory
{
    public ExecutionMode Mode => ExecutionMode.OfflineTemplate;
    public string Provider => "offline-template";
    public string? ModelDeployment => null;
    public AIAgent? Create(AgentDefinition definition) => null;
}

public sealed class BudgetExhaustedException(string message) : Exception(message);
