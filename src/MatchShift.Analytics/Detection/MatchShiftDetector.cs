using MatchShift.Analytics.Detection.Detectors;
using MatchShift.Analytics.State;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Analytics.Detection;

/// <summary>
/// The central, deterministic MatchShift detector. Runs every shift detector on
/// each one-minute tick and the moment detector on each event, then applies
/// noise control before anything becomes an <see cref="Insight"/>:
///
/// 1. Warm-up: shift detectors are silent before <see cref="DetectionOptions.WarmupMinutes"/>.
/// 2. Persistence: a shift must hold for <see cref="DetectionOptions.RequiredConsecutiveTicks"/> ticks.
/// 3. Cooldown: the same type/team/variant cannot repeat within <see cref="DetectionOptions.CooldownMinutes"/>;
///    the same type/team cannot repeat within half that. A shift that simply persists is never re-announced:
///    it can fire again only after its condition has cleared.
/// 4. Suppression: possession/territorial shifts are dropped when a control shift for the same team
///    was just reported, because control already explains them.
/// 5. Moments are de-duplicated by their anchoring event.
/// </summary>
public sealed class MatchShiftDetector
{
    private static readonly InsightType[] SuppressedByControl = [InsightType.PossessionShift, InsightType.TerritorialShift];

    private readonly IReadOnlyList<IShiftDetector> _detectors;
    private readonly MomentDetector _moments = new();
    private readonly DetectionOptions _options;
    private readonly Dictionary<string, int> _streaks = [];
    private readonly Dictionary<string, int> _lastFired = [];
    private readonly HashSet<string> _active = [];
    private readonly HashSet<string> _anchors = [];
    private readonly List<Insight> _insights = [];
    private int _counter;

    public MatchShiftDetector(DetectionOptions? options = null, IReadOnlyList<IShiftDetector>? detectors = null)
    {
        _options = options ?? DetectionOptions.Default;
        _detectors = detectors ?? DefaultDetectors();
    }

    public static IReadOnlyList<IShiftDetector> DefaultDetectors() =>
    [
        new AttackChannelShiftDetector(),
        new PostSubstitutionChangeDetector(),
        new PressureShiftDetector(),
        new ControlShiftDetector(),
        new ShotPressureDetector(),
        new CounterAttackThreatDetector(),
        new ChaosIncreaseDetector(),
        new TurnoverSpikeDetector(),
        new TempoShiftDetector(),
        new TerritorialShiftDetector(),
        new PossessionShiftDetector(),
    ];

    public DetectionOptions Options => _options;
    public IReadOnlyList<Insight> Insights => _insights;

    /// <summary>Evaluate all shift detectors at <paramref name="now"/> (a minute boundary).</summary>
    public IReadOnlyList<Insight> EvaluateTick(MatchState state, int now)
    {
        var emitted = new List<Insight>();
        if (now < _options.WarmupMinutes * 60) return emitted;

        var context = new DetectionContext(state, now, _options);
        var candidates = _detectors.SelectMany(d => d.Evaluate(context)).ToList();

        var seen = candidates.Select(c => c.Key).ToHashSet();
        foreach (var key in _streaks.Keys.Where(k => !seen.Contains(k)).ToList()) _streaks.Remove(key);
        _active.RemoveWhere(k => !seen.Contains(k));
        foreach (var key in seen) _streaks[key] = _streaks.GetValueOrDefault(key) + 1;

        foreach (var candidate in candidates)
        {
            if (_streaks[candidate.Key] < RequiredTicks(candidate.Type)) continue;
            if (_active.Contains(candidate.Key)) continue;
            if (InCooldown(candidate, now)) continue;
            if (IsSuppressed(candidate, state.Context.HomeTeamId, state.Context.AwayTeamId, now)) continue;
            emitted.Add(Emit(state, candidate, now));
        }
        return emitted;
    }

    /// <summary>Evaluate event-anchored moments for an event that has just been ingested.</summary>
    public IReadOnlyList<Insight> OnEvent(MatchState state, MatchEvent e)
    {
        var emitted = new List<Insight>();
        foreach (var candidate in _moments.OnEvent(state, e, _options))
            TryEmitMoment(state, candidate, e.MatchSecond, emitted);
        return emitted;
    }

    private void TryEmitMoment(MatchState state, DetectionCandidate candidate, int now, List<Insight> emitted)
    {
        var anchor = $"{candidate.Type}|{candidate.AnchorEventId}";
        if (candidate.AnchorEventId is not null && !_anchors.Add(anchor)) return;

        // One non-goal moment of a kind per team per spell: a scramble of losses is one story, not five.
        if (candidate.Type != InsightType.Goal &&
            _insights.Any(i => i.Type == candidate.Type && i.TeamId == candidate.TeamId && now - i.DetectedAtSecond < MomentSpacingSeconds))
            return;

        emitted.Add(Emit(state, candidate, now));
    }

