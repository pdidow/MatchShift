using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Azure.Core;
using Azure.Identity;
using MatchShift.Api.Playback;

namespace MatchShift.Api.Speech;

public sealed record SpeechRequest(string Text, string Locale);

/// <summary>Server-only, bounded Speech REST adapter. Never returns credentials or accepts SSML/URLs from clients.</summary>
public sealed class SpeechFallback : IDisposable
{
    public static readonly IReadOnlyDictionary<string, string> Voices = new Dictionary<string, string>
    {
        ["en-GB"] = "en-GB-SoniaNeural", ["fr-FR"] = "fr-FR-DeniseNeural",
        ["es-ES"] = "es-ES-ElviraNeural", ["de-DE"] = "de-DE-KatjaNeural",
        ["it-IT"] = "it-IT-ElsaNeural", ["pt-PT"] = "pt-PT-RaquelNeural"
    };
    private readonly IConfiguration config;
    private readonly IHttpClientFactory clients;
    private readonly TokenCredential credential;
    private readonly ILogger<SpeechFallback> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, byte[]> cache = new();
    private readonly AgentRunQuota quota;

    public SpeechFallback(IConfiguration config, IHttpClientFactory clients, ILogger<SpeechFallback> logger)
    {
        this.config = config; this.clients = clients; this.logger = logger;
        credential = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is not null
            ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned) : new AzureCliCredential();
        var agentPath = AgentRunQuota.DefaultStatePath(config);
        quota = new AgentRunQuota(120, 500, 60, statePath: agentPath is null ? null : Path.Combine(Path.GetDirectoryName(agentPath)!, "speech-quota.json"), logger: logger);
    }

    public bool Enabled => config.GetValue<bool>("Speech:Enabled") &&
        !string.IsNullOrWhiteSpace(config["Speech:ResourceId"]) &&
        System.Text.RegularExpressions.Regex.IsMatch(config["Speech:Region"] ?? "", "^[a-z0-9]{3,30}$");

    public static bool Valid(SpeechRequest request) => !string.IsNullOrWhiteSpace(request.Text) &&
        request.Text.Length <= 800 && request.Locale is not null && Voices.ContainsKey(request.Locale) &&
        !request.Text.Any(c => char.IsControl(c) && c != '\n' && c != '\t');

    public static string Ssml(SpeechRequest request)
    {
        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        return new XElement(ns + "speak", new XAttribute("version", "1.0"), new XAttribute(XNamespace.Xml + "lang", request.Locale),
            new XElement(ns + "voice", new XAttribute("name", Voices[request.Locale]), request.Text)).ToString(SaveOptions.DisableFormatting);
    }

    public async Task<IResult> Synthesize(SpeechRequest request, HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Valid(request)) return Results.BadRequest(new { message = "Invalid speech text or locale." });
        if (!Enabled) return Results.StatusCode(503);
        // No unbounded queue. Serializing also coalesces identical requests against the bounded cache.
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(2), context.RequestAborted)) return Results.StatusCode(429);
        try
        {
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Locale + "\n" + request.Text)));
            if (cache.TryGetValue(key, out var hit)) return Results.Bytes(hit, "audio/mpeg");
            if (quota.TryReserve(ApiSecurity.ClientKey(context)) != QuotaRefusal.None) return Results.StatusCode(429);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var token = await credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), timeout.Token);
            using var message = new HttpRequestMessage(HttpMethod.Post, $"https://{config["Speech:Region"]}.tts.speech.microsoft.com/cognitiveservices/v1");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"aad#{config["Speech:ResourceId"]}#{token.Token}");
            message.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-48kbitrate-mono-mp3");
            message.Headers.UserAgent.ParseAdd("MatchShift/1.0");
            message.Content = new StringContent(Ssml(request), Encoding.UTF8, "application/ssml+xml");
            using var response = await clients.CreateClient("speech").SendAsync(message, timeout.Token);
            response.EnsureSuccessStatusCode();
            var audio = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (audio.Length == 0 || audio.Length > 1_000_000) throw new InvalidDataException("Invalid speech response size");
            if (cache.Count >= 64) cache.Remove(cache.Keys.First());
            cache[key] = audio;
            return Results.Bytes(audio, "audio/mpeg");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or Azure.Identity.AuthenticationFailedException or InvalidDataException or Azure.RequestFailedException)
        {
            logger.LogWarning("Speech fallback failed ({FailureType}); no narration text or credentials logged", ex.GetType().Name);
            return Results.StatusCode(503);
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}
