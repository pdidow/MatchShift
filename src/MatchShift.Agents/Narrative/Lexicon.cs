using System.Globalization;
using MatchShift.Core.Insights;

namespace MatchShift.Agents.Narrative;

/// <summary>
/// All language-specific wording and number formatting for one language. Templates use
/// <c>{slot}</c> placeholders filled from <see cref="NarrativeSlots"/>; they never contain
/// numbers of their own, so every number a viewer reads comes from structured evidence.
///
/// Template keys:
///   headline|fact|inference|teach|ticker|studio . TYPE_CODE [. variant]
///   explain . interpretation_code
///   metric . MetricKey            (labels for WHY? panels)
///   recap . *, ui . *, perspective . *, ...
/// Lookup falls back from the most specific key ("headline.GOAL.goal_equaliser")
/// to the general one ("headline.GOAL").
/// </summary>
public abstract class Lexicon
{
    protected Lexicon(string code, string name, CultureInfo culture)
    {
        Code = code;
        Name = name;
        Culture = culture;
    }

    public string Code { get; }
    public string Name { get; }
    public CultureInfo Culture { get; }

    protected abstract IReadOnlyDictionary<string, string> Templates { get; }

    public string? Find(string key, string? variant = null)
    {
        if (variant is not null && Templates.TryGetValue($"{key}.{variant}", out var specific)) return specific;
        return Templates.TryGetValue(key, out var general) ? general : null;
    }

    public string Get(string key, string? variant = null) =>
        Find(key, variant) ?? throw new KeyNotFoundException($"[{Code}] missing template '{key}'{(variant is null ? "" : $" / variant '{variant}'")}");

    public IEnumerable<string> Keys => Templates.Keys;

    /// <summary>Format a value for reading. Shares become whole percentages.</summary>
    public virtual string Format(double value, MetricUnit unit) => unit switch
    {
        MetricUnit.Share => Percent(value),
        MetricUnit.Count => Math.Round(value).ToString("0", Culture),
        MetricUnit.Index => Math.Round(value).ToString("0", Culture),
        MetricUnit.PerMinute => value.ToString("0.0", Culture),
        MetricUnit.Seconds => Math.Round(value).ToString("0", Culture),
        MetricUnit.Quality => value.ToString("0.00", Culture),
        MetricUnit.Statistic => value.ToString("0.0", Culture),
        _ => value.ToString("0.##", Culture),
    };

    /// <summary>Format a change. Shares are expressed in percentage points.</summary>
    public virtual string FormatDiff(double diff, MetricUnit unit)
    {
        var sign = diff >= 0 ? "+" : "−";
        var magnitude = Math.Abs(diff);
        return unit == MetricUnit.Share
            ? $"{sign}{Math.Round(magnitude * 100).ToString("0", Culture)} {PercentagePoints}"
            : $"{sign}{Format(magnitude, unit)}";
    }

    protected abstract string PercentagePoints { get; }

    protected virtual string Percent(double share) => $"{Math.Round(share * 100).ToString("0", Culture)}%";

    /// <summary>Lower-case channel adjective: "right", "droite", "derecha".</summary>
    public string ChannelSide(string channel) => Get($"channel.{channel}");

    /// <summary>Directional phrase: "down the right", "sur la droite", "por la derecha".</summary>
    public string ChannelPhrase(string channel) => Get($"channelPhrase.{channel}");
}
