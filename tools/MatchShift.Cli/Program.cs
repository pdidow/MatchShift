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
    case "calibrate":
    {
        MatchShift.Cli.Calibration.PrintTimeline(SyntheticMatchGenerator.GenerateFlagship().Match);
        foreach (var seed in new long[] { 1, 2 })
            MatchShift.Cli.Calibration.PrintTimeline(SyntheticMatchGenerator.GenerateMatch(seed));
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
    var st = MatchShift.Analytics.State.MatchState.FromEvents(MatchShift.Analytics.State.MatchContext.From(match), match.Events);
    Console.WriteLine($"  entries H={st.Entries.Count(x => x.TeamId == match.HomeTeam.Id)} A={st.Entries.Count(x => x.TeamId == match.AwayTeam.Id)} " +
                      $"box H={st.BoxEntries.Count(x => x.TeamId == match.HomeTeam.Id)} A={st.BoxEntries.Count(x => x.TeamId == match.AwayTeam.Id)} " +
                      $"possessions={st.Possessions.Count} losses={st.Losses.Count}");
    foreach (var e in match.Events.Where(e => e.Type is EventType.Goal or EventType.Substitution))
        Console.WriteLine($"  {e}");
}
