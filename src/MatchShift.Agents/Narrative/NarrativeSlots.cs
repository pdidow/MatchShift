using System.Text.RegularExpressions;
using MatchShift.Core.Insights;

namespace MatchShift.Agents.Narrative;

/// <summary>
/// The named values a template may use. Built ONLY from an insight's structured evidence,
/// facts and the roster - so a rendered sentence can never contain a number that is not
/// already in the evidence.
/// </summary>
public sealed partial class NarrativeSlots
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Values => _values;

    public string? this[string key]
    {
        get => _values.GetValueOrDefault(key);
        set { if (value is not null) _values[key] = value; }
    }

    public static NarrativeSlots ForInsight(Insight insight, MatchRoster roster, Lexicon lex, ViewerProfile? profile = null)
    {
        var s = new NarrativeSlots();
        s["team"] = roster.Team(insight.TeamId);
        s["opponent"] = roster.Opponent(insight.TeamId);
        s["homeTeam"] = roster.Team(roster.HomeTeamId);
        s["awayTeam"] = roster.Team(roster.AwayTeamId);
        s["minute"] = insight.DisplayMinute.ToString(lex.Culture);
        s["confidence"] = lex.Get($"confidence.{insight.Confidence}");
        s["severity"] = lex.Get($"severity.{insight.Severity}");

        foreach (var e in insight.Evidence)
        {
            s[$"{e.Metric}.to"] = lex.Format(e.RecentValue, e.Unit);
            if (e.BaselineValue is { } b)
            {
                s[$"{e.Metric}.from"] = lex.Format(b, e.Unit);
                s[$"{e.Metric}.diff"] = lex.FormatDiff(e.RecentValue - b, e.Unit);
            }
            if (e.Threshold is { } t) s[$"{e.Metric}.threshold"] = lex.Format(Math.Abs(t), e.Unit == MetricUnit.Count ? MetricUnit.Count : e.Unit);
        }

        var p = insight.PrimaryEvidence;
        if (p is not null)
        {
            s["to"] = lex.Format(p.RecentValue, p.Unit);
            if (p.BaselineValue is { } pb)
            {
                s["from"] = lex.Format(pb, p.Unit);
                s["diff"] = lex.FormatDiff(p.RecentValue - pb, p.Unit);
            }
            if (p.RecentWindow is { } rw)
            {
                s["window"] = rw.Label;
                s["windowStart"] = Minute(lex, rw.FromMinute);
                s["recentMinutes"] = Math.Max(1, (int)Math.Round((rw.ToSecond - rw.FromSecond) / 60.0)).ToString(lex.Culture);
            }
            if (p.BaselineWindow is { } bw)
            {
                s["baselineWindow"] = bw.Label;
                s["baselineMinutes"] = Math.Max(1, (int)Math.Round((bw.ToSecond - bw.FromSecond) / 60.0)).ToString(lex.Culture);
            }
        }

        if (insight.Fact("channel") is { } channel)
        {
            s["channelSide"] = lex.ChannelSide(channel);
            s["channelPhrase"] = lex.ChannelPhrase(channel);
        }

        s["player"] = roster.Player(insight.KeyPlayerId);
        foreach (var (key, name) in new[]
                 {
                     ("scorerId", "scorer"), ("assistId", "assister"), ("shooterId", "player"), ("playerId", "player"),
                     ("playerOnId", "substitute"), ("substitutePlayerId", "substitute"), ("playerOffId", "substituteOff"),
                 })
        {
            if (insight.Fact(key) is { Length: > 0 } id) s[name] = roster.Player(id);
        }

        if (insight.Fact("substitutionMinute") is { } subMinute) s["substitutionMinute"] = subMinute;
        if (insight.Fact("homeScore") is { } hs) s["homeScore"] = hs;
        if (insight.Fact("awayScore") is { } aws) s["awayScore"] = aws;
        s["assistPhrase"] = s["assister"] is { Length: > 0 } ? Fill(lex.Get("assistPhrase"), s).Text : "";
        if (insight.Fact("zone") is { } zone)
        {
            s["zonePhrase"] = lex.Get($"zone.{zone}");
            s["zoneShort"] = lex.Get($"zoneShort.{zone}");
        }
        if (insight.Fact("outcome") is { } outcome && lex.Find($"outcome.{outcome}") is not null)
        {
            s["outcomePhrase"] = lex.Get($"outcome.{outcome}");
            s["outcomeShort"] = lex.Get($"outcomeShort.{outcome}");
        }

        if (profile?.FocusTeamId is { } focusTeam && roster.IsKnownTeam(focusTeam)) s["focusTeam"] = roster.Team(focusTeam);
        if (profile?.FocusPlayerId is { } focusPlayer && roster.Player(focusPlayer) is { Length: > 0 } fp) s["focusPlayer"] = fp;
        return s;
    }

    public static string Minute(Lexicon lex, int minute) =>
        lex.Code switch
        {
            "fr" => $"{minute}e minute",
            "es" => $"minuto {minute}",
            "de" => $"{minute}. Minute",
            "it" => $"{minute}° minuto",
            "pt" => $"minuto {minute}",
            _ => $"{minute}'",
        };

    /// <summary>Fill <c>{slot}</c> placeholders; reports any that could not be resolved.</summary>
    public static (string Text, IReadOnlyList<string> Missing) Fill(string template, NarrativeSlots slots)
    {
        var missing = new List<string>();
        var text = SlotPattern().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            if (slots._values.TryGetValue(key, out var v)) return v;
            missing.Add(key);
            return m.Value;
        });
        return (text, missing);
    }

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9_.]*)\}")]
    private static partial Regex SlotPattern();
}
