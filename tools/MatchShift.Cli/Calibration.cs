using MatchShift.Analytics.Indices;
using MatchShift.Analytics.Metrics;
using MatchShift.Analytics.State;
using MatchShift.Core.Domain;

namespace MatchShift.Cli;

/// <summary>Prints rolling index values per window so thresholds can be sanity-checked against the script.</summary>
public static class Calibration
{
    public static void PrintTimeline(Match match, int stepMinutes = 5, double windowMinutes = 5)
    {
        var state = MatchState.FromEvents(MatchContext.From(match), match.Events);
        Console.WriteLine($"\n{match.Id}  ({match.HomeTeam.Code} v {match.AwayTeam.Code})  window={windowMinutes}m");
        Console.WriteLine("min  | ctrl | pressH pressA | chaos state | entH L/C/R    entA L/C/R    | shH shA | toH toA | posChg trans");
        for (var minute = stepMinutes; minute <= 90; minute += stepMinutes)
        {
            var now = minute * 60;
            var w = MetricsCalculator.Compute(state, AnalysisWindow.Last(now, windowMinutes));
            var control = MatchControl.Compute(w);
            var ph = PressureIndex.Compute(w.Home);
            var pa = PressureIndex.Compute(w.Away);
            var chaos = ChaosIndex.Compute(w);
            Console.WriteLine(
                $"{minute,3}  | {control.HomeControl,4:0} | {ph.Value,6:0} {pa.Value,6:0} | {chaos.Value,5:0} {chaos.State,-10} | " +
                $"{w.Home.LeftAttacks,2}/{w.Home.CentreAttacks,2}/{w.Home.RightAttacks,2} p{w.Home.Attacks,2} {w.Away.LeftAttacks,2}/{w.Away.CentreAttacks,2}/{w.Away.RightAttacks,2} p{w.Away.Attacks,2}      | " +
                $"{w.Home.Shots,3} {w.Away.Shots,3} | {w.Home.Turnovers,3} {w.Away.Turnovers,3} | " +
                $"{w.TotalPossessions / windowMinutes,5:0.0} {w.TotalFastTransitions,4}  " +
                string.Join(" ", chaos.Components.Select(c => $"{c.Key[..4]}={c.RawValue:0.00}")));
        }
    }

    public static void PrintChannels(Match match)
    {
        var state = MatchState.FromEvents(MatchContext.From(match), match.Events);
        foreach (var team in new[] { match.HomeTeam.Id, match.AwayTeam.Id })
        {
            Console.WriteLine($"-- {team}: minute  base L/C/R (poss)   recent L/C/R (poss)   zRight zLeft");
            for (var minute = 20; minute <= 90; minute += 2)
            {
                var now = minute * 60;
                var b = MetricsCalculator.Compute(state, AnalysisWindow.MatchSoFar(now - 12 * 60)).For(team);
                var r = MetricsCalculator.Compute(state, AnalysisWindow.Last(now, 12)).For(team);
                var zr = MatchShift.Analytics.Detection.Statistics.TwoProportionZ(b.RightShare, b.Attacks, r.RightShare, r.Attacks);
                var zl = MatchShift.Analytics.Detection.Statistics.TwoProportionZ(b.LeftShare, b.Attacks, r.LeftShare, r.Attacks);
                Console.WriteLine($"   {minute,3}   {b.LeftShare:0.00}/{b.CentreShare:0.00}/{b.RightShare:0.00} ({b.Attacks,2},{b.AttackingThirdActions,3})   " +
                                  $"{r.LeftShare:0.00}/{r.CentreShare:0.00}/{r.RightShare:0.00} ({r.Attacks,2},{r.AttackingThirdActions,3})   {zr,5:0.0} {zl,5:0.0}");
            }
        }
    }
}
