using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Azure.AI.Projects;
using Azure.Core;
using MatchShift.Agents.Workflow;
using MatchShift.Infrastructure.Foundry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MatchShift.Evaluation;

/// <summary>The structured JSON each agent returned in one workflow execution (last answer wins, e.g. after a repair).</summary>
public sealed class CaptureSink
{
    public ConcurrentDictionary<AgentRole, string> Outputs { get; } = new();
}

/// <summary>Records the model's final text per agent role. Sees only responses, never credentials.</summary>
public sealed class CapturingChatClient(IChatClient inner, CaptureSink sink) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? [.. messages];
        var response = await base.GetResponseAsync(list, options, cancellationToken);
        var instructions = (options?.Instructions ?? "") + string.Join("\n", list.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
        var role = MatchShiftAgents.All.FirstOrDefault(d => instructions.Contains(Tag(d.Role), StringComparison.Ordinal))?.Role;
        if (role is { } r) sink.Outputs[r] = response.Text;
        return response;
    }

    private static string Tag(AgentRole role) => role switch
    {
        AgentRole.TacticalAnalyst => "ROLE: TACTICAL_ANALYST",
        AgentRole.AudienceNarrative => "ROLE: AUDIENCE_NARRATIVE",
        AgentRole.AccessibilityNarrator => "ROLE: ACCESSIBILITY_NARRATOR",
        _ => "ROLE: VERIFICATION",
    };
}

/// <summary>
/// LIVE_FOUNDRY agents identical to production (same definitions, same AsAIAgent call, same project and deployment),
/// with a capturing chat client so the evaluation can see what each agent actually produced.
/// </summary>
public sealed class CapturingFoundryAgentFactory : IInsightAgentFactory
{
    private readonly AIProjectClient _project;
    private readonly FoundryAgentFactory _classifier;
    private readonly CaptureSink _sink;

    public CapturingFoundryAgentFactory(Uri projectEndpoint, string deployment, TokenCredential credential, CaptureSink sink)
    {
        ModelDeployment = deployment;
        _sink = sink;
        _classifier = new FoundryAgentFactory(projectEndpoint, deployment, credential, "evaluation");
        _project = new AIProjectClient(projectEndpoint, credential, new AIProjectClientOptions
        {
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            NetworkTimeout = TimeSpan.FromSeconds(60),
        });
    }

    public ExecutionMode Mode => ExecutionMode.LiveFoundry;
    public string Provider => "microsoft-foundry";
    public string? ModelDeployment { get; }

    public AIAgent? Create(AgentDefinition definition) =>
        _project.AsAIAgent(MatchShiftAgents.Options(definition, ModelDeployment), clientFactory: inner => new CapturingChatClient(inner, _sink));

    public (AgentFailureKind Kind, TimeSpan? RetryAfter) Classify(Exception exception) => _classifier.Classify(exception);

    public string? FailureDetail(Exception exception) => _classifier.FailureDetail(exception);
}
