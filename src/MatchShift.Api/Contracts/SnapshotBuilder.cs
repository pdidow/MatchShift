using MatchShift.Agents.Narrative;
using MatchShift.Agents.Agents;
using MatchShift.Analytics;
using MatchShift.Analytics.Indices;
using MatchShift.Analytics.Metrics;
using MatchShift.Analytics.Story;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Api.Contracts;

public static class SnapshotBuilder
{
    public const int RecentEventCount = 30;

    public static TeamDto Team(Team t) => new(t.Id, t.Name, t.ShortName, t.Code, t.PrimaryColor, t.SecondaryColor, t.Formation);

    public static PlayerDto Player(Player p) => new(p.Id, p.Name, p.ShortName, p.ShirtNumber, p.Position.ToString(), p.TeamId, p.IsStarter);

    public static MatchSummaryDto Summary(Match m) =>
        new(m.Id, m.ScenarioName, m.Seed, Team(m.HomeTeam), Team(m.AwayTeam), m.ScoreAt(MatchClock.FullTimeSecond), m.Events.Count, m.IsSynthetic);

    public static PitchEventDto Event(MatchEvent e, Match match)
    {
        var home = match.IsHome(e.TeamId);
        (double X, double Y)? start = e.HasLocation ? Pitch.ToAbsolute(e.StartX!.Value, e.StartY!.Value, home) : null;
        (double X, double Y)? end = e.HasEndLocation ? Pitch.ToAbsolute(e.EndX!.Value, e.EndY!.Value, home) : null;
        return new PitchEventDto(e.Id, e.SequenceNumber, e.MatchSecond, MatchClock.Format(e.MatchSecond), e.Type.ToString(), e.Outcome.ToString(),
            e.TeamId, e.PlayerId, match.FindPlayer(e.PlayerId)?.Name, R(start?.X), R(start?.Y), R(end?.X), R(end?.Y), e.Qualifier);

        static double? R(double? v) => v is null ? null : Math.Round(v.Value, 1);
    }

    public static InsightCardDto Card(Insight i, NarrativeContext context, Func<Insight, NarrativeContext, InsightNarrative>? narrate = null) =>
        new(i.Id, i.Type.Code(), i.DisplayMinute, i.DetectedAtSecond, i.TeamId, i.Severity, i.Confidence, i.ConfidenceScore, i.Importance,
            i.KeyPlayerId, i.Span, i.Visuals, i.SupportingEventIds.Count, narrate?.Invoke(i, context) ?? TemplateInsightProvider.Narrate(i, context));

    public static SnapshotDto Build(LiveMatchAnalyzer analysis, int clock, Match match, ViewerProfile profile, bool playing, int speed,
        Func<Insight, NarrativeContext, InsightNarrative>? narrate = null)
    {
        var context = new NarrativeContext(new MatchRoster(match), profile);
        var state = analysis.State;
        var recent = state.Events.TakeLast(RecentEventCount).Select(e => Event(e, match)).ToList();

        var window = AnalysisWindow.Last(Math.Max(clock, 60), 10);
        var w = MetricsCalculator.Compute(state, window);
        var control = MatchControl.Compute(w);
        var chaos = ChaosIndex.Compute(w);
        var pressureWindow = MetricsCalculator.Compute(state, AnalysisWindow.Last(Math.Max(clock, 60), LiveMatchAnalyzer.TimelinePressureWindowMinutes));

        TeamLiveMetricsDto TeamMetrics(TeamWindowMetrics m, TeamWindowMetrics p) => new(
            m.TeamId, Math.Round(m.PossessionShare, 3), PressureIndex.Compute(p).Value, m.Shots, m.ShotsOnTarget, Math.Round(m.ChanceQuality, 2),
            m.Attacks, Math.Round(m.LeftShare, 3), Math.Round(m.CentreShare, 3), Math.Round(m.RightShare, 3), m.Turnovers, m.HighRecoveries,
            m.PassesAttempted, m.PassesCompleted, m.ProgressivePasses, m.LongBalls);

        var insights = NarrativeFilter.Select(analysis.Insights, profile)
            .Where(i => EvidenceGate.Check(i, match).Approved)
            .OrderByDescending(i => profile.FocusPlayerId is { } player && TemplateInsightProvider.Involvement(i, match, player) > 0)
            .ThenByDescending(i => i.DetectedAtSecond).ThenByDescending(i => i.Importance)
            .Select(i => Card(i, context, narrate)).ToList();

        var highlights = HighlightRanker.Rank(analysis, clock).Take(8)
            .Select(h =>
            {
                var n = HighlightNarrator.Narrate(h, analysis.Insights, context);
                return new HighlightCardDto(h.Id, h.Kind, h.DisplayMinute, h.Second, h.TeamId, h.ImportanceScore, h.InsightId, n.Headline,
                    n.WhyItMatters, h.RelevantEventIds.Count);
            })
            .OrderBy(h => h.Second).ToList();

        return new SnapshotDto(
            match.Id,
            playing,
            speed,
            clock >= MatchClock.FullTimeSecond,
            new ClockDto(clock, MatchClock.DisplayMinute(clock), MatchClock.PeriodOf(Math.Min(clock, MatchClock.FullTimeSecond - 1)), MatchClock.Format(clock)),
            match.ScoreAt(clock),
            Team(match.HomeTeam),
            Team(match.AwayTeam),
            recent,
            new LiveMetricsDto(window.Label, TeamMetrics(w.Home, pressureWindow.Home), TeamMetrics(w.Away, pressureWindow.Away),
                control.HomeControl, chaos.Value, chaos.State, control.Components, chaos.Components),
            analysis.Timeline.ToArray(),
            insights,
            highlights,
            profile, FocusedPlayer: match.FindPlayer(profile.FocusPlayerId) is { } p ? PlayerPerformance.Calculate(state, p.Id) : null);
    }

    public static ViewerProfile Profile(string? lang, string? mode, string? team, string? player) => new(
        Enum.TryParse<ViewerMode>(mode, true, out var m) ? m : ViewerMode.Analyst,
        LexiconRegistry.IsSupported(lang) ? lang!.ToLowerInvariant() : "en",
        string.IsNullOrWhiteSpace(team) ? null : team,
        string.IsNullOrWhiteSpace(player) ? null : player);
}
