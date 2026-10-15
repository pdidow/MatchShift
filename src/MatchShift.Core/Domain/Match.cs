namespace MatchShift.Core.Domain;

/// <summary>
/// A complete synthetic match: metadata, both squads and the ordered event stream.
/// Ground-truth labels for scripted scenarios are deliberately NOT part of this
/// type - see <c>MatchShift.SyntheticData.GroundTruth</c>.
/// </summary>
public sealed record Match
{
    public required string Id { get; init; }
    public required string Competition { get; init; }
    public required string Venue { get; init; }
    public required DateOnly Date { get; init; }
    public required Team HomeTeam { get; init; }
    public required Team AwayTeam { get; init; }
    public required IReadOnlyList<MatchEvent> Events { get; init; }
    public required long Seed { get; init; }
    public required string ScenarioName { get; init; }

    public bool IsSynthetic => true;

    public Team TeamById(string teamId) =>
        teamId == HomeTeam.Id ? HomeTeam :
        teamId == AwayTeam.Id ? AwayTeam :
        throw new ArgumentException($"Unknown team '{teamId}'", nameof(teamId));

    public Team Opponent(string teamId) => teamId == HomeTeam.Id ? AwayTeam : HomeTeam;

    public bool IsHome(string teamId) => teamId == HomeTeam.Id;

    public Player? FindPlayer(string? playerId) =>
        playerId is null ? null :
        HomeTeam.Squad.FirstOrDefault(p => p.Id == playerId) ??
        AwayTeam.Squad.FirstOrDefault(p => p.Id == playerId);

    public Score ScoreAt(int matchSecond)
    {
        var home = 0;
        var away = 0;
        foreach (var e in Events)
        {
            if (e.MatchSecond > matchSecond) break;
            if (e.Type != EventType.Goal) continue;
            if (e.TeamId == HomeTeam.Id) home++; else away++;
        }
        return new Score(home, away);
    }
}

public readonly record struct Score(int Home, int Away)
{
    public override string ToString() => $"{Home}-{Away}";
}

/// <summary>Lightweight match descriptor for listings.</summary>
public sealed record MatchSummary(
    string Id,
    string ScenarioName,
    long Seed,
    string HomeTeam,
    string AwayTeam,
    Score FinalScore,
    int EventCount);
