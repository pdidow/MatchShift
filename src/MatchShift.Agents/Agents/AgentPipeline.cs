using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using MatchShift.Agents.Narrative;
using MatchShift.Analytics.Indices;
using MatchShift.Analytics.Metrics;
using MatchShift.Analytics.State;
using MatchShift.Core.Domain;
using MatchShift.Core.Insights;

namespace MatchShift.Agents.Agents;

[JsonConverter(typeof(JsonStringEnumConverter<AgentStatus>))]
public enum AgentStatus
{
    Completed,
    Approved,
    Rejected,
    Fallback,
    Skipped
}

public sealed record AgentStep(string Agent, string Responsibility, string Input, string Output, AgentStatus Status, long DurationMs);

public sealed record AgentTrace(string InsightId, string Provider, IReadOnlyList<AgentStep> Steps, InsightNarrative Narrative, EvidenceAudit? EvidenceAudit = null);

/// <summary>A factual finding, re-derived from raw events.</summary>
public sealed record VerifiedFact(string Metric, double Cited, double Recomputed, bool Matches);

/// <summary>
/// The MatchShift multi-agent pipeline for one insight. Every agent has one responsibility,
/// an explicit input and output, and a defined failure behaviour:
///
/// 1. Event Analyst      - events -&gt; re-derived supported metrics. Full replay and these diagnostics
///                        must agree before narration; inconsistent evidence is withheld.
/// 2. Pattern Detection  - detector output -&gt; structured pattern (type, team, severity, confidence, strength).
/// 3. Football Reasoning - verified facts + pattern -&gt; cautious interpretation, from an allow-list per pattern.
///                         Fails safe: an interpretation not allowed for the pattern is withheld.
/// 4. Storytelling       - facts + interpretation -&gt; viewer-facing words (template or Microsoft Foundry).
/// 5. Personalization    - profile -&gt; mode, language, perspective, visibility.
/// 6. Verification       - every sentence -&gt; approved / rejected. Rejected AI text falls back to verified templates.
/// (The Recap agent runs the same verification over recap facts - see <see cref="NarrationService"/>.)
/// </summary>
public sealed class AgentPipeline(NarrationService narration, VerificationAgent verifier)
{
    private static readonly IReadOnlyDictionary<InsightType, string[]> AllowedInterpretations = new Dictionary<InsightType, string[]>
    {
        [InsightType.AttackChannelShift] = ["channel_overload", "channel_after_substitution"],
        [InsightType.PostSubstitutionChange] = ["substitution_channel", "substitution_pressure", "substitution_possession"],
        [InsightType.PressureIncrease] = ["chasing_game", "pressing_lead", "seizing_momentum"],
        [InsightType.PressureDecrease] = ["protecting_lead", "pushed_back"],
        [InsightType.ControlShift] = ["control_swing", "control_gained"],
        [InsightType.ChaosIncrease] = ["open_end_to_end"],
        [InsightType.PossessionShift] = ["keeping_the_ball"],
        [InsightType.TerritorialShift] = ["pinning_back"],
        [InsightType.TempoShift] = ["raising_tempo", "managing_game", "slowing_down"],
        [InsightType.ShotPressure] = ["goal_threat_building"],
        [InsightType.TurnoverSpike] = ["pressed_into_errors", "loose_in_possession"],
        [InsightType.CounterAttackThreat] = ["counter_while_opponent_dominates", "exploiting_transitions"],
        [InsightType.Goal] = ["goal_after_pressure", "goal_on_counter", "goal_from_set_piece", "goal_open_play"],
        [InsightType.BigChance] = ["big_chance_saved", "big_chance_missed"],
        [InsightType.DangerousTurnover] = ["costly_pass", "caught_in_possession"],
    };

