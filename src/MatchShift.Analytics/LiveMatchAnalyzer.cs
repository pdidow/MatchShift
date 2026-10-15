using MatchShift.Analytics.Detection;
using MatchShift.Analytics.Indices;
using MatchShift.Analytics.Metrics;
using MatchShift.Analytics.State;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics;

/// <summary>Per-minute momentum sample for charts and recaps.</summary>
public sealed record TimelinePoint(
    int Minute,
    double HomeControl,
    double HomePressure,
    double AwayPressure,
    double Chaos,
    ChaosState ChaosState,
    int HomeGoals,
    int AwayGoals);

/// <summary>
/// Processes events as a live feed. Events are ingested one at a time; at every
/// minute boundary the analyzer evaluates the shift detectors using ONLY the
/// events seen so far, and records a timeline sample. Moments (goals, big chances,
/// dangerous turnovers) are evaluated as each event arrives.
///
/// Seeking backwards is handled by building a fresh analyzer and replaying - the
/// result is identical to having watched live, because nothing looks ahead.
/// </summary>
public sealed class LiveMatchAnalyzer
{
    public const int TimelineControlWindowMinutes = 10;
    public const int TimelinePressureWindowMinutes = 5;

    private readonly MatchShiftDetector _detector;
    private readonly List<TimelinePoint> _timeline = [];
    private int _nextTickMinute = 1;

    public LiveMatchAnalyzer(MatchContext context, DetectionOptions? options = null, IReadOnlyList<IShiftDetector>? detectors = null)
    {
        State = new MatchState(context);
        _detector = new MatchShiftDetector(options, detectors);
    }

    public MatchState State { get; }
    public IReadOnlyList<Insight> Insights => _detector.Insights;
    public IReadOnlyList<TimelinePoint> Timeline => _timeline;
    public int ClockSecond => State.ClockSecond;

    /// <summary>Ingest the next event. Returns any insights raised as a result (including minute ticks crossed).</summary>
    public IReadOnlyList<Insight> Ingest(MatchEvent e)
    {
        var raised = new List<Insight>(AdvanceTo(e.MatchSecond));
        State.Ingest(e);
        raised.AddRange(_detector.OnEvent(State, e));
        return raised;
    }

    /// <summary>Move the analysis clock forward (e.g. through a quiet spell), running any minute ticks crossed.</summary>
    public IReadOnlyList<Insight> AdvanceTo(int matchSecond)
    {
        var raised = new List<Insight>();
        while (_nextTickMinute <= 90 && _nextTickMinute * 60 <= matchSecond)
        {
            var now = _nextTickMinute * 60;
            State.AdvanceClock(now);
            raised.AddRange(_detector.EvaluateTick(State, now));
            _timeline.Add(Sample(now));
            _nextTickMinute++;
        }
        State.AdvanceClock(matchSecond);
        return raised;
    }

    private TimelinePoint Sample(int now)
    {
        var control = MatchControl.Compute(MetricsCalculator.Compute(State, AnalysisWindow.Last(now, TimelineControlWindowMinutes)));
        var pressureWindow = MetricsCalculator.Compute(State, AnalysisWindow.Last(now, TimelinePressureWindowMinutes));
        var chaos = ChaosIndex.Compute(MetricsCalculator.Compute(State, AnalysisWindow.Last(now, 10)));
        return new TimelinePoint(
            now / 60,
            control.HomeControl,
            PressureIndex.Compute(pressureWindow.Home).Value,
            PressureIndex.Compute(pressureWindow.Away).Value,
            chaos.Value,
            chaos.State,
            State.GoalsBefore(State.Context.HomeTeamId, now + 1),
            State.GoalsBefore(State.Context.AwayTeamId, now + 1));
    }

    /// <summary>Convenience: run a whole match through the live pipeline.</summary>
    public static LiveMatchAnalyzer AnalyzeFull(Match match, DetectionOptions? options = null, IReadOnlyList<IShiftDetector>? detectors = null) =>
        AnalyzeUntil(match, MatchClock.FullTimeSecond, options, detectors);

    /// <summary>Replay a match up to (and including) <paramref name="matchSecond"/>.</summary>
    public static LiveMatchAnalyzer AnalyzeUntil(Match match, int matchSecond, DetectionOptions? options = null,
        IReadOnlyList<IShiftDetector>? detectors = null)
    {
        var analyzer = new LiveMatchAnalyzer(MatchContext.From(match), options, detectors);
        foreach (var e in match.Events)
        {
            if (e.MatchSecond > matchSecond) break;
            analyzer.Ingest(e);
        }
        analyzer.AdvanceTo(matchSecond);
        return analyzer;
    }
}
