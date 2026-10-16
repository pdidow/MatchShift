using Azure.Identity;
using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
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
}