    public async Task<AgentTrace> RunAsync(Insight insight, Match match, NarrativeContext context, CancellationToken ct = default)
    {
        var steps = new List<AgentStep>();
        EvidenceGate.Require(insight, match);

        // 1. Event Analyst
        var sw = Stopwatch.StartNew();
        var facts = EventAnalyst(insight, match);
        var mismatches = facts.Count(f => !f.Matches);
        if (mismatches > 0) throw new InvalidDataException("Claim withheld: raw-event metric recomputation failed.");
        steps.Add(new AgentStep("Event Analyst Agent", "Re-derives every cited metric from the raw synthetic events",
            $"{insight.SupportingEventIds.Count} supporting events, {insight.Evidence.Count} evidence items",
            facts.Count == 0
                ? "No windowed metrics to re-derive (event-anchored moment)"
                : string.Join("; ", facts.Select(f => $"{f.Metric}: cited {Fmt(f.Cited)} = recomputed {Fmt(f.Recomputed)}{(f.Matches ? " ✓" : " ✗")}")),
            mismatches == 0 ? AgentStatus.Completed : AgentStatus.Rejected, sw.ElapsedMilliseconds));

        // 2. Pattern Detection
        sw.Restart();
        var primary = insight.PrimaryEvidence;
        var strength = insight.Evidence.FirstOrDefault(e => e.Metric.EndsWith("Z", StringComparison.Ordinal) || e.Metric.EndsWith("Ratio", StringComparison.Ordinal));
        steps.Add(new AgentStep("Pattern Detection Agent", "Turns rolling metrics into a structured, graded pattern",
            primary is null ? insight.Type.Code() : $"{primary.Metric} {Fmt(primary.BaselineValue)} → {Fmt(primary.RecentValue)} (threshold {Fmt(primary.Threshold)})",
            $"{insight.Type.Code()} · team {insight.TeamId ?? "both"} · severity {insight.Severity} · confidence {insight.Confidence} ({insight.ConfidenceScore:0.00})" +
            (strength is null ? "" : $" · {strength.Metric} {Fmt(strength.RecentValue)}"),
            AgentStatus.Completed, sw.ElapsedMilliseconds));

        // 3. Football Reasoning
        sw.Restart();
        var allowed = insight.InterpretationCode is not null &&
                      AllowedInterpretations.TryGetValue(insight.Type, out var codes) && codes.Contains(insight.InterpretationCode);
        var reasoned = allowed ? insight : insight with { InterpretationCode = null };
        steps.Add(new AgentStep("Football Reasoning Agent", "Adds a cautious interpretation - never a claim of intent",
            $"verified facts + pattern {insight.Type.Code()}",
            allowed ? $"interpretation '{insight.InterpretationCode}' (hedged: may / might / could)" : "no interpretation offered",
            allowed || insight.InterpretationCode is null ? AgentStatus.Completed : AgentStatus.Rejected, sw.ElapsedMilliseconds));

        // 4 + 5. Storytelling and Personalization
        sw.Restart();
        var narrative = await narration.InsightAsync(reasoned, context, ct);
        var storyStatus = narrative.VerificationNote?.Contains("Template used", StringComparison.Ordinal) == true ? AgentStatus.Fallback : AgentStatus.Completed;
        steps.Add(new AgentStep("Storytelling Agent", "Writes the viewer-facing narrative from facts + interpretation",
            $"provider: {narration.ProviderName}",
            $"\"{narrative.Headline}\"" + (narrative.VerificationNote is null ? "" : $" ({narrative.VerificationNote})"),
            storyStatus, sw.ElapsedMilliseconds));

        sw.Restart();
        var visible = NarrativeFilter.Select([insight], context.Profile).Count > 0;
        steps.Add(new AgentStep("Personalization Agent", "Adapts wording, language and priority to the viewer",
            $"mode {context.Profile.Mode} · language {context.Profile.Language}" +
            (context.Profile.FocusTeamId is null ? "" : $" · follows {context.Roster.Team(context.Profile.FocusTeamId)}"),
            (visible ? "shown in this mode" : "filtered out in this mode") +
            (narrative.Teaching is null ? "" : " · adds Football 101 explanation") +
            (narrative.Perspective is null ? "" : $" · \"{narrative.Perspective}\""),
            visible ? AgentStatus.Completed : AgentStatus.Skipped, sw.ElapsedMilliseconds));

        // 6. Verification
        sw.Restart();
        var slots = NarrativeSlots.ForInsight(reasoned, context.Roster, LexiconRegistry.Get(context.Profile.Language), context.Profile);
        var allowedNumbers = VerificationAgent.AllowedNumbers(reasoned, slots).ToList();
        if (context.Profile.FocusPlayerId is { } fp) allowedNumbers.Add(TemplateInsightProvider.Involvement(reasoned, match, fp));
        var verification = verifier.Verify(narrative.Sentences(), allowedNumbers);
        var final = narrative;
        if (!verification.Approved || mismatches > 0)
        {
            final = TemplateInsightProvider.Narrate(reasoned, context) with { VerificationNote = "Replaced by verified template text." };
        }
        steps.Add(new AgentStep("Verification Agent", "Checks every number and claim against structured evidence",
            $"{narrative.Sentences().Count()} sentences",
            verification.Approved
                ? $"approved - evidence binding verified, {verification.NumbersChecked} numeric occurrences checked"
                : $"rejected: {string.Join("; ", verification.Issues.Take(2).Select(i => i.Problem))}",
            verification.Approved && mismatches == 0 ? AgentStatus.Approved : AgentStatus.Rejected, sw.ElapsedMilliseconds));

        steps.InsertRange(3, narrative.Collaboration);
        return new AgentTrace(insight.Id, narration.ProviderName, steps, final, EvidenceGate.Check(insight, match));
    }

