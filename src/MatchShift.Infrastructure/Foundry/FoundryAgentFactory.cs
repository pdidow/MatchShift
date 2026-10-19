using System.ClientModel;
using System.ClientModel.Primitives;
using System.Reflection;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using MatchShift.Agents.Workflow;
using Microsoft.Agents.AI;

namespace MatchShift.Infrastructure.Foundry;

/// <summary>
/// Agent workflow settings. The Foundry project endpoint and model deployment default to the standard
/// FOUNDRY_PROJECT_ENDPOINT and AZURE_AI_MODEL_DEPLOYMENT_NAME environment variables. There is deliberately
/// no API key setting: authentication is Microsoft Entra ID only.
/// </summary>
public sealed class AgentWorkflowOptions
{
    public const string Section = "AgentWorkflow";

    /// <summary>Auto (LIVE_FOUNDRY when endpoint and deployment are configured, else OFFLINE_TEMPLATE), LiveFoundry or OfflineTemplate.</summary>
    public string Mode { get; set; } = "Auto";
    public string? ProjectEndpoint { get; set; }
    public string? ModelDeployment { get; set; }

    /// <summary>Default (DefaultAzureCredential), AzureCli (fastest locally after az login) or ManagedIdentity (hosted).</summary>
    public string Credential { get; set; } = "Default";

    /// <summary>Run the workflow automatically for fresh, meaningful insight candidates while a match is playing.</summary>
    public bool AutoRunMeaningful { get; set; } = true;
    public double MeaningfulImportance { get; set; } = 70;
    public int SessionAutoRunBudget { get; set; } = 4;
    public int MaxRunsPerHour { get; set; } = 40;

    /// <summary>Rolling 24-hour cap on live executions (persisted on App Service, see AgentRunQuota).</summary>
    public int MaxRunsPerDay { get; set; } = 60;

    /// <summary>Live executions one client (IP address) may start per rolling hour.</summary>
    public int MaxRunsPerClientPerHour { get; set; } = 4;
    public int MaxConcurrentWorkflows { get; set; } = 2;
    public int AgentTimeoutSeconds { get; set; } = 30;
    public int WorkflowTimeoutSeconds { get; set; } = 120;
    public int MaxAttemptsPerAgent { get; set; } = 2;
    public int MaxModelCallsPerWorkflow { get; set; } = 8;

    public WorkflowLimits Limits => new()
    {
        AgentCallTimeout = TimeSpan.FromSeconds(AgentTimeoutSeconds),
        WorkflowTimeout = TimeSpan.FromSeconds(WorkflowTimeoutSeconds),
        MaxAttemptsPerAgent = Math.Clamp(MaxAttemptsPerAgent, 1, 3),
        MaxModelCalls = Math.Clamp(MaxModelCallsPerWorkflow, 1, 12),
    };
}

/// <summary>
/// LIVE_FOUNDRY: code-first Microsoft Agent Framework agents (<see cref="ChatClientAgent"/>) created with
/// <c>AIProjectClient.AsAIAgent</c> against the Microsoft Foundry project, backed by the project's Responses API
/// and the configured model deployment. No portal-defined agents, no API keys: Entra ID via Azure Identity.
/// </summary>
public sealed class FoundryAgentFactory : IInsightAgentFactory
{
    public const string TokenScope = "https://ai.azure.com/.default";
    private readonly AIProjectClient _project;
    private readonly TokenCredential _credential;

    public FoundryAgentFactory(Uri projectEndpoint, string deployment, TokenCredential credential, string credentialName)
    {
        ModelDeployment = deployment;
        EndpointHost = projectEndpoint.Host;
        CredentialName = credentialName;
        _credential = credential;
        // Retries are owned by AgentInvoker so every retry is bounded and visible in the trace.
        _project = new AIProjectClient(projectEndpoint, credential, new AIProjectClientOptions
        {
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            NetworkTimeout = TimeSpan.FromSeconds(60),
        });
    }

    public ExecutionMode Mode => ExecutionMode.LiveFoundry;
    public string Provider => "microsoft-foundry";
    public string? ModelDeployment { get; }
    public string EndpointHost { get; }
    public string CredentialName { get; }

    public AIAgent? Create(AgentDefinition definition) => _project.AsAIAgent(MatchShiftAgents.Options(definition, ModelDeployment));

    public (AgentFailureKind Kind, TimeSpan? RetryAfter) Classify(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case ClientResultException { Status: 429 } r: return (AgentFailureKind.Throttled, RetryAfter(r));
                case ClientResultException { Status: 401 or 403 }: return (AgentFailureKind.Authentication, null);
                case ClientResultException { Status: 408 }: return (AgentFailureKind.Timeout, null);
                case ClientResultException { Status: >= 500 }: return (AgentFailureKind.ProviderError, null);
                case ClientResultException { Status: 400 or 404 or 422 }: return (AgentFailureKind.BadRequest, null);
                case AuthenticationFailedException or CredentialUnavailableException: return (AgentFailureKind.Authentication, null);
            }
        }
        return AgentFailures.Classify(exception);
    }

    public string? FailureDetail(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
            switch (e)
            {
                case ClientResultException { Status: > 0 } r: return $"HTTP {r.Status}";
                case AuthenticationFailedException or CredentialUnavailableException: return "credential";
                case HttpRequestException { StatusCode: { } code }: return $"HTTP {(int)code}";
            }
        return null;
    }

    /// <summary>Acquires one Entra token at start-up (no model call) so the first workflow does not pay credential discovery time.</summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken) =>
        await _credential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken);

    private static TimeSpan? RetryAfter(ClientResultException r)
    {
        var headers = r.GetRawResponse()?.Headers;
        if (headers is null) return null;
        if (headers.TryGetValue("retry-after-ms", out var ms) && double.TryParse(ms, out var m)) return TimeSpan.FromMilliseconds(m);
        if (headers.TryGetValue("Retry-After", out var s) && double.TryParse(s, out var sec)) return TimeSpan.FromSeconds(sec);
        return null;
    }

    public static TokenCredential CreateCredential(string name) => name.Trim().ToLowerInvariant() switch
    {
        "azurecli" => new AzureCliCredential(),
        "managedidentity" => new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned),
        _ => new DefaultAzureCredential(),
    };
}

/// <summary>What the API reports about the agent runtime. Contains no secrets: only names, host and versions.</summary>
public sealed record AgentRuntimeInfo(
    ExecutionMode ExecutionMode,
    string Provider,
    string? ModelDeployment,
    string? ProjectEndpointHost,
    string? Credential,
    string Orchestrator,
    IReadOnlyDictionary<string, string> Packages,
    string? ConfigurationNote)
{
    public static IReadOnlyDictionary<string, string> PackageVersions() => new SortedDictionary<string, string>
    {
        ["Microsoft.Agents.AI"] = Version(typeof(ChatClientAgent)),
        ["Microsoft.Agents.AI.Workflows"] = Version(typeof(Microsoft.Agents.AI.Workflows.WorkflowBuilder)),
        ["Microsoft.Agents.AI.Foundry"] = Version(Assembly.Load("Microsoft.Agents.AI.Foundry")),
        ["Azure.AI.Projects"] = Version(typeof(AIProjectClient)),
        ["Azure.Identity"] = Version(typeof(DefaultAzureCredential)),
    };

    private static string Version(Type t) => Version(t.Assembly);

    private static string Version(Assembly a) =>
        (a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? a.GetName().Version?.ToString() ?? "?").Split('+')[0];
}
