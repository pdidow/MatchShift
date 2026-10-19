using Azure.Identity;
using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Infrastructure.Foundry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MatchShift.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the narrative stack. Without Foundry configuration (the default for local runs)
    /// the offline <see cref="TemplateInsightProvider"/> is used, so no keys or cloud services are needed.
    /// </summary>
    public static IServiceCollection AddMatchShiftNarration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FoundryOptions>(configuration.GetSection(FoundryOptions.Section));
        var foundry = configuration.GetSection(FoundryOptions.Section).Get<FoundryOptions>() ?? new FoundryOptions();
        var mode = configuration["Narration:Provider"]?.ToLowerInvariant() ?? (foundry.Enabled ? "foundry" : "template");
        var collaborative = configuration["Narration:Workflow"] != "single";
        if (mode is not ("template" or "mock" or "foundry"))
            throw new InvalidOperationException("Narration:Provider must be template, mock or foundry.");

        services.AddSingleton<VerificationAgent>();
        services.AddSingleton<TemplateInsightProvider>();

        if (mode == "mock")
        {
            services.AddSingleton<IChatCompletionClient, MockChatCompletionClient>();
            services.AddSingleton<IAiInsightProvider>(sp => new FoundryInsightProvider(sp.GetRequiredService<IChatCompletionClient>(), "mock", collaborative));
        }
        else if (mode == "foundry" && foundry.IsConfigured)
        {
            services.AddHttpClient<IChatCompletionClient, FoundryChatClient>((http, sp) =>
                    new FoundryChatClient(http, sp.GetRequiredService<IOptions<FoundryOptions>>(),
                        string.IsNullOrWhiteSpace(foundry.ApiKey) ? new DefaultAzureCredential() : null))
                .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(foundry.TimeoutSeconds + 5));
            services.AddSingleton<IAiInsightProvider>(sp => new FoundryInsightProvider(sp.GetRequiredService<IChatCompletionClient>(), collaborative: collaborative));
        }
        else
        {
            services.AddSingleton<IAiInsightProvider>(sp => sp.GetRequiredService<TemplateInsightProvider>());
        }

        services.AddSingleton(sp => new NarrationService(
            sp.GetRequiredService<IAiInsightProvider>(),
            sp.GetRequiredService<VerificationAgent>(),
            TimeSpan.FromSeconds(foundry.TimeoutSeconds)));
        services.AddSingleton<AgentPipeline>();
        return services;
    }

    /// <summary>
    /// Registers the Microsoft Agent Framework insight workflow. LIVE_FOUNDRY when a Foundry project endpoint and model
    /// deployment are configured (FOUNDRY_PROJECT_ENDPOINT / AZURE_AI_MODEL_DEPLOYMENT_NAME, or AgentWorkflow:*), otherwise
    /// OFFLINE_TEMPLATE. Mode is decided once at start-up and reported honestly by <see cref="AgentRuntimeInfo"/>.
    /// </summary>
    public static IServiceCollection AddMatchShiftAgentWorkflow(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AgentWorkflowOptions.Section).Get<AgentWorkflowOptions>() ?? new AgentWorkflowOptions();
        options.ProjectEndpoint ??= configuration["FOUNDRY_PROJECT_ENDPOINT"];
        options.ModelDeployment ??= configuration["AZURE_AI_MODEL_DEPLOYMENT_NAME"];
        var mode = options.Mode.Trim().ToLowerInvariant();
        if (mode is not ("auto" or "livefoundry" or "offlinetemplate"))
            throw new InvalidOperationException("AgentWorkflow:Mode must be Auto, LiveFoundry or OfflineTemplate.");

        var configured = Uri.TryCreate(options.ProjectEndpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttps
                         && !string.IsNullOrWhiteSpace(options.ModelDeployment);
        string? note = null;
        IInsightAgentFactory factory;
        if (mode != "offlinetemplate" && configured)
        {
            factory = new FoundryAgentFactory(endpoint!, options.ModelDeployment!.Trim(), FoundryAgentFactory.CreateCredential(options.Credential), options.Credential);
        }
        else
        {
            if (mode == "livefoundry") note = "LiveFoundry requested but FOUNDRY_PROJECT_ENDPOINT (https) or AZURE_AI_MODEL_DEPLOYMENT_NAME is missing; running OFFLINE_TEMPLATE.";
            else if (mode == "auto") note = "Foundry project endpoint or model deployment not configured; running OFFLINE_TEMPLATE.";
            factory = new OfflineTemplateAgentFactory();
        }

        var foundry = factory as FoundryAgentFactory;
        services.AddSingleton(options);
        services.AddSingleton(factory);
        services.AddSingleton(new AgentRuntimeInfo(factory.Mode, factory.Provider, factory.ModelDeployment, foundry?.EndpointHost, foundry?.CredentialName,
            Agents.Workflow.WorkflowTraceRecorder.OrchestratorName, AgentRuntimeInfo.PackageVersions(), note));
        services.AddSingleton(new Agents.Workflow.InsightWorkflowRunner(factory, options.Limits));
        return services;
    }
}
