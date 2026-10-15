using System.Text.Json.Serialization;

namespace MatchShift.Agents.Narrative;

/// <summary>How the same verified intelligence should be presented.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ViewerMode>))]
public enum ViewerMode
{
    /// <summary>New to football: plain language plus a short explanation of the concept.</summary>
    TeachMe,

    /// <summary>Minimal noise: only the most important insights, one sentence each.</summary>
    WhatMatters,

    /// <summary>Deeper metrics, every evidence line, statistical strength.</summary>
    Analyst,

    /// <summary>Prioritise a chosen club and/or player and frame insights from their point of view.</summary>
    MyTeam,

    /// <summary>Broadcast / studio: concise talking points and on-air lines.</summary>
    Studio
}

[JsonConverter(typeof(JsonStringEnumConverter<RecapStyle>))]
public enum RecapStyle
{
    Casual,
    Analyst,
    Broadcast,
    Social
}

public sealed record ViewerProfile(
    ViewerMode Mode = ViewerMode.Analyst,
    string Language = "en",
    string? FocusTeamId = null,
    string? FocusPlayerId = null)
{
    public static ViewerProfile Default { get; } = new();
}
