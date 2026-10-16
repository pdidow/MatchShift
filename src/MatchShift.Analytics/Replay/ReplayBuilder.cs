using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Replay;

/// <summary>
/// Turns an insight's evidence into a 3D replay reconstruction. Deterministic and renderer-independent.
///
/// 1. <b>Sequence selection</b> - the insight's supporting/visual event IDs are grouped into possessions; the
///    most telling possession (shots, goals, box entries, regains, evidence count) becomes the clip, padded with
///    the preceding regain so turnovers are visible. Tactical shifts get a BEFORE clip from the baseline window
///    and an AFTER clip from the recent window.
/// 2. <b>Event timing</b> - events keep their match seconds; events sharing a second are spread across it in
///    sequence order; passes and carries end when the next action starts.
/// 3. <b>Ball</b> - pass/carry/shot segments run between recorded event positions; between actions the ball
///    stays with its holder. Ball height is not in the data: arcs on long balls and crosses are visual only.
/// 4. <b>Players</b> - every on-pitch player gets a track. Positions recorded in events are
///    <see cref="PositionProvenance.ActualEventPosition"/>; between nearby recorded positions they are
///    <see cref="PositionProvenance.InterpolatedPosition"/>; otherwise they are
///    <see cref="PositionProvenance.EstimatedPosition"/> - the player's anchor (their own observed events blended
///    with a role default, see <see cref="RoleAnchors"/>) shifted with the ball. Nothing depends on generator-only data.
/// </summary>
public static class ReplayBuilder
{
    public const double SampleStep = 0.5;
    public const int PreRollSeconds = 4;
    public const int PostRollSeconds = 3;
    public const int MaxClipSeconds = 40;
    public const int MinClipSeconds = 10;

    /// <summary>Recorded positions further apart than this are not joined by straight-line interpolation.</summary>
    public const double MaxInterpolationGap = 7;

    /// <summary>Within this many seconds of a recorded position, estimates blend towards it.</summary>
    public const double BlendSeconds = 3;

    public const string Notice =
        "3D reconstruction of synthetic events. Positions at events come from the event data; positions between " +
        "events are interpolated; players not involved in nearby events are estimated from their formation role. " +
        "Ball height is not recorded - arcs are visual only.";

    private static readonly InsightType[] BeforeAfterTypes =
    [
        InsightType.AttackChannelShift, InsightType.PostSubstitutionChange, InsightType.ControlShift,
        InsightType.TerritorialShift, InsightType.PossessionShift, InsightType.TempoShift, InsightType.PassingStyleShift,
    ];

    private static readonly EventType[] Skipped = [EventType.PeriodStart, EventType.PeriodEnd, EventType.Substitution, EventType.Goal];

    public static ReplaySequence ForInsight(Match match, Insight insight)
    {
        var evidence = insight.SupportingEventIds.Concat(insight.Visuals.SelectMany(v => v.EventIds)).ToHashSet();
        var byId = match.Events.ToDictionary(e => e.Id);
        var evidenceEvents = evidence.Where(byId.ContainsKey).Select(id => byId[id]).OrderBy(e => e.SequenceNumber).ToList();
        var attacking = AttackingTeam(match, insight);

        var clips = new List<ReplayClip>();
        if (BeforeAfterTypes.Contains(insight.Type) && insight.TeamId is not null)
        {
            var recent = insight.PrimaryEvidence?.RecentWindow ?? insight.Span;
            var after = BestSequence(match, evidenceEvents.Where(e => e.MatchSecond >= recent.FromSecond && e.MatchSecond < recent.ToSecond).ToList(),
                attacking, preferChannel: null, insight.DetectedAtSecond);
            var baseline = insight.PrimaryEvidence?.BaselineWindow;
            var before = baseline is null ? null : BeforeSequence(match, insight, attacking, baseline, after);
            if (before is not null) clips.Add(BuildClip(match, insight, "before", "BEFORE SHIFT", before.Value, evidence, attacking));
            if (after is not null) clips.Add(BuildClip(match, insight, "after", before is null ? "KEY SEQUENCE" : "AFTER SHIFT", after.Value, evidence, attacking));
        }

        if (clips.Count == 0)
        {
            var window = BestSequence(match, evidenceEvents, attacking, null, insight.DetectedAtSecond) ?? FallbackWindow(insight, evidenceEvents);
            clips.Add(BuildClip(match, insight, "key", "KEY SEQUENCE", window, evidence, attacking));
        }

        var relevant = new List<string>();
        if (insight.KeyPlayerId is not null) relevant.Add(insight.KeyPlayerId);
        relevant.AddRange(insight.RelatedPlayerIds);
        relevant.AddRange(evidenceEvents.Select(e => e.PlayerId).OfType<string>());

        var players = match.HomeTeam.Squad.Concat(match.AwayTeam.Squad)
            .Select(p => new ReplayPlayer(p.Id, p.TeamId, match.IsHome(p.TeamId), p.ShirtNumber, p.Name, p.ShortName, p.Position.ToString(),
                p.Position == Position.Goalkeeper))
            .ToList();

        return new ReplaySequence(match.Id, insight.Id, insight.Type.Code(), insight.TeamId, match.HomeTeam.Id, match.AwayTeam.Id,
            insight.Span, evidenceEvents.Select(e => e.Id).ToList(), relevant.Distinct().ToList(), players, clips, Notice);
    }

