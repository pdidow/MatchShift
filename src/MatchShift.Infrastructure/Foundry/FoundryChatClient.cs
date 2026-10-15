using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace MatchShift.Infrastructure.Foundry;

public interface IChatCompletionClient
{
    Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}

/// <summary>
/// Minimal client for a Microsoft Foundry model deployment via the OpenAI-compatible
/// chat-completions API. Requests JSON output so responses can be parsed and verified.
/// </summary>
public sealed class FoundryChatClient(HttpClient http, IOptions<FoundryOptions> options, TokenCredential? credential = null) : IChatCompletionClient
{
    private static readonly string[] Scopes = ["https://cognitiveservices.azure.com/.default"];
    private readonly FoundryOptions _options = options.Value;

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured) throw new InvalidOperationException("Microsoft Foundry is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.Endpoint!.TrimEnd('/')}/chat/completions");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Add("api-key", _options.ApiKey);
        }
        else if (credential is not null)
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        request.Content = JsonContent.Create(new ChatRequest(
            _options.Deployment!,
            [new ChatMessage("system", systemPrompt), new ChatMessage("user", userPrompt)],
            _options.Temperature,
            _options.MaxOutputTokens,
            new ResponseFormat("json_object")));

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken)
                   ?? throw new InvalidOperationException("Empty response from Foundry.");
        return body.Choices.FirstOrDefault()?.Message.Content
               ?? throw new InvalidOperationException("Foundry returned no choices.");
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("response_format")] ResponseFormat ResponseFormat);

    private sealed record ResponseFormat([property: JsonPropertyName("type")] string Type);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatResponse([property: JsonPropertyName("choices")] IReadOnlyList<Choice> Choices);

    private sealed record Choice([property: JsonPropertyName("message")] ChatMessage Message);

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
