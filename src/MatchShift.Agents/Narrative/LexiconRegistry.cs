using MatchShift.Agents.Narrative.Lexicons;
using MatchShift.Core.Domain;

namespace MatchShift.Agents.Narrative;

public static class LexiconRegistry
{
    private static readonly Dictionary<string, Lexicon> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new EnglishLexicon(),
        ["fr"] = new FrenchLexicon(),
        ["es"] = new SpanishLexicon(),
    };

    public static IReadOnlyCollection<Lexicon> Supported => All.Values;

    public static bool IsSupported(string? code) => code is not null && All.ContainsKey(code);

    /// <summary>Returns the requested lexicon, falling back to English.</summary>
    public static Lexicon Get(string? code) => code is not null && All.TryGetValue(code, out var l) ? l : All["en"];
}

/// <summary>Names for teams and players. Narratives resolve IDs here; IDs themselves are never translated.</summary>
public sealed class MatchRoster
{
    private readonly Match _match;

    public MatchRoster(Match match) => _match = match;

    public Match Match => _match;
    public string HomeTeamId => _match.HomeTeam.Id;
    public string AwayTeamId => _match.AwayTeam.Id;

    public string Team(string? teamId) => teamId is null ? "" : _match.TeamById(teamId).ShortName;
    public string Opponent(string? teamId) => teamId is null ? "" : _match.Opponent(teamId).ShortName;
    public string Player(string? playerId) => _match.FindPlayer(playerId)?.Name ?? "";
    public bool IsKnownTeam(string? id) => id == HomeTeamId || id == AwayTeamId;
}
