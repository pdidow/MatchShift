using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MatchShift.Agents.Narrative;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Agents.Workflow;

public sealed record TeamFact(string Id, string Name, string ShortName);

public sealed record ScoreFact(int Home, int Away)
{
    public override string ToString() => $"{Home}-{Away}";
}

public sealed record WindowFact(int FromMinute, int ToMinute, string Label);

/// <summary>One deterministic metric exactly as MatchShift computed it. <c>Display*</c> are the viewer-facing renderings.</summary>
public sealed record MetricFact(
    string Key,
    string Label,
    string Unit,
    double? Baseline,
    double Current,
    double? Change,
    double? Threshold,
    string? DisplayBaseline,
    string DisplayCurrent,
    string? DisplayChange,
    string? DisplayThreshold,
    WindowFact? BaselineWindow,
    WindowFact? CurrentWindow,
    int SampleSize,
    int? BaselineSampleSize,
    int? MinimumSample,
    bool SampleRequirementMet,
    string? Comparison,
    bool IsPrimary);

public sealed record ConfidenceInputs(string Level, double Score, bool AllSampleRequirementsMet, string Severity);

/// <summary>A supporting event as the model sees it. <c>Zone</c> is derived deterministically from event coordinates.</summary>
public sealed record SourceEventFact(
    string Id, int Second, string Clock, string Type, string Outcome, string TeamId, string? PlayerId, string? PlayerName, string? Zone);

public sealed record PlayerFact(string Id, string Name, string TeamId);

/// <summary>The deterministic, lexicon-rendered wording of the insight. Authoritative; never produced by a model.</summary>
public sealed record AuthoritativeWording(string Headline, string Fact, string Inference, string? PossibleExplanation,
    string Ticker, string StudioLine, string? Teaching);

public sealed record AccessibilityRequirements(bool Required, bool SpokenInsights, bool VisualDescription, bool SimplifiedLanguage)
{
    public static AccessibilityRequirements None { get; } = new(false, false, false, false);
    public static AccessibilityRequirements All { get; } = new(true, true, true, true);
}

/// <summary>
/// The strongly typed, immutable shared state of one agent workflow execution. It is built ONLY by
/// deterministic code from an <see cref="Insight"/> raised by MatchShift's analytics, written once into
/// Agent Framework workflow state by the grounding executor, and read (never written) by every agent.
/// Agents produce separate interpretation objects; they cannot overwrite these facts.
/// </summary>
public sealed record InsightContext
{
    public required string WorkflowExecutionId { get; init; }
    public required string MatchId { get; init; }
    public required string InsightId { get; init; }
    public required string InsightType { get; init; }

    /// <summary>Evidence cut-off: the match second at which the insight was detected. Nothing later may be referenced.</summary>
    public required int MatchSecond { get; init; }
    public required string MatchTime { get; init; }
    public required int DisplayMinute { get; init; }
    public required TeamFact HomeTeam { get; init; }
    public required TeamFact AwayTeam { get; init; }
    public required ScoreFact Score { get; init; }
    public TeamFact? SubjectTeam { get; init; }
    public TeamFact? FocusTeam { get; init; }
    public required ImmutableArray<MetricFact> DeterministicMetrics { get; init; }
    public required ConfidenceInputs ConfidenceInputs { get; init; }
    public required ImmutableSortedDictionary<string, string> Facts { get; init; }
    public string? InterpretationCode { get; init; }
    public required ImmutableArray<string> SupportingEventIds { get; init; }
    public required ImmutableArray<SourceEventFact> SourceEvents { get; init; }
    public required ImmutableArray<PlayerFact> Players { get; init; }
    public required AuthoritativeWording DeterministicNarrative { get; init; }
    public required string SelectedAudienceMode { get; init; }
    public required string Language { get; init; }
    public required AccessibilityRequirements AccessibilityRequirements { get; init; }

    // ----- Validation-only data: never sent to a model. -----

    /// <summary>Second of every event in the match, so validation can tell an unknown ID from a future one.</summary>
    [JsonIgnore] public ImmutableDictionary<string, int> KnownEventSeconds { get; init; } = ImmutableDictionary<string, int>.Empty;

    /// <summary>Every squad player in the match (both teams), to detect players the evidence does not involve.</summary>
    [JsonIgnore] public ImmutableArray<PlayerFact> AllMatchPlayers { get; init; } = [];

    /// <summary>Clubs that are not in this match (e.g. the rest of the synthetic league).</summary>
    [JsonIgnore] public ImmutableArray<string> ForeignTeamNames { get; init; } = [];

    /// <summary>SHA-256 over the authoritative facts. Recomputed after the workflow to prove nothing changed them.</summary>
    public string Fingerprint() =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this with { WorkflowExecutionId = "" }, AgentJson.Options)))[..16];
}

public static class AgentJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Builds an <see cref="InsightContext"/> from deterministic MatchShift output. No model is involved.</summary>
public static class InsightContextBuilder
{
    /// <summary>Source events sent to the model are capped to keep prompts compact; validation still knows every supporting ID.</summary>
    public const int MaxSourceEvents = 12;

