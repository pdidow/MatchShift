using MatchShift.Analytics;
using MatchShift.Core.Domain;

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

    public DemoSession(MatchCatalog catalog)
    {
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
        _analyzer = new LiveMatchAnalyzer(Analytics.State.MatchContext.From(Match));
        _nextEventIndex = 0;
        _clock = 0;
        IsPlaying = false;
        Version++;
    }

    private void AdvanceLocked(int toSecond)
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
public sealed class PlaybackService(DemoSession session, ILogger<PlaybackService> logger) : BackgroundService
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
                session.Tick(TimeProvider.System.GetElapsedTime(last, now));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Playback tick failed");
                session.Pause();
            }
            last = now;
        }
    }
}
