using MatchShift.Core.Domain;
using MatchShift.SyntheticData;

var command = args.Length > 0 ? args[0] : "inspect";

switch (command)
{
    case "inspect":
    {
        var match = SyntheticMatchGenerator.GenerateFlagship().Match;
        Inspect(match);
        foreach (var seed in new long[] { 1, 2, 3, 42, 12345 })
            Inspect(SyntheticMatchGenerator.GenerateMatch(seed));
        break;
    }
    default:
        Console.WriteLine("Usage: matchshift inspect");
        break;
}

static void Inspect(Match match)
{
    Console.WriteLine($"{match.HomeTeam.Name} {match.ScoreAt(int.MaxValue)} {match.AwayTeam.Name}  events={match.Events.Count}");
    foreach (var team in new[] { match.HomeTeam, match.AwayTeam })
    {
        var ev = match.Events.Where(e => e.TeamId == team.Id).ToList();
        Console.WriteLine($"{team.Code}: passes={ev.Count(e => e.Type == EventType.Pass)} " +
                          $"complete={ev.Count(e => e.Type == EventType.Pass && e.Outcome == EventOutcome.Complete)} " +
                          $"shots={ev.Count(e => e.Type == EventType.Shot)} corners={ev.Count(e => e.Type == EventType.Corner)} " +
                          $"fouls={ev.Count(e => e.Type == EventType.Foul)} tackles={ev.Count(e => e.Type == EventType.Tackle)}");
    }
    foreach (var e in match.Events.Where(e => e.Type is EventType.Goal or EventType.Substitution))
        Console.WriteLine($"  {e}");
}