    /// <summary>The team whose attack the replay follows: the punishing team for a dangerous turnover, otherwise the insight's team.</summary>
    public static string AttackingTeam(Match match, Insight insight) =>
        insight.Type == InsightType.DangerousTurnover && insight.TeamId is not null ? match.Opponent(insight.TeamId).Id
        : insight.Type == InsightType.PressureDecrease && insight.TeamId is not null ? match.Opponent(insight.TeamId).Id
        : insight.TeamId ?? match.HomeTeam.Id;

    // ------------------------------------------------------------------ sequence selection

    /// <summary>Pick the most telling possession among the given events and return its padded time window.</summary>
    public static (int From, int To)? BestSequence(Match match, IReadOnlyList<MatchEvent> candidates, string team, string? preferChannel, int beforeSecond)
    {
        var possessionIds = candidates
            .Where(e => e.PossessionId > 0 && e.MatchSecond <= beforeSecond)
            .Select(e => e.PossessionId)
            .Distinct()
            .ToList();
        if (possessionIds.Count == 0) return null;

        var candidateIds = candidates.Select(e => e.Id).ToHashSet();
        (int Id, double Score)? best = null;
        foreach (var pid in possessionIds)
        {
            var events = match.Events.Where(e => e.PossessionId == pid).ToList();
            if (events.Count == 0) continue;
            var owner = events[0].PossessionTeamId;
            double score = events.Count(e => candidateIds.Contains(e.Id));
            if (owner == team) score += 2;
            score += events.Count(e => e.Type == EventType.Shot) * 4;
            score += events.Count(e => e.Type == EventType.Shot && e.Outcome == EventOutcome.Goal) * 6;
            score += events.Count(e => e.Type == EventType.Pass && e.Outcome == EventOutcome.Complete && e.EndX >= Pitch.PenaltyAreaStartX) * 2;
            score += Math.Min(4, events.Count(e => e.TeamId == owner && e.Type is EventType.Pass or EventType.Carry)) * 0.5;
            if (preferChannel is not null && AttackChannelOf(events, owner) == preferChannel) score += 3;
            if (best is null || score > best.Value.Score || (score == best.Value.Score && pid > best.Value.Id)) best = (pid, score);
        }
        return best is null ? null : PossessionWindow(match, best.Value.Id);
    }

    /// <summary>Possession window, extended back to show the regain (the opponent's last actions) and padded.</summary>
    public static (int From, int To) PossessionWindow(Match match, int possessionId)
    {
        var events = match.Events.Where(e => e.PossessionId == possessionId).ToList();
        var first = events[0];
        var start = first.MatchSecond;
        var index = IndexOf(match.Events, first);
        // If the previous possession belonged to the other team, include its last couple of actions (the turnover).
        for (var i = index - 1; i >= 0 && i >= index - 3; i--)
        {
            var e = match.Events[i];
            if (e.Type is EventType.PeriodStart or EventType.Goal) break;
            if (first.MatchSecond - e.MatchSecond > 8) break;
            start = Math.Min(start, e.MatchSecond);
        }
        var end = events[^1].MatchSecond + PostRollSeconds + (events.Any(e => e.Type == EventType.Shot) ? 1 : 0);
        start -= PreRollSeconds;
        if (end - start > MaxClipSeconds) start = end - MaxClipSeconds;
        if (end - start < MinClipSeconds) start = end - MinClipSeconds;
        var periodStart = first.Period == 1 ? 0 : MatchClock.SecondHalfStartSecond;
        return (Math.Max(periodStart, start), end);
    }

    /// <summary>For a tactical shift: a representative attack by the same team from the baseline window.</summary>
    private static (int From, int To)? BeforeSequence(Match match, Insight insight, string team, TimeSpanRef baseline, (int From, int To)? after)
    {
        var shiftedChannel = insight.Fact("channel");
        var from = Math.Max(baseline.FromSecond, baseline.ToSecond - 20 * 60);
        var possessions = match.Events
            .Where(e => e.MatchSecond >= from && e.MatchSecond < baseline.ToSecond && e.PossessionTeamId == team && e.PossessionId > 0)
            .GroupBy(e => e.PossessionId)
            .Select(g => g.ToList())
            .Where(evts => evts.Any(e => e.TeamId == team && e.Type is EventType.Pass or EventType.Carry && e.Outcome == EventOutcome.Complete && e.EndX >= 60))
            .ToList();
        if (possessions.Count == 0) return null;

        // "Before" shows what attacks typically looked like: the most common channel other than the one the
        // team later shifted to. Prefer continuous possessions (no ball jumps) with more build-up, latest first.
        var typical = possessions
            .Select(p => AttackChannelOf(p, team))
            .Where(c => c is not null && c != shiftedChannel)
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        var chosen = possessions
            .OrderByDescending(p => typical is not null && AttackChannelOf(p, team) == typical ? 1 : 0)
            .ThenByDescending(p => IsContinuous(p, team) ? 1 : 0)
            .ThenByDescending(p => Math.Min(8, p.Count(e => e.TeamId == team && e.Type is EventType.Pass or EventType.Carry)))
            .ThenByDescending(p => p.Count(e => e.Type == EventType.Shot))
            .ThenByDescending(p => p[0].MatchSecond)
            .First();
        var window = PossessionWindow(match, chosen[0].PossessionId);
        return after is not null && window.From < after.Value.To && after.Value.From < window.To ? null : window;
    }

