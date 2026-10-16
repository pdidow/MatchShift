using System.Diagnostics;
using System.Text.Json;
using MatchShift.Agents.Agents;
using MatchShift.Agents.Narrative;
using MatchShift.Core.Insights;

namespace MatchShift.Infrastructure.Foundry;

/// <summary>Evidence-bound editorial agents. A model selects treatments and recap sections;
/// deterministic renderers retain responsibility for all viewer-facing factual sentences.</summary>
public sealed class FoundryInsightProvider(IChatCompletionClient client, string name = "microsoft-foundry", bool collaborative = true) : IAiInsightProvider
{
    public const string PromptVersion = "evidence-editor-v2";
    public string Name => name;

    public async Task<InsightNarrative> NarrateInsightAsync(Insight insight, NarrativeContext context, CancellationToken cancellationToken = default)
    {
        var authoritative = NarrativeSelection.For(insight);
        var template = TemplateInsightProvider.Narrate(insight, context);
        var steps = new List<AgentStep>();
        async Task<T> Ask<T>(string role, object input, string instructions)
        {
            var sw = Stopwatch.StartNew();
            var json = await client.CompleteJsonAsync(
                $"MatchShift {role}. {PromptVersion}. {instructions} Return JSON only. Never invent claims, names or causes.",
                JsonSerializer.Serialize(input, FoundryChatClient.Json), cancellationToken);
            var result = JsonSerializer.Deserialize<T>(json, FoundryChatClient.Json)
                ?? throw new InvalidDataException($"Missing {role} response.");
            steps.Add(new AgentStep(role, instructions, "Typed evidence-bound handoff", "Response received; application validation follows",
                AgentStatus.Completed, sw.ElapsedMilliseconds));
            return result;
        }
        var draft = await Ask<Proposal>("Interpreter", new { claim = authoritative, language = context.Profile.Language,
            mode = context.Profile.Mode.ToString(), catalogue = template },
            "Propose {selection: claim}. Preserve every reference/value. Choose treatment analysis, context or brief for this audience.");
        if (collaborative)
        {
            var accepted = false;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var review = await Ask<Review>("Skeptic", new { claim = authoritative, proposal = draft, attempt },
                    "Independently compare proposal.selection with claim. Reject wrong attribution, numbers, windows or event references. Return {approved:bool,issues:[reason codes]}.");
                accepted = review.Approved && draft.Selection?.Matches(insight) == true;
                steps[^1] = steps[^1] with { Status = accepted ? AgentStatus.Approved : AgentStatus.Rejected,
                    Output = accepted ? "Evidence references accepted" : "Revision requested: unsupported evidence binding or skeptical review" };
                if (accepted) break;
                if (attempt == 0)
                    draft = await Ask<Proposal>("Interpreter revision", new { claim = authoritative, rejected = draft, feedback = review.Issues },
                        "Repair the rejected selection using only claim. Return {selection: claim}; treatment may be analysis, context or brief.");
            }
            if (!accepted) throw new EditorialRejectionException("Skeptic rejected the bounded revision. Verified template required.", steps);
            draft = await Ask<Proposal>("Editor", new { claim = authoritative, accepted = draft, mode = context.Profile.Mode.ToString() },
                "Return {selection: accepted.selection}; choose the final audience treatment without altering evidence references.");
        }
        if (draft.Selection is null || !draft.Selection.Matches(insight))
        {
            steps.Add(new AgentStep("Evidence binding gate", "Validate final editorial selection", "Typed selection",
                "Unsupported claim withheld", AgentStatus.Rejected, 0));
            throw new EditorialRejectionException("Unsupported editorial claim.", steps);
        }
        return draft.Selection.Render(insight, context, Name) with { Collaboration = steps };
    }

    public async Task<RecapNarrative> NarrateRecapAsync(RecapFacts recap, RecapStyle style, NarrativeContext context,
        IReadOnlyList<Insight> insights, CancellationToken cancellationToken = default)
    {
        var template = RecapNarrator.Narrate(recap, style, context, insights);
        var json = await client.CompleteJsonAsync("MatchShift Recap Editor. Order the supplied sections into an engaging recap. " +
            "Keep every section exactly once. Return JSON {sectionOrder:[zero-based indices]}. No prose.",
            JsonSerializer.Serialize(new { sections = template.Sections, style = style.ToString(), language = context.Profile.Language }, FoundryChatClient.Json), cancellationToken);
        var order = JsonSerializer.Deserialize<RecapOrder>(json, FoundryChatClient.Json)?.SectionOrder;
        if (order is null || !order.Order().SequenceEqual(Enumerable.Range(0, template.Sections.Count)))
            throw new InvalidDataException("Invalid recap section references.");
        return template with { Sections = order.Select(i => template.Sections[i]).ToArray(), Source = Name };
    }

    private sealed record Proposal(NarrativeSelection? Selection);
    private sealed record Review(bool Approved, IReadOnlyList<string> Issues);
    private sealed record RecapOrder(IReadOnlyList<int> SectionOrder);
}
