using MatchShift.Analytics;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;

namespace MatchShift.Api.Playback;

/// <summary>
/// The live demo: one synthetic match replayed as if it were arriving in real time.
///
/// Speeds: 1x = 30 match seconds per real second (a full match in 3 minutes); 2x, 5x, 10x scale that.
/// Events are fed to a <see cref="LiveMatchAnalyzer"/> as the clock passes them. Seeking backwards
/// rebuilds the analyzer from kick-off, so analytics are always exactly what a live viewer would have seen.
/// </summary>
public sealed class DemoSession
{
    public const double MatchSecondsPerRealSecondAt1x = 30;
    public static readonly int[] AllowedSpeeds = [1, 2, 5, 10];

    private readonly object _gate = new();
    private LiveMatchAnalyzer _analyzer;
    private int _nextEventIndex;
    private double _clock;

    private readonly NarrationQueue? _narration;
    private readonly Dictionary<(string Id, ViewerProfile Profile), Task<AgentTrace>> _narratives = [];
    private CancellationTokenSource _epochCancellation = new();
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public long Epoch { get; private set; }
    public DateTime LastAccess { get; private set; } = DateTime.UtcNow;
    public void Touch() => LastAccess = DateTime.UtcNow;

    public DemoSession(MatchCatalog catalog, NarrationQueue? narration = null)
    {
        _narration = narration;
        Match = catalog.Default;
        _analyzer = new LiveMatchAnalyzer(Analytics.State.MatchContext.From(Match));
    }

    public Match Match { get; private set; }
    public bool IsPlaying { get; private set; }
    public int Speed { get; private set; } = 2;
    public long Version { get; private set; }

    public int ClockSecond { get { lock (_gate) return (int)_clock; } }
    public bool IsFinished => ClockSecond >= MatchClock.FullTimeSecond;

    /// <summary>Run <paramref name="read"/> against a consistent view of the session.</summary>
    public T Read<T>(Func<LiveMatchAnalyzer, int, T> read)
    {
        lock (_gate) return read(_analyzer, (int)_clock);
    }

    public Task<AgentTrace>? Trace(Insight i, NarrativeContext context)
    {
        lock (_gate)
        {
            if (_narratives.TryGetValue((i.Id, context.Profile), out var cached)) return cached;
            if (_narratives.Count >= 256) return null;
            var epoch = Epoch;
            var task = _narration?.Enqueue(i, Match, context, _epochCancellation.Token,
                () => { lock (_gate) { if (Epoch == epoch) Version++; } });
            if (task is not null) _narratives[(i.Id, context.Profile)] = task;
            return task;
        }
    }

    public InsightNarrative Narrative(Insight i, NarrativeContext context)
    {
        var task = Trace(i, context);
        if (task?.IsCompletedSuccessfully == true) return task.Result.Narrative;
        return TemplateInsightProvider.Narrate(i, context) with
        { VerificationNote = task is null ? "Narration capacity reached; verified template." :
            task.IsFaulted ? "Narration withheld; verified template." : "Verified template; editorial review pending." };
    }

    private void NewEpoch()
    {
        Epoch++;
        _epochCancellation.Cancel();
        _epochCancellation.Dispose();
        _epochCancellation = new();
        _narratives.Clear();
    }

    // Owned by SessionRegistry, never by the per-request DI scope.
    public void Close() { lock (_gate) { IsPlaying = false; _epochCancellation.Cancel(); _epochCancellation.Dispose(); } }

    public void Load(Match match)
    {
        lock (_gate)
        {
            Match = match;
            ResetLocked();
        }
    }

    public void Reset()
    {
        lock (_gate) ResetLocked();
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_clock >= MatchClock.FullTimeSecond) ResetLocked();
            IsPlaying = true;
            Version++;
        }
    }

    public void Pause()
    {
        lock (_gate) { IsPlaying = false; Version++; }
    }

    public void SetSpeed(int speed)
    {
        if (!AllowedSpeeds.Contains(speed)) throw new ArgumentOutOfRangeException(nameof(speed), $"Speed must be one of {string.Join(", ", AllowedSpeeds)}.");
        lock (_gate) { Speed = speed; Version++; }
    }

    /// <summary>Jump to a match second. Backwards seeks replay from kick-off; forward seeks process events in order.</summary>
    public void Seek(int matchSecond)
    {
        matchSecond = Math.Clamp(matchSecond, 0, MatchClock.FullTimeSecond);
        lock (_gate)
        {
            NewEpoch();
            if (matchSecond < _clock)
            {
                _analyzer = new LiveMatchAnalyzer(Analytics.State.MatchContext.From(Match));
                _nextEventIndex = 0;
            }
            AdvanceLocked(matchSecond);
            _clock = matchSecond;
            Version++;
        }
    }

    /// <summary>Called by the playback loop with real elapsed time.</summary>
    public void Tick(TimeSpan elapsed)
    {
        lock (_gate)
        {
            if (!IsPlaying) return;
            var target = Math.Min(MatchClock.FullTimeSecond, _clock + elapsed.TotalSeconds * MatchSecondsPerRealSecondAt1x * Speed);
            AdvanceLocked((int)target);
            _clock = target;
            if (_clock >= MatchClock.FullTimeSecond) IsPlaying = false;
            Version++;
        }
    }

    private void ResetLocked()
    {
        NewEpoch();
        _analyzer = new LiveMatchAnalyzer(Analytics.State.MatchContext.From(Match));
        _nextEventIndex = 0;
        _clock = 0;
        IsPlaying = false;
        Version++;
    }

    private void AdvanceLocked(int toSecond)
    {
        var before = _analyzer.Insights.Count;
        AdvanceEvents(toSecond);
        var raised = _analyzer.Insights.Count - before;
        if (raised > 0) MatchShiftMetrics.InsightsRaised.Add(raised);
    }

    private void AdvanceEvents(int toSecond)
    {
        var events = Match.Events;
        while (_nextEventIndex < events.Count && events[_nextEventIndex].MatchSecond <= toSecond)
        {
            _analyzer.Ingest(events[_nextEventIndex]);
            _nextEventIndex++;
        }
        _analyzer.AdvanceTo(toSecond);
    }
}

/// <summary>Drives the demo clock at ~8 ticks per second.</summary>
public sealed class PlaybackService(SessionRegistry sessions, ILogger<PlaybackService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(125));
        var last = TimeProvider.System.GetTimestamp();
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = TimeProvider.System.GetTimestamp();
            try
            {
                foreach (var session in sessions.All) session.Tick(TimeProvider.System.GetElapsedTime(last, now));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Playback tick failed");
                foreach (var session in sessions.All) session.Pause();
            }
            last = now;
        }
    }
}