    /// <summary>
    /// Re-derive windowed metrics from the events that existed when the insight was raised.
    /// Only metrics with a known, deterministic recomputation are checked.
    /// </summary>
    public static IReadOnlyList<VerifiedFact> EventAnalyst(Insight insight, Match match)
    {
        var state = MatchState.FromEvents(MatchContext.From(match), match.Events.Where(e => e.MatchSecond < insight.DetectedAtSecond));
        var facts = new List<VerifiedFact>();
        foreach (var e in insight.Evidence)
        {
            if (e.RecentWindow is null) continue;
            var recent = Recompute(state, e.Metric, insight.TeamId, e.RecentWindow);
            if (recent is null) continue;
            facts.Add(new VerifiedFact(e.Metric, e.RecentValue, recent.Value, Close(e.RecentValue, recent.Value, e.Unit)));
            if (e is { BaselineWindow: not null, BaselineValue: { } b })
            {
                var baseline = Recompute(state, e.Metric, insight.TeamId, e.BaselineWindow);
                if (baseline is not null) facts.Add(new VerifiedFact($"{e.Metric} (baseline)", b, baseline.Value, Close(b, baseline.Value, e.Unit)));
            }
        }
        return facts;
    }

    private static double? Recompute(MatchState state, string metric, string? teamId, TimeSpanRef window)
    {
        var w = MetricsCalculator.Compute(state, new AnalysisWindow(window.FromSecond, window.ToSecond));
        if (metric == "ChaosIndex") return ChaosIndex.Compute(w).Value;
        if (teamId is null) return null;
        var m = w.For(teamId);
        return metric switch
        {
            "LeftAttackShare" => m.LeftShare,
            "CentreAttackShare" => m.CentreShare,
            "RightAttackShare" => m.RightShare,
            "PressureIndex" => PressureIndex.Compute(m).Value,
            "MatchControl" => MatchControl.Compute(w).ControlFor(teamId),
            "PossessionShare" => m.PossessionShare,
            "FieldTilt" => m.FieldTilt,
            "Shots" => m.Shots,
            "Turnovers" => m.Turnovers,
            "FastTransitions" => m.FastTransitions,
            "LongBallShare" => m.PassesAttempted == 0 ? 0 : (double)m.LongBalls / m.PassesAttempted,
            _ => null,
        };
    }

    private static bool Close(double cited, double recomputed, MetricUnit unit) => unit switch
    {
        MetricUnit.Share => Math.Abs(cited - recomputed) <= 0.0015,
        MetricUnit.Index => Math.Abs(cited - recomputed) <= 0.15,
        _ => Math.Abs(cited - recomputed) <= 0.5,
    };

    private static string Fmt(double? v) => v is null ? "-" : v.Value.ToString("0.###", CultureInfo.InvariantCulture);
}
