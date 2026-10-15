using System.Text.Json.Serialization;

namespace MatchShift.Core.Insights;

/// <summary>
/// Every kind of finding MatchShift can produce. Shift types compare windows of
/// play; moment types are anchored to a single event sequence.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<InsightType>))]
public enum InsightType
{
    [JsonStringEnumMemberName("ATTACK_CHANNEL_SHIFT")] AttackChannelShift,
    [JsonStringEnumMemberName("PRESSURE_INCREASE")] PressureIncrease,
    [JsonStringEnumMemberName("PRESSURE_DECREASE")] PressureDecrease,
    [JsonStringEnumMemberName("TERRITORIAL_SHIFT")] TerritorialShift,
    [JsonStringEnumMemberName("POSSESSION_SHIFT")] PossessionShift,
    [JsonStringEnumMemberName("TEMPO_SHIFT")] TempoShift,
    [JsonStringEnumMemberName("CONTROL_SHIFT")] ControlShift,
    [JsonStringEnumMemberName("CHAOS_INCREASE")] ChaosIncrease,
    [JsonStringEnumMemberName("SHOT_PRESSURE")] ShotPressure,
    [JsonStringEnumMemberName("TURNOVER_SPIKE")] TurnoverSpike,
    [JsonStringEnumMemberName("POST_SUBSTITUTION_CHANGE")] PostSubstitutionChange,
    [JsonStringEnumMemberName("COUNTER_ATTACK_THREAT")] CounterAttackThreat,
    [JsonStringEnumMemberName("DANGEROUS_TURNOVER")] DangerousTurnover,
    [JsonStringEnumMemberName("BIG_CHANCE")] BigChance,
    [JsonStringEnumMemberName("GOAL")] Goal,
}

public static class InsightTypeExtensions
{
    public static bool IsMoment(this InsightType type) =>
        type is InsightType.DangerousTurnover or InsightType.BigChance or InsightType.Goal;

    public static bool IsShift(this InsightType type) => !type.IsMoment();

    public static string Code(this InsightType type) => type switch
    {
        InsightType.AttackChannelShift => "ATTACK_CHANNEL_SHIFT",
        InsightType.PressureIncrease => "PRESSURE_INCREASE",
        InsightType.PressureDecrease => "PRESSURE_DECREASE",
        InsightType.TerritorialShift => "TERRITORIAL_SHIFT",
        InsightType.PossessionShift => "POSSESSION_SHIFT",
        InsightType.TempoShift => "TEMPO_SHIFT",
        InsightType.ControlShift => "CONTROL_SHIFT",
        InsightType.ChaosIncrease => "CHAOS_INCREASE",
        InsightType.ShotPressure => "SHOT_PRESSURE",
        InsightType.TurnoverSpike => "TURNOVER_SPIKE",
        InsightType.PostSubstitutionChange => "POST_SUBSTITUTION_CHANGE",
        InsightType.CounterAttackThreat => "COUNTER_ATTACK_THREAT",
        InsightType.DangerousTurnover => "DANGEROUS_TURNOVER",
        InsightType.BigChance => "BIG_CHANCE",
        InsightType.Goal => "GOAL",
        _ => type.ToString(),
    };
}
