using MatchShift.Core.Domain;

namespace MatchShift.Analytics.Metrics;

/// <summary>A half-open span of match time [FromSecond, ToSecond).</summary>
public readonly record struct AnalysisWindow(int FromSecond, int ToSecond)
{
    public int DurationSeconds => Math.Max(0, ToSecond - FromSecond);
    public double Minutes => DurationSeconds / 60.0;
    public bool IsEmpty => DurationSeconds <= 0;

    public bool Contains(int second) => second >= FromSecond && second < ToSecond;

    /// <summary>Broadcast-style label, e.g. "40'-55'".</summary>
    public string Label => $"{FromSecond / 60}'-{(ToSecond + 59) / 60}'";

    public int FromMinute => FromSecond / 60;
    public int ToMinute => (ToSecond + 59) / 60;

    public static AnalysisWindow OfMinutes(double fromMinute, double toMinute) =>
        new((int)Math.Round(fromMinute * 60), (int)Math.Round(toMinute * 60));

    /// <summary>The last <paramref name="minutes"/> minutes up to <paramref name="nowSecond"/>.</summary>
    public static AnalysisWindow Last(int nowSecond, double minutes) =>
        new(Math.Max(0, nowSecond - (int)(minutes * 60)), nowSecond);

    /// <summary>
    /// The <paramref name="baselineMinutes"/> immediately before the last
    /// <paramref name="recentMinutes"/>, e.g. "previous 10" in "last 5 vs previous 10".
    /// </summary>
    public static AnalysisWindow Before(int nowSecond, double recentMinutes, double baselineMinutes)
    {
        var to = Math.Max(0, nowSecond - (int)(recentMinutes * 60));
        return new AnalysisWindow(Math.Max(0, to - (int)(baselineMinutes * 60)), to);
    }

    public static AnalysisWindow MatchSoFar(int nowSecond) => new(0, nowSecond);

    public static AnalysisWindow FirstHalf => new(0, MatchClock.SecondHalfStartSecond);
    public static AnalysisWindow SecondHalf => new(MatchClock.SecondHalfStartSecond, MatchClock.FullTimeSecond);

    public override string ToString() => Label;
}

/// <summary>A recent-vs-baseline pair with a stable name used in evidence.</summary>
public sealed record WindowComparison(string Name, AnalysisWindow Baseline, AnalysisWindow Recent)
{
    public static WindowComparison Last5VsPrevious10(int now) =>
        new("last 5 vs previous 10", AnalysisWindow.Before(now, 5, 10), AnalysisWindow.Last(now, 5));

    public static WindowComparison Last10VsPrevious15(int now) =>
        new("last 10 vs previous 15", AnalysisWindow.Before(now, 10, 15), AnalysisWindow.Last(now, 10));

    public static WindowComparison Last8VsPrevious15(int now) =>
        new("last 8 vs previous 15", AnalysisWindow.Before(now, 8, 15), AnalysisWindow.Last(now, 8));

    public static WindowComparison RecentVsMatch(int now, double recentMinutes) =>
        new($"last {recentMinutes:0} vs match so far", AnalysisWindow.MatchSoFar(now - (int)(recentMinutes * 60)),
            AnalysisWindow.Last(now, recentMinutes));

    public static WindowComparison AroundSubstitution(int substitutionSecond, int now, double beforeMinutes = 15) =>
        new("before vs after substitution",
            new AnalysisWindow(Math.Max(0, substitutionSecond - (int)(beforeMinutes * 60)), substitutionSecond),
            new AnalysisWindow(substitutionSecond, now));
}
