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
                $"{w.Home.LeftAttacks,2}/{w.Home.CentreAttacks,2}/{w.Home.RightAttacks,2} p{w.Home.AttackingPossessions,2} {w.Away.LeftAttacks,2}/{w.Away.CentreAttacks,2}/{w.Away.RightAttacks,2} p{w.Away.AttackingPossessions,2}      | " +
                $"{w.Home.Shots,3} {w.Away.Shots,3} | {w.Home.Turnovers,3} {w.Away.Turnovers,3} | " +
                $"{w.TotalPossessions / windowMinutes,5:0.0} {w.TotalFastTransitions,4}  " +
                string.Join(" ", chaos.Components.Select(c => $"{c.Key[..4]}={c.RawValue:0.00}")));
        }
    }
}
