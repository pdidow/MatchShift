using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using MatchShift.Agents.Workflow;
using Microsoft.Extensions.AI;

namespace MatchShift.Agents.Tests;

/// <summary>
/// A scripted model behind real Microsoft Agent Framework <c>ChatClientAgent</c>s. It identifies which agent is calling
/// from the agent's instructions, records every call (prompt, start, end) and returns scripted JSON, delays or failures.
/// Offline only: it never represents a live Foundry call.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    public sealed record Call(AgentRole Role, string Prompt, DateTimeOffset Start, DateTimeOffset End);

    public delegate Task<string> Responder(AgentRole role, JsonNode prompt, int attempt, CancellationToken ct);

    private readonly ConcurrentDictionary<AgentRole, int> _attempts = new();
    public ConcurrentQueue<Call> Calls { get; } = new();
    public Responder Respond { get; set; } = (role, prompt, _, _) => Task.FromResult(Good(role, prompt));

    public IEnumerable<Call> For(AgentRole role) => Calls.Where(c => c.Role == role);

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var instructions = (options?.Instructions ?? "") + string.Join("\n", list.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
        var role = MatchShiftAgents.All.First(d => instructions.Contains($"ROLE: {RoleTag(d.Role)}", StringComparison.Ordinal)).Role;
        var prompt = list.Last(m => m.Role == ChatRole.User).Text;
        var attempt = _attempts.AddOrUpdate(role, 0, (_, n) => n + 1);
        var start = DateTimeOffset.UtcNow;
        try
        {
            var text = await Respond(role, JsonNode.Parse(prompt)!, attempt, cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 } };
        }
        finally
        {
            Calls.Enqueue(new Call(role, prompt, start, DateTimeOffset.UtcNow));
        }
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    public static string RoleTag(AgentRole role) => role switch
    {
        AgentRole.TacticalAnalyst => "TACTICAL_ANALYST",
        AgentRole.AudienceNarrative => "AUDIENCE_NARRATIVE",
        AgentRole.AccessibilityNarrator => "ACCESSIBILITY_NARRATOR",
        _ => "VERIFICATION",
    };

    /// <summary>A well-behaved answer built only from the prompt's own context, as a grounded model would.</summary>
    public static string Good(AgentRole role, JsonNode prompt)
    {
        var c = prompt["insightContext"]!;
        var w = c["deterministicNarrative"]!;
        var ids = c["sourceEvents"]!.AsArray().Select(e => e!["id"]!.GetValue<string>()).Take(2).ToArray();
        object result = role switch
        {
            AgentRole.TacticalAnalyst => new TacticalAnalystResult([w["fact"]!.GetValue<string>()], "The evidence suggests a change in how the team is attacking.",
                "This might reflect a tactical adjustment.", "It could shape the next phase of play.", "Confidence follows the deterministic sample checks.",
                ids, ["Synthetic data."]),
            AgentRole.AudienceNarrative => new AudienceNarrativeResult(c["selectedAudienceMode"]!.GetValue<string>(), w["headline"]!.GetValue<string>(),
                w["fact"]!.GetValue<string>(), null, "Worth watching."),
            AgentRole.AccessibilityNarrator => new AccessibilityNarrativeResult("A change was detected. " + w["headline"]!.GetValue<string>(),
                "The supporting events are spread across the pitch.", "One team is changing how it attacks.", null),
            _ => new VerificationAgentResult("APPROVED", ["All claims are supported."], [], [], ids, null, []),
        };
        return JsonSerializer.Serialize(result, result.GetType(), AgentJson.Options);
    }
}
