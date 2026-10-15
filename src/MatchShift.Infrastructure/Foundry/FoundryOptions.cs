namespace MatchShift.Infrastructure.Foundry;

/// <summary>
/// Microsoft Foundry (Azure AI Foundry) model deployment settings. Configure via
/// appsettings "Foundry" section or environment variables such as Foundry__Endpoint.
///
/// Endpoint: the resource's OpenAI-compatible base, e.g.
///   https://&lt;resource&gt;.services.ai.azure.com/openai/v1   or
///   https://&lt;resource&gt;.openai.azure.com/openai/v1
/// Authentication: ApiKey when set, otherwise Microsoft Entra ID (DefaultAzureCredential,
/// e.g. a managed identity in Azure Container Apps).
/// </summary>
public sealed class FoundryOptions
{
    public const string Section = "Foundry";

    public bool Enabled { get; set; }
    public string? Endpoint { get; set; }
    public string? Deployment { get; set; }
    public string? ApiKey { get; set; }
    public double Temperature { get; set; } = 0.4;
    public int MaxOutputTokens { get; set; } = 900;
    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Deployment);
}