    /// <summary>True when each on-ball action starts close to where the previous one ended (no repositioning jumps).</summary>
    public static bool IsContinuous(IReadOnlyList<MatchEvent> possession, string team)
    {
        var actions = possession.Where(e => e.TeamId == team && e.Type is EventType.Pass or EventType.Carry or EventType.Shot && e.HasLocation).ToList();
        for (var i = 1; i < actions.Count; i++)
        {
            var prev = actions[i - 1];
            if (prev.EndX is null || prev.EndY is null) continue;
            if (Metres(prev.EndX.Value, prev.EndY.Value, actions[i].StartX!.Value, actions[i].StartY!.Value) > 12) return false;
        }
        return true;
    }

    /// <summary>Majority channel ("Left"/"Centre"/"Right", team perspective) of a team's completed actions beyond X = 55.</summary>
    public static string? AttackChannelOf(IEnumerable<MatchEvent> events, string? team)
    {
        var channels = events
            .Where(e => e.TeamId == team && e.Type is EventType.Pass or EventType.Carry && e.Outcome == EventOutcome.Complete &&
                        e.EndX >= Pitch.AttackZoneStartX && e.EndY is not null && e.Qualifier != "KickOff")
            .Select(e => Pitch.ChannelOf(e.EndY!.Value))
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .ToList();
        return channels.Count == 0 ? null : channels[0].Key.ToString();
    }

    private static (int From, int To) FallbackWindow(Insight insight, IReadOnlyList<MatchEvent> evidence)
    {
        if (evidence.Count == 0) return (Math.Max(0, insight.DetectedAtSecond - 25), insight.DetectedAtSecond);
        var to = evidence[^1].MatchSecond + PostRollSeconds;
        var from = Math.Max(evidence[0].MatchSecond - PreRollSeconds, to - MaxClipSeconds);
        return (from, Math.Max(to, from + MinClipSeconds));
    }

    // ------------------------------------------------------------------ clip construction

    public static ReplayClip BuildClip(Match match, Insight insight, string key, string label, (int From, int To) window,
        IReadOnlySet<string> evidence, string attackingTeam)
    {
        var (from, to) = window;
        var duration = (double)(to - from);

        // Events with context around the clip so tracks enter and leave smoothly.
        var context = match.Events
            .Where(e => e.MatchSecond >= from - 10 && e.MatchSecond <= to + 10 && !Skipped.Contains(e.Type))
            .OrderBy(e => e.SequenceNumber)
            .ToList();
        var timed = TimeEvents(match, context, from);
        var events = timed.Where(t => t.Event.MatchSecond >= from && t.Event.MatchSecond <= to)
            .Select(t => ToReplayEvent(match, t, evidence))
            .ToList();

        var ball = BuildBall(match, timed, duration);
        var lineup = LineupAt(match, from);
        var keyframes = Keyframes(match, timed);
        var anchors = RoleAnchors(match, lineup, to);
        var tracks = lineup.Select(p => Track(match, p, keyframes.GetValueOrDefault(p.Id) ?? [], ball, duration, anchors[p.Id])).ToList();
        var overlays = Overlays(match, insight, events, attackingTeam, duration);
        var camera = Camera(match, events, ball, attackingTeam, insight);

        return new ReplayClip(key, label, from, to, duration, match.ScoreAt(from - 1), events, tracks, ball, overlays, camera);
    }

    public sealed record TimedEvent(MatchEvent Event, double Time, double EndTime);

