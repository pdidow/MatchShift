using System.Text.Json;

namespace MatchShift.Infrastructure.Foundry;

/// <summary>Offline simulator of the exact production handoff protocol. Never represents a real model call.</summary>
public sealed class MockChatCompletionClient : IChatCompletionClient
{
    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        await Task.Delay(20, cancellationToken);
        using var input = JsonDocument.Parse(userPrompt);
        var root = input.RootElement;
        if (systemPrompt.Contains("Recap Editor", StringComparison.Ordinal))
            return JsonSerializer.Serialize(new { sectionOrder = Enumerable.Range(0, root.GetProperty("sections").GetArrayLength()) });
        if (systemPrompt.Contains("Skeptic", StringComparison.Ordinal))
        {
            var claim = root.GetProperty("claim");
            var proposal = root.GetProperty("proposal").GetProperty("selection");
            // The application gate also checks the full binding. Mock review does not prove model quality.
            var approved = claim.GetProperty("teamId").ToString() == proposal.GetProperty("teamId").ToString();
            return JsonSerializer.Serialize(new { approved, issues = approved ? Array.Empty<string>() : ["wrong_team"] });
        }
        var selection = JsonSerializer.Deserialize<Dictionary<string, object?>>(root.GetProperty("claim").GetRawText())!;
        selection["treatment"] = "brief";
        return JsonSerializer.Serialize(new { selection });
    }
}