    public static InsightContext Build(Insight insight, Match match, ViewerProfile profile, AccessibilityRequirements accessibility,
        string workflowExecutionId, IEnumerable<string>? foreignTeamNames = null)
    {
        var lex = LexiconRegistry.Get(profile.Language);
        var context = new NarrativeContext(new MatchRoster(match), profile);
        var wording = TemplateInsightProvider.Narrate(insight, context);
        var lines = TemplateInsightProvider.EvidenceLines(insight, lex);
        var byId = match.Events.ToDictionary(e => e.Id);
        var supporting = insight.SupportingEventIds.Where(byId.ContainsKey).Select(id => byId[id])
            .Where(e => e.MatchSecond <= insight.DetectedAtSecond).OrderBy(e => e.SequenceNumber).ToList();

        // Shots and goals first, then the most recent events: the evidence a viewer is most likely to discuss.
        var shown = supporting.Where(e => e.Type is EventType.Shot or EventType.Goal)
            .Concat(supporting.AsEnumerable().Reverse())
            .DistinctBy(e => e.Id).Take(MaxSourceEvents).OrderBy(e => e.SequenceNumber).ToList();

        var playerIds = new HashSet<string>();
        if (insight.KeyPlayerId is not null) playerIds.Add(insight.KeyPlayerId);
        foreach (var p in insight.RelatedPlayerIds) playerIds.Add(p);
        foreach (var e in supporting)
        {
            if (e.PlayerId is not null) playerIds.Add(e.PlayerId);
            if (e.RelatedPlayerId is not null) playerIds.Add(e.RelatedPlayerId);
        }
        if (profile.FocusPlayerId is { } fp && TemplateInsightProvider.Involvement(insight, match, fp) > 0) playerIds.Add(fp);

        TeamFact Team(Team t) => new(t.Id, t.Name, t.ShortName);
        TeamFact? TeamOf(string? id) => id is null || (id != match.HomeTeam.Id && id != match.AwayTeam.Id) ? null : Team(match.TeamById(id));
        PlayerFact Player(Player p) => new(p.Id, p.Name, p.TeamId);

        var score = match.ScoreAt(insight.DetectedAtSecond);
        return new InsightContext
        {
            WorkflowExecutionId = workflowExecutionId,
            MatchId = match.Id,
            InsightId = insight.Id,
            InsightType = insight.Type.Code(),
            MatchSecond = insight.DetectedAtSecond,
            MatchTime = MatchClock.Format(insight.DetectedAtSecond),
            DisplayMinute = insight.DisplayMinute,
            HomeTeam = Team(match.HomeTeam),
            AwayTeam = Team(match.AwayTeam),
            Score = new ScoreFact(score.Home, score.Away),
            SubjectTeam = TeamOf(insight.TeamId),
            FocusTeam = TeamOf(profile.FocusTeamId),
            DeterministicMetrics = [.. insight.Evidence.Zip(lines, (e, l) => new MetricFact(
                e.Metric, l.Label, e.Unit.ToString(), Round(e.BaselineValue), Round(e.RecentValue)!.Value, Round(e.Difference), Round(e.Threshold),
                l.Baseline, l.Recent, l.Change, l.Threshold, Window(e.BaselineWindow), Window(e.RecentWindow),
                e.SampleSize, e.BaselineSampleSize, e.MinimumSampleSize, e.SampleRequirementMet, e.Comparison, e.IsPrimary))],
            ConfidenceInputs = new ConfidenceInputs(insight.Confidence.ToString(), Math.Round(insight.ConfidenceScore, 3),
                insight.Evidence.All(e => e.SampleRequirementMet), insight.Severity.ToString()),
            Facts = insight.Facts.ToImmutableSortedDictionary(),
            InterpretationCode = insight.InterpretationCode,
            SupportingEventIds = [.. supporting.Select(e => e.Id)],
            SourceEvents = [.. shown.Select(e => new SourceEventFact(e.Id, e.MatchSecond, MatchClock.Format(e.MatchSecond), e.Type.ToString(),
                e.Outcome.ToString(), e.TeamId, e.PlayerId, match.FindPlayer(e.PlayerId)?.Name, ZoneOf(e)))],
            Players = [.. playerIds.Select(match.FindPlayer).OfType<Player>().Select(Player).OrderBy(p => p.TeamId).ThenBy(p => p.Name)],
            DeterministicNarrative = new AuthoritativeWording(wording.Headline, wording.Fact, wording.Inference, wording.Explanation,
                wording.Ticker, wording.StudioLine, wording.Teaching),
            SelectedAudienceMode = profile.Mode.ToString(),
            Language = lex.Code,
            AccessibilityRequirements = accessibility,
            KnownEventSeconds = match.Events.ToImmutableDictionary(e => e.Id, e => e.MatchSecond),
            AllMatchPlayers = [.. match.HomeTeam.Squad.Concat(match.AwayTeam.Squad).Select(Player)],
            ForeignTeamNames = [.. (foreignTeamNames ?? []).Where(n => n != match.HomeTeam.Name && n != match.HomeTeam.ShortName &&
                n != match.AwayTeam.Name && n != match.AwayTeam.ShortName)],
        };
    }

    private static double? Round(double? v) => v is null ? null : Math.Round(v.Value, 4);

    private static WindowFact? Window(TimeSpanRef? w) => w is null ? null : new(w.FromMinute, w.ToMinute, w.Label);

    /// <summary>"right channel, attacking third" in the acting team's frame of reference.</summary>
    public static string? ZoneOf(MatchEvent e)
    {
        if (!e.HasLocation) return null;
        var channel = Pitch.ChannelOf(e.StartY!.Value).ToString().ToLowerInvariant();
        var third = Pitch.IsInAttackingThird(e.StartX!.Value) ? "attacking third" : Pitch.IsInDefensiveThird(e.StartX.Value) ? "defensive third" : "middle third";
        return Pitch.IsInPenaltyArea(e.StartX.Value, e.StartY.Value) ? $"{channel} channel, inside the penalty area" : $"{channel} channel, {third}";
    }
}