    /// <summary>
    /// Place events on the clip clock. Events sharing a match second are spread across that second in sequence
    /// order. Passes and carries end when the next action starts (or after a plausible travel time).
    /// </summary>
    public static IReadOnlyList<TimedEvent> TimeEvents(Match match, IReadOnlyList<MatchEvent> events, int from)
    {
        var starts = new double[events.Count];
        for (var i = 0; i < events.Count;)
        {
            var j = i;
            while (j < events.Count && events[j].MatchSecond == events[i].MatchSecond) j++;
            var n = j - i;
            for (var k = i; k < j; k++) starts[k] = events[k].MatchSecond - from + (k - i) * (0.9 / n);
            i = j;
        }

        var result = new List<TimedEvent>(events.Count);
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var t = starts[i];
            var end = t;
            if (e.Type is EventType.Pass or EventType.Carry or EventType.Shot or EventType.Clearance && e.HasLocation && e.HasEndLocation)
            {
                var dist = Metres(e.StartX!.Value, e.StartY!.Value, e.EndX!.Value, e.EndY!.Value);
                var speed = e.Type switch { EventType.Shot => 26.0, EventType.Carry => 6.5, EventType.Clearance => 22.0, _ => 15.0 };
                var travel = Math.Clamp(dist / speed, 0.35, e.Type == EventType.Carry ? 4.0 : 3.0);
                var next = i + 1 < events.Count ? starts[i + 1] : double.MaxValue;
                end = e.Type == EventType.Shot ? t + travel : Math.Min(Math.Max(t + 0.35, next), t + travel * 1.6);
                if (next < double.MaxValue && next - t > 0.35 && e.Type != EventType.Shot) end = Math.Min(Math.Max(end, Math.Min(next, t + travel)), next);
            }
            result.Add(new TimedEvent(e, Math.Round(t, 3), Math.Round(end, 3)));
        }
        return result;
    }

    private static ReplayEvent ToReplayEvent(Match match, TimedEvent t, IReadOnlySet<string> evidence)
    {
        var e = t.Event;
        var home = match.IsHome(e.TeamId);
        (double X, double Y)? s = e.HasLocation ? Pitch.ToAbsolute(e.StartX!.Value, e.StartY!.Value, home) : null;
        (double X, double Y)? end = e.HasEndLocation ? Pitch.ToAbsolute(e.EndX!.Value, e.EndY!.Value, home) : null;
        return new ReplayEvent(e.Id, e.SequenceNumber, t.Time, t.EndTime, e.MatchSecond, MatchClock.Format(e.MatchSecond),
            e.Type.ToString(), e.Outcome.ToString(), LabelOf(match, e), e.TeamId, e.PlayerId, e.RelatedPlayerId,
            R(s?.X), R(s?.Y), R(end?.X), R(end?.Y), e.Qualifier, evidence.Contains(e.Id));
    }

    /// <summary>Broadcast label for an event, e.g. HIGH_PRESS_INTERCEPTION or PROGRESSIVE_PASS.</summary>
    public static string LabelOf(Match match, MatchEvent e) => e.Type switch
    {
        EventType.Pass when e.Qualifier == "KickOff" => "KICK_OFF",
        EventType.Pass when e.Outcome != EventOutcome.Complete => "PASS_LOST",
        EventType.Pass when e.Qualifier is "Cross" => "CROSS",
        EventType.Pass when e.Qualifier is "Cutback" => "CUTBACK",
        EventType.Pass when e.Qualifier is "LongBall" => "LONG_BALL",
        EventType.Pass when e.EndX - e.StartX >= 15 && e.EndX >= 50 => "PROGRESSIVE_PASS",
        EventType.Pass => "PASS",
        EventType.Carry => "CARRY",
        EventType.Shot => e.Outcome switch
        {
            EventOutcome.Goal => "GOAL",
            EventOutcome.OnTarget => "SHOT_ON_TARGET",
            EventOutcome.Blocked => "SHOT_BLOCKED",
            _ => "SHOT_OFF_TARGET",
        },
        EventType.Interception => e.StartX >= 60 ? "HIGH_PRESS_INTERCEPTION" : "INTERCEPTION",
        EventType.BallRecovery => e.StartX >= 60 ? "HIGH_PRESSURE_RECOVERY" : "RECOVERY",
        EventType.Tackle => "TACKLE",
        EventType.Turnover => "TURNOVER",
        EventType.Clearance => "CLEARANCE",
        EventType.Save => "SAVE",
        EventType.Corner => "CORNER",
        EventType.Foul => "FOUL",
        EventType.Card => "CARD",
        EventType.Offside => "OFFSIDE",
        _ => e.Type.ToString().ToUpperInvariant(),
    };

    // ------------------------------------------------------------------ ball

    public static IReadOnlyList<BallSegment> BuildBall(Match match, IReadOnlyList<TimedEvent> timed, double duration)
    {
        var segments = new List<BallSegment>();
        double bx = 50, by = 50, bz = 0, now = double.NegativeInfinity;
        string? holder = null;
        var started = false;

        void Gap(double until, double x, double y, string? nextHolder)
        {
            if (!started) { bx = x; by = y; now = until; started = true; return; }
            if (until > now)
            {
                if (holder is not null && Metres(bx, by, x, y) < 1.5)
                    segments.Add(new BallSegment(now, until, BallMotion.Held, bx, by, x, y, 0, 0, 0, holder, null, PositionProvenance.InterpolatedPosition));
                else
                    segments.Add(new BallSegment(now, until, holder is null ? BallMotion.Loose : BallMotion.Held, bx, by, x, y, bz, 0, 0,
                        holder, null, PositionProvenance.InterpolatedPosition));
            }
            now = Math.Max(now, until);
            bx = x; by = y; bz = 0;
            holder = nextHolder;
        }

        foreach (var t in timed)
        {
            var e = t.Event;
            if (!e.HasLocation) continue;
            var home = match.IsHome(e.TeamId);
            var (x, y) = Pitch.ToAbsolute(e.StartX!.Value, e.StartY!.Value, home);
            switch (e.Type)
            {
                case EventType.Pass or EventType.Clearance when e.HasEndLocation:
                {
                    Gap(t.Time, x, y, e.PlayerId);
                    var (ex, ey) = Pitch.ToAbsolute(e.EndX!.Value, e.EndY!.Value, home);
                    var dist = Metres(x, y, ex, ey);
                    var lofted = e.Type == EventType.Clearance || e.Qualifier is "LongBall" or "Cross" || (e.StartX >= 99) || dist > 32;
                    var arc = lofted ? Math.Clamp(dist * 0.12, 1.5, 7) : 0;
                    segments.Add(new BallSegment(t.Time, t.EndTime, BallMotion.Pass, x, y, ex, ey, 0, 0, Math.Round(arc, 2), null, e.Id,
                        PositionProvenance.InterpolatedPosition));
                    now = t.EndTime; bx = ex; by = ey; bz = 0;
                    holder = e.Outcome == EventOutcome.Complete ? e.RelatedPlayerId : null;
                    break;
                }
                case EventType.Carry when e.HasEndLocation:
                {
                    Gap(t.Time, x, y, e.PlayerId);
                    var (ex, ey) = Pitch.ToAbsolute(e.EndX!.Value, e.EndY!.Value, home);
                    segments.Add(new BallSegment(t.Time, t.EndTime, BallMotion.Carry, x, y, ex, ey, 0, 0, 0, e.PlayerId, e.Id,
                        PositionProvenance.InterpolatedPosition));
                    now = t.EndTime; bx = ex; by = ey;
                    holder = e.PlayerId;
                    break;
                }
                case EventType.Shot:
                {
                    Gap(t.Time, x, y, e.PlayerId);
                    var goalX = home ? 100.0 : 0.0;
                    var targetY = e.EndY is { } ty ? (home ? ty : 100 - ty) : 50;
                    var (ex, ey, ez) = e.Outcome switch
                    {
                        EventOutcome.Goal => (home ? 101.5 : -1.5, targetY, 1.0),
                        EventOutcome.OnTarget => (home ? 99.6 : 0.4, targetY, 1.2),
                        EventOutcome.Blocked => (x + (goalX - x) * 0.3, y + (targetY - y) * 0.3, 0.4),
                        _ => (home ? 101.0 : -1.0, targetY, 1.7),
                    };
                    segments.Add(new BallSegment(t.Time, t.EndTime, BallMotion.Shot, x, y, ex, ey, 0.1, ez,
                        Math.Round(e.Outcome == EventOutcome.Blocked ? 0.3 : 0.8, 2), null, e.Id, PositionProvenance.InterpolatedPosition));
                    now = t.EndTime; bx = ex; by = ey; bz = ez;
                    holder = null;
                    break;
                }
                case EventType.Interception or EventType.BallRecovery or EventType.Tackle or EventType.Save:
                    Gap(t.Time, x, y, e.PlayerId);
                    holder = e.PlayerId;
                    break;
                case EventType.Corner:
                    Gap(t.Time, x, y, e.PlayerId);
                    break;
            }
        }

        // Fill to the end of the clip and clip every segment to [0, duration].
        if (started && now < duration)
            segments.Add(new BallSegment(now, duration, holder is null ? BallMotion.Loose : BallMotion.Held, bx, by, bx, by, bz, bz, 0, holder,
                null, PositionProvenance.InterpolatedPosition));
        return ClipSegments(segments, duration);
    }

    private static IReadOnlyList<BallSegment> ClipSegments(List<BallSegment> segments, double duration)
    {
        var result = new List<BallSegment>();
        foreach (var s in segments.Where(s => s.T1 > 0 && s.T0 < duration && s.T1 > s.T0))
        {
            var t0 = Math.Max(0, s.T0);
            var t1 = Math.Min(duration, s.T1);
            var (x0, y0) = Lerp(s, t0);
            var (x1, y1) = Lerp(s, t1);
            result.Add(s with { T0 = Math.Round(t0, 3), T1 = Math.Round(t1, 3), X0 = R(x0)!.Value, Y0 = R(y0)!.Value, X1 = R(x1)!.Value, Y1 = R(y1)!.Value });
        }
        if (result.Count == 0) return result;
        // Close any tiny gaps so the ball is always defined.
        if (result[0].T0 > 0) result.Insert(0, result[0] with { T1 = result[0].T0, T0 = 0, Motion = BallMotion.Loose, X1 = result[0].X0, Y1 = result[0].Y0, EventId = null });
        for (var i = 1; i < result.Count; i++)
        {
            if (result[i].T0 > result[i - 1].T1 + 1e-6)
                result.Insert(i, result[i - 1] with { T0 = result[i - 1].T1, T1 = result[i].T0, Motion = BallMotion.Loose, X0 = result[i - 1].X1, Y0 = result[i - 1].Y1,
                    X1 = result[i].X0, Y1 = result[i].Y0, Z0 = result[i - 1].Z1, Z1 = result[i].Z0, ArcHeight = 0, EventId = null, HolderId = null });
            else if (result[i].T0 < result[i - 1].T1) result[i] = result[i] with { T0 = result[i - 1].T1 };
        }
        return result;

        static (double X, double Y) Lerp(BallSegment s, double t)
        {
            var f = s.T1 <= s.T0 ? 1 : Math.Clamp((t - s.T0) / (s.T1 - s.T0), 0, 1);
            return (s.X0 + (s.X1 - s.X0) * f, s.Y0 + (s.Y1 - s.Y0) * f);
        }
    }

    /// <summary>Ground position of the ball at time t (holder segments use the segment endpoints as an approximation).</summary>
    public static (double X, double Y) BallAt(IReadOnlyList<BallSegment> ball, double t)
    {
        if (ball.Count == 0) return (50, 50);
        var s = ball.FirstOrDefault(b => t >= b.T0 && t <= b.T1) ?? (t < ball[0].T0 ? ball[0] : ball[^1]);
        var f = s.T1 <= s.T0 ? 1 : Math.Clamp((t - s.T0) / (s.T1 - s.T0), 0, 1);
        return (s.X0 + (s.X1 - s.X0) * f, s.Y0 + (s.Y1 - s.Y0) * f);
    }

    // ------------------------------------------------------------------ players

    /// <summary>Players on the pitch at a match second: starters with substitutions applied.</summary>
    public static IReadOnlyList<Player> LineupAt(Match match, int second)
    {
        var lineup = match.HomeTeam.Starters.Concat(match.AwayTeam.Starters).ToList();
        foreach (var sub in match.Events.Where(e => e.Type == EventType.Substitution && e.MatchSecond <= second))
        {
            var index = lineup.FindIndex(p => p.Id == sub.PlayerId);
            var on = match.FindPlayer(sub.RelatedPlayerId);
            if (index >= 0 && on is not null) lineup[index] = on;
        }
        return lineup;
    }

    public sealed record Keyframe(double T, double X, double Y);

    /// <summary>Every recorded player position in the timed events (absolute coordinates).</summary>
    public static Dictionary<string, List<Keyframe>> Keyframes(Match match, IReadOnlyList<TimedEvent> timed)
    {
        var result = new Dictionary<string, List<Keyframe>>();
        void Add(string? player, double t, double x, double y)
        {
            if (player is null) return;
            if (!result.TryGetValue(player, out var list)) result[player] = list = [];
            list.Add(new Keyframe(Math.Round(t, 3), R(x)!.Value, R(y)!.Value));
        }

        foreach (var t in timed)
        {
            var e = t.Event;
            if (!e.HasLocation) continue;
            var home = match.IsHome(e.TeamId);
            var (x, y) = Pitch.ToAbsolute(e.StartX!.Value, e.StartY!.Value, home);
            if (e.Type == EventType.Save) { Add(e.PlayerId, t.Time, home ? 2 : 98, y); continue; }
            Add(e.PlayerId, t.Time, x, y);
            if (e.HasEndLocation)
            {
                var (ex, ey) = Pitch.ToAbsolute(e.EndX!.Value, e.EndY!.Value, home);
                if (e.Type == EventType.Pass && e.Outcome == EventOutcome.Complete) Add(e.RelatedPlayerId, t.EndTime, ex, ey);
                if (e.Type == EventType.Carry) Add(e.PlayerId, t.EndTime, ex, ey);
            }
            if (e.Type is EventType.Foul or EventType.Tackle) Add(e.RelatedPlayerId, t.Time, x, y);
        }

        foreach (var key in result.Keys.ToList())
        {
            // One keyframe per instant: the later event in sequence wins.
            result[key] = result[key].GroupBy(k => k.T).Select(g => g.Last()).OrderBy(k => k.T).ToList();
        }
        return result;
    }

    /// <summary>Build one player's track: actual where recorded, interpolated between, estimated elsewhere.</summary>
    public static PlayerTrack Track(Match match, Player player, IReadOnlyList<Keyframe> keyframes, IReadOnlyList<BallSegment> ball, double duration,
        (double X, double Y)? anchor = null)
    {
        var times = new SortedSet<double>();
        for (var t = 0.0; t <= duration + 1e-9; t += SampleStep) times.Add(Math.Round(t, 3));
        foreach (var k in keyframes.Where(k => k.T >= 0 && k.T <= duration)) times.Add(k.T);

        var isHome = match.IsHome(player.TeamId);
        var samples = new List<TrackSample>(times.Count);
        double? smoothX = null, smoothY = null;
        var last = 0.0;

        foreach (var t in times)
        {
            // Formation estimate, smoothed over time so estimated players move rather than jump.
            var (ex, ey) = Estimate(anchor ?? DefaultAnchor(player), player.Position == Position.Goalkeeper, isHome, BallAt(ball, t));
            if (smoothX is null) { smoothX = ex; smoothY = ey; }
            else
            {
                var a = Math.Min(1, (t - last) / 1.2);
                smoothX += (ex - smoothX) * a;
                smoothY += (ey - smoothY) * a;
            }
            last = t;

            var prev = keyframes.LastOrDefault(k => k.T <= t + 1e-9);
            var next = keyframes.FirstOrDefault(k => k.T >= t - 1e-9);
            var nearest = new[] { prev, next }.Where(k => k is not null).OrderBy(k => Math.Abs(k!.T - t)).FirstOrDefault();

            if (nearest is not null && Math.Abs(nearest.T - t) <= 0.05)
            {
                samples.Add(new TrackSample(t, nearest.X, nearest.Y, PositionProvenance.ActualEventPosition));
            }
            else if (prev is not null && next is not null && next.T - prev.T <= MaxInterpolationGap)
            {
                var f = (t - prev.T) / (next.T - prev.T);
                samples.Add(new TrackSample(t, R(prev.X + (next.X - prev.X) * f)!.Value, R(prev.Y + (next.Y - prev.Y) * f)!.Value,
                    PositionProvenance.InterpolatedPosition));
            }
            else if (nearest is not null && Math.Abs(nearest.T - t) < BlendSeconds)
            {
                var w = 1 - Math.Abs(nearest.T - t) / BlendSeconds;
                samples.Add(new TrackSample(t, R(smoothX!.Value + (nearest.X - smoothX.Value) * w)!.Value,
                    R(smoothY!.Value + (nearest.Y - smoothY.Value) * w)!.Value, PositionProvenance.InterpolatedPosition));
            }
            else
            {
                samples.Add(new TrackSample(t, R(smoothX!.Value)!.Value, R(smoothY!.Value)!.Value, PositionProvenance.EstimatedPosition));
            }
        }
        return new PlayerTrack(player.Id, samples);
    }

    /// <summary>Events at or beyond this count let a player's own events dominate their estimated anchor.</summary>
    public const int MinEventsForDataAnchor = 6;

    /// <summary>
    /// Where each player "lives" on the pitch (team frame, attacking towards X = 100), used for estimated positions.
    /// Works for any data source:
    /// 1. role default - a standard spot for the position (GK, full-backs, centre-backs, midfield, wingers, strikers),
    ///    with team-mates in the same role spread across the pitch;
    /// 2. a supplied behaviour profile (the synthetic generator provides one) replaces the role default;
    /// 3. the player's own located events up to <paramref name="uptoSecond"/> - observed data - are blended in
    ///    (65%) once there are at least <see cref="MinEventsForDataAnchor"/> of them.
    /// </summary>
    public static IReadOnlyDictionary<string, (double X, double Y)> RoleAnchors(Match match, IReadOnlyList<Player> lineup, int uptoSecond)
    {
        var observed = match.Events
            .Where(e => e.MatchSecond <= uptoSecond && e.PlayerId is not null && e.HasLocation &&
                        e.Type is not (EventType.PeriodStart or EventType.PeriodEnd or EventType.Substitution or EventType.Goal) &&
                        e.Qualifier != "KickOff")
            .GroupBy(e => e.PlayerId!)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), X: g.Average(e => e.StartX!.Value), Y: g.Average(e => e.StartY!.Value)));

        var result = new Dictionary<string, (double X, double Y)>();
        foreach (var role in lineup.GroupBy(p => (p.TeamId, p.Position)))
        {
            var players = role.OrderBy(p => p.ShirtNumber).ToList();
            for (var k = 0; k < players.Count; k++)
            {
                var player = players[k];
                var anchor = HasProfilePosition(player)
                    ? (player.Profile.AverageX, player.Profile.AverageY)
                    : SpreadRole(RoleDefault(player.Position), k, players.Count);
                if (observed.TryGetValue(player.Id, out var o) && o.Count >= MinEventsForDataAnchor)
                    anchor = (o.X * 0.65 + anchor.Item1 * 0.35, o.Y * 0.65 + anchor.Item2 * 0.35);
                result[player.Id] = anchor;
            }
        }
        return result;
    }

    /// <summary>True when the data source supplied an average position (the BehaviourProfile default is 50, 50).</summary>
    public static bool HasProfilePosition(Player player) => player.Profile.AverageX != 50 || player.Profile.AverageY != 50;

    /// <summary>Anchor for a single player without match context (profile if supplied, otherwise the role default).</summary>
    public static (double X, double Y) DefaultAnchor(Player player) =>
        HasProfilePosition(player) ? (player.Profile.AverageX, player.Profile.AverageY) : RoleDefault(player.Position);

    /// <summary>Standard team-frame spot for a position (Y = 0 is the team's left).</summary>
    public static (double X, double Y) RoleDefault(Position position) => position switch
    {
        Position.Goalkeeper => (6, 50),
        Position.RightBack => (33, 85),
        Position.CentreBack => (22, 50),
        Position.LeftBack => (33, 15),
        Position.DefensiveMidfielder => (40, 50),
        Position.CentralMidfielder => (50, 50),
        Position.AttackingMidfielder => (63, 50),
        Position.RightWinger => (70, 85),
        Position.LeftWinger => (70, 15),
        Position.Striker => (80, 50),
        _ => (50, 50),
    };

    /// <summary>Spread several players sharing a role across the pitch width (e.g. two centre-backs at Y 38 and 62).</summary>
    public static (double X, double Y) SpreadRole((double X, double Y) role, int index, int count) =>
        count <= 1 ? role : (role.X, Math.Clamp(role.Y + (index - (count - 1) / 2.0) * 24, 8, 92));

    /// <summary>Estimated position for a player without match context (profile or role default anchor).</summary>
    public static (double X, double Y) Estimate(Player player, bool isHome, (double X, double Y) ballAbsolute) =>
        Estimate(DefaultAnchor(player), player.Position == Position.Goalkeeper, isHome, ballAbsolute);

    /// <summary>
    /// Estimated position: the player's anchor (team frame) with the team block shifted up/down the pitch and
    /// slightly towards the ball's side. Illustrative only - marked as an estimated position.
    /// </summary>
    public static (double X, double Y) Estimate((double X, double Y) anchor, bool isGoalkeeper, bool isHome, (double X, double Y) ballAbsolute)
    {
        var (bx, by) = isHome ? ballAbsolute : Pitch.Flip(ballAbsolute.X, ballAbsolute.Y);
        double x, y;
        if (isGoalkeeper)
        {
            x = Math.Clamp(anchor.X + (bx - 50) * 0.12, 2, 18);
            y = 50 + (by - 50) * 0.15;
        }
        else
        {
            x = Math.Clamp(anchor.X + (bx - 50) * 0.45, 5, 95);
            y = Math.Clamp(anchor.Y + (by - anchor.Y) * 0.2, 3, 97);
        }
        return isHome ? (x, y) : Pitch.Flip(x, y);
    }

    // ------------------------------------------------------------------ overlays and camera

    private static IReadOnlyList<ReplayOverlay> Overlays(Match match, Insight insight, IReadOnlyList<ReplayEvent> events, string attacking,
        double duration)
    {
        var overlays = new List<ReplayOverlay>();
        var attackHome = match.IsHome(attacking);

        // Channel highlight: the insight's channel, or the route this attack actually took.
        var channel = insight.Fact("channel");
        var channelLabel = channel is null ? "ATTACK_ROUTE" : "INSIGHT_CHANNEL";
        if (channel is null && insight.Type is InsightType.DangerousTurnover or InsightType.Goal or InsightType.BigChance or InsightType.CounterAttackThreat)
        {
            var clipEvents = match.Events.Where(e => events.Any(r => r.EventId == e.Id)).ToList();
            channel = AttackChannelOf(clipEvents, attacking);
        }
        if (channel is not null && (insight.Fact("channel") is not null ? insight.TeamId == attacking : true))
        {
            var (y0, y1) = channel switch { "Left" => (0.0, 33.3), "Right" => (66.7, 100.0), _ => (33.3, 66.7) };
            var (ax0, ay0) = Pitch.ToAbsolute(Pitch.AttackZoneStartX, y0, attackHome);
            var (ax1, ay1) = Pitch.ToAbsolute(100, y1, attackHome);
            overlays.Add(new ReplayOverlay("Channel", attacking, $"{channelLabel}:{channel}", Math.Min(ax0, ax1), Math.Min(ay0, ay1),
                Math.Max(ax0, ax1), Math.Max(ay0, ay1), [], [], 0, duration));
        }

        if (insight.Type is InsightType.PressureIncrease or InsightType.ShotPressure or InsightType.TerritorialShift or InsightType.ControlShift)
        {
            var (zx0, zy0) = Pitch.ToAbsolute(Pitch.AttackingThirdStart, 0, attackHome);
            var (zx1, zy1) = Pitch.ToAbsolute(100, 100, attackHome);
            overlays.Add(new ReplayOverlay("Zone", attacking, "ATTACKING_THIRD", Math.Min(zx0, zx1), Math.Min(zy0, zy1), Math.Max(zx0, zx1),
                Math.Max(zy0, zy1), [], [], 0, duration));
        }

        // Ball winners from the attacking team: the press.
        var winners = events.Where(e => e.TeamId == attacking && e.Type is "Interception" or "BallRecovery" or "Tackle" && e.PlayerId is not null).ToList();
        if (winners.Count > 0)
            overlays.Add(new ReplayOverlay("PressingPlayers", attacking, "BALL_WINNERS", null, null, null, null,
                winners.Select(w => w.PlayerId!).Distinct().ToList(), winners.Select(w => w.EventId).ToList(), winners.Min(w => w.Time), duration));

        // Turnover / regain marker.
        var loss = events.FirstOrDefault(e => e.TeamId != attacking && e.Label is "PASS_LOST" or "TURNOVER");
        var regain = events.FirstOrDefault(e => e.TeamId == attacking && e.Type is "Interception" or "BallRecovery" or "Tackle");
        var marker = regain ?? loss;
        if (marker?.X is not null)
            overlays.Add(new ReplayOverlay("Marker", marker.TeamId, regain is not null ? "TURNOVER_WON" : "TURNOVER", marker.X, marker.Y, null, null,
                marker.PlayerId is null ? [] : [marker.PlayerId], [marker.EventId], marker.Time, duration));

        var shot = events.LastOrDefault(e => e.Type == "Shot" && e.TeamId == attacking);
        if (shot?.X is not null)
            overlays.Add(new ReplayOverlay("Marker", shot.TeamId, shot.Label, shot.X, shot.Y, null, null,
                shot.PlayerId is null ? [] : [shot.PlayerId], [shot.EventId], shot.Time, duration));

        // The attacking team's passing route, revealed as it happens.
        var route = events.Where(e => e.TeamId == attacking && e.Type is "Pass" or "Carry" && e.Outcome == "Complete").Select(e => e.EventId).ToList();
        if (route.Count > 0)
            overlays.Add(new ReplayOverlay("PassPath", attacking, "ROUTE", null, null, null, null, [], route, 0, duration));

        return overlays;
    }

    private static ReplayCamera Camera(Match match, IReadOnlyList<ReplayEvent> events, IReadOnlyList<BallSegment> ball, string attacking, Insight insight)
    {
        var points = ball.SelectMany(b => new[] { (b.X0, b.Y0), (b.X1, b.Y1) }).Where(p => p.Item1 is >= 0 and <= 100).ToList();
        var cx = points.Count == 0 ? 50 : points.Average(p => p.Item1);
        var cy = points.Count == 0 ? 50 : points.Average(p => p.Item2);
        var attackingActors = events.Where(e => e.TeamId == attacking && e.PlayerId is not null && e.Type is "Pass" or "Carry" or "Shot").ToList();
        var focus = attackingActors.LastOrDefault(e => e.Type == "Shot")?.PlayerId
                    ?? attackingActors.GroupBy(e => e.PlayerId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
                    ?? insight.KeyPlayerId;
        return new ReplayCamera(attacking, match.IsHome(attacking) ? 1 : -1, focus, Math.Round(cx, 1), Math.Round(cy, 1));
    }

    private static int IndexOf(IReadOnlyList<MatchEvent> events, MatchEvent target)
    {
        for (var i = 0; i < events.Count; i++) if (ReferenceEquals(events[i], target)) return i;
        return -1;
    }

    private static double Metres(double x0, double y0, double x1, double y1) =>
        Math.Sqrt(Math.Pow((x1 - x0) * 1.05, 2) + Math.Pow((y1 - y0) * 0.68, 2));

    private static double? R(double? v) => v is null ? null : Math.Round(v.Value, 2);
}