    private const int MomentSpacingSeconds = 150;

    private int RequiredTicks(InsightType type) => type switch
    {
        InsightType.AttackChannelShift => _options.AttackChannel.RequiredConsecutiveTicks,
        InsightType.PostSubstitutionChange => Math.Min(2, _options.RequiredConsecutiveTicks),
        _ => _options.RequiredConsecutiveTicks,
    };

    private bool InCooldown(DetectionCandidate c, int now)
    {
        if (_lastFired.TryGetValue(c.Key, out var last) && now - last < _options.CooldownMinutes * 60) return true;
        var typeKey = $"{c.Type}|{c.TeamId}|*";
        return _lastFired.TryGetValue(typeKey, out var lastType) && now - lastType < _options.CooldownMinutes * 30;
    }

    /// <summary>
    /// Redundancy rules: an insight is dropped when a recent, stronger insight already tells the same story.
    /// - possession / territorial shift for a team that just gained control or started pressing;
    /// - a team's pressure "decrease" right after the opponent's pressure increase or control shift (mirror image).
    /// </summary>
    private bool IsSuppressed(DetectionCandidate c, string homeId, string awayId, int now)
    {
        bool Recent(InsightType type, string? team) =>
            _insights.Any(i => i.Type == type && i.TeamId == team && now - i.DetectedAtSecond <= SuppressionWindowSeconds);

        if (SuppressedByControl.Contains(c.Type))
            return Recent(InsightType.ControlShift, c.TeamId) || Recent(InsightType.PressureIncrease, c.TeamId);

        if (c.Type == InsightType.PressureDecrease && c.TeamId is not null)
        {
            var opponent = c.TeamId == homeId ? awayId : homeId;
            return Recent(InsightType.PressureIncrease, opponent) || Recent(InsightType.ControlShift, opponent);
        }
        return false;
    }

    private const int SuppressionWindowSeconds = 6 * 60;

    private Insight Emit(MatchState state, DetectionCandidate c, int now)
    {
        _lastFired[c.Key] = now;
        if (c.AnchorEventId is null) _active.Add(c.Key);
        _lastFired[$"{c.Type}|{c.TeamId}|*"] = now;
        var insight = new Insight
        {
            Id = $"{state.Context.MatchId}-I{++_counter:D3}",
            MatchId = state.Context.MatchId,
            Type = c.Type,
            DetectedAtSecond = now,
            Span = c.Span,
            TeamId = c.TeamId,
            KeyPlayerId = c.KeyPlayerId,
            RelatedPlayerIds = c.RelatedPlayerIds,
            Severity = c.Severity,
            Confidence = Statistics.LevelOf(c.ConfidenceScore),
            ConfidenceScore = c.ConfidenceScore,
            Facts = c.Facts,
            Evidence = c.Evidence,
            SupportingEventIds = c.SupportingEventIds.Distinct().ToList(),
            Visuals = c.Visuals,
            InterpretationCode = c.InterpretationCode,
            Importance = ImportanceModel.Score(c, now, c.TeamId is null ? 0 : state.GoalDifference(c.TeamId, now)),
        };
        _insights.Add(insight);
        return insight;
    }
}

/// <summary>
/// Transparent importance score (0-100) used by highlights and the
/// "just tell me what matters" viewer mode.
/// </summary>
public static class ImportanceModel
{
    public static double BaseOf(InsightType type) => type switch
    {
        InsightType.Goal => 100,
        InsightType.DangerousTurnover => 64,
        InsightType.AttackChannelShift => 70,
        InsightType.PostSubstitutionChange => 68,
        InsightType.ControlShift => 66,
        InsightType.BigChance => 62,
        InsightType.PressureIncrease => 62,
        InsightType.CounterAttackThreat => 60,
        InsightType.ShotPressure => 58,
        InsightType.ChaosIncrease => 56,
        InsightType.TurnoverSpike => 48,
        InsightType.TerritorialShift => 46,
        InsightType.TempoShift => 44,
        InsightType.PossessionShift => 42,
        InsightType.PressureDecrease => 40,
        _ => 40,
    };

    public static double Score(DetectionCandidate c, int now, int goalDifference)
    {
        if (c.Type == InsightType.Goal) return 100;
        var score = BaseOf(c.Type);
        score += c.Severity switch { Severity.High => 8, Severity.Medium => 0, _ => -8 };
        score += (c.ConfidenceScore - 0.8) * 20;
        if (now >= 75 * 60) score += 4;
        if (Math.Abs(goalDifference) <= 1) score += 3;
        return Math.Round(Math.Clamp(score, 0, 99), 1);
    }
}
