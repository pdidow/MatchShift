using MatchShift.Core.Domain;

namespace MatchShift.SyntheticData.Simulation;

/// <summary>
/// Everything the simulator needs to produce one match: teams, phases of behaviour,
/// and scripted beats (fixed sequences such as a substitution or a goal).
/// </summary>
public sealed record ScenarioScript
{
    public required string Name { get; init; }
    public required string MatchId { get; init; }
    public required Team Home { get; init; }
    public required Team Away { get; init; }
    public required IReadOnlyList<ScenarioPhase> Phases { get; init; }
    public IReadOnlyList<ScriptedBeat> Beats { get; init; } = [];

    /// <summary>
    /// When false, open-play shots never become goals; only scripted beats score.
    /// Used by the flagship match so the score line tells a fixed story.
    /// </summary>
    public bool AllowRandomGoals { get; init; } = true;

    public string Venue { get; init; } = "Synthetic Stadium";
    public DateOnly Date { get; init; } = new(2026, 3, 14);

    public ScenarioPhase PhaseAt(int matchSecond)
    {
        var minute = matchSecond / 60.0;
        foreach (var phase in Phases)
        {
            if (minute >= phase.StartMinute && minute < phase.EndMinute) return phase;
        }
        return Phases[^1];
    }
}

/// <summary>A fixed sequence of events injected at (or just after) a given match second.</summary>
public sealed record ScriptedBeat(int AtSecond, string Description, Action<ScriptContext> Play);
