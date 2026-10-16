namespace MatchShift.Agents.Agents;

/// <summary>Retains inspectable handoffs when editorial review falls back.</summary>
public sealed class EditorialRejectionException(string message, IReadOnlyList<AgentStep> steps) : Exception(message)
{
    public IReadOnlyList<AgentStep> Steps { get; } = steps.ToArray();
}
