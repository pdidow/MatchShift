using System.Text.Json;
using MatchShift.Agents.Narrative;
using MatchShift.Agents.Workflow;
using MatchShift.Analytics;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData;
using Microsoft.ML.Tokenizers;

namespace MatchShift.Agents.Tests;

/// <summary>
/// The verifier's structured output must fit its output-token limit, or the JSON is truncated and the workflow falls back
/// unverified (observed live at the old 600-token limit). Token counts use o200k_base, the gpt-4.1-mini tokenizer.
/// </summary>
public class VerifierOutputTests
{
    private static readonly Tokenizer O200k = TiktokenTokenizer.CreateForEncoding("o200k_base");

    // Real verifier wording from hosted runs, used to build maximal-length items (the model writes English, not filler tokens).
    private const string Prose =
        "The narrative states that Westhaven increased their right flank attack share to 90 per cent from 31 per cent in the " +
        "50'-62' window, which matches the deterministic metrics, but the claim about eight key players is not supported by " +
        "insightContext and the spoken summary repeats an approximate minute that does not appear among the source events.";

    private static readonly string[] ProseWords = Prose.Split(' ');

    private static string Words(int n, int offset) => string.Join(' ', Enumerable.Range(0, n).Select(i => ProseWords[(i + offset) % ProseWords.Length]));

    private static string Chars(int n) => string.Concat(Enumerable.Repeat(Prose + " ", 3))[..n];

    /// <summary>A verdict that uses every list to its contracted maximum, with realistic long words and real-format IDs.</summary>
    private static VerificationAgentResult Maximal() => new(
        VerifierDecisions.ApprovedWithChanges,
        [.. Enumerable.Range(0, VerifierLimits.Reasons).Select(i => Words(VerifierLimits.ReasonWords - 1, i * 7))],
        [.. Enumerable.Range(0, VerifierLimits.UnsupportedClaims).Select(i => Words(VerifierLimits.ClaimWords - 1, i * 5 + 3))],
        [.. Enumerable.Range(0, VerifierLimits.Corrections).Select(i => Words(VerifierLimits.CorrectionWords - 1, i * 11 + 1))],
        [.. Enumerable.Range(0, VerifierLimits.EvidenceIds).Select(i => $"MS-FLAGSHIP-001-E{840 + i:D5}")],
        new CorrectedNarrative(Chars(VerifierLimits.HeadlineChars), Chars(VerifierLimits.SummaryChars),
            Chars(VerifierLimits.InferenceChars), Chars(VerifierLimits.SpokenChars)),
        [.. Enumerable.Range(0, VerifierLimits.Limitations).Select(i => Words(VerifierLimits.LimitationWords - 1, i * 13 + 2))]);

    [Fact]
    public void A_maximal_verdict_under_the_contract_fits_the_verifier_output_limit_with_margin()
    {
        var json = JsonSerializer.Serialize(Maximal(), AgentJson.Options);
        var tokens = O200k.CountTokens(json);
        var limit = MatchShiftAgents.Verifier.MaxOutputTokens;
        Assert.True(tokens <= limit * 0.85, $"maximal verdict is {tokens} tokens; limit {limit} leaves too little margin");
        Assert.True(limit > 600, "600 tokens truncated real verdicts");
        Assert.True(limit <= 1200, "keep the verifier allowance modest");
    }

    [Fact]
    public void The_verifier_is_told_the_size_contract()
    {
        var i = MatchShiftAgents.Verifier.Instructions;
        Assert.Contains("at most 3 reasons and 3 corrections", i);
        Assert.Contains("at most 6", i);
        Assert.Contains("at most 2 limitations", i);
        // Strictness is unchanged: the size contract does not replace the unsupported-claim rules.
        Assert.Contains("Treat as unsupported, however plausible", i);
        Assert.Contains("REJECTED otherwise", i);
    }

    [Theory]
    [InlineData("""{"decision":"APPROVED","reasons":["All values match."],"unsupportedClaims":[],"corrections":[],"evidenceIdsChecked":["MS-FLAGSHIP-001-E00842"],"correctedNarrative":null,"limitations":[]}""", VerifierDecisions.Approved)]
    [InlineData("""{"decision":"APPROVED_WITH_CHANGES","reasons":["One count removed."],"unsupportedClaims":["8 key players"],"corrections":["Remove the player count."],"evidenceIdsChecked":[],"correctedNarrative":{"headline":"Westhaven switch right","summary":"Most attacks now go right.","inference":null,"spokenSummary":null},"limitations":[]}""", VerifierDecisions.ApprovedWithChanges)]
    [InlineData("""{"decision":"rejected","reasons":["Unsupported score."],"unsupportedClaims":["3-0"],"corrections":[],"evidenceIdsChecked":[],"correctedNarrative":null,"limitations":[]}""", VerifierDecisions.Rejected)]
    public void Each_decision_parses_and_normalises(string json, string expected)
    {
        var v = TemplateOutputs.Normalize(JsonSerializer.Deserialize<VerificationAgentResult>(json, AgentJson.Options)!);
        Assert.Equal(expected, v.Decision);
        Assert.Contains(v.Decision, VerifierDecisions.All);
        Assert.Equal(expected == VerifierDecisions.ApprovedWithChanges, v.CorrectedNarrative is not null);
    }

    [Fact]
    public void Truncated_json_cannot_be_parsed()
    {
        var json = JsonSerializer.Serialize(Maximal(), AgentJson.Options);
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<VerificationAgentResult>(json[..(json.Length * 2 / 3)], AgentJson.Options));
    }

    [Fact]
    public async Task Truncated_verifier_output_fails_safely_and_is_never_verified()
    {
        var match = SyntheticMatchGenerator.GenerateFlagship().Match;
        var insight = LiveMatchAnalyzer.AnalyzeFull(match).Insights.First(i => i.Type == InsightType.AttackChannelShift);
        var context = InsightContextBuilder.Build(insight, match, new ViewerProfile(), AccessibilityRequirements.All, "wf-test");
        var model = new ScriptedChatClient();
        model.Respond = (role, prompt, _, _) =>
        {
            var good = ScriptedChatClient.Good(role, prompt);
            return Task.FromResult(role == AgentRole.Verification ? good[..(good.Length / 2)] : good); // cut off, as at a token limit
        };
        var runner = new InsightWorkflowRunner(new ChatClientAgentFactory(model, ExecutionMode.LiveFoundry, "microsoft-foundry", "gpt-test"));
        var x = await runner.RunAsync(context, runner.CreateRecorder(context), CancellationToken.None);

        var verifier = x.Steps.First(s => s.Stage == Stages.Verification);
        Assert.Equal(["MalformedOutput", "MalformedOutput"], verifier.AttemptFailures);
        Assert.Equal(WorkflowOutcome.FallbackUnverified, x.Outcome);
        Assert.False(x.Verified);
        Assert.Null(x.Published!.Headline);
        Assert.True(x.AuthoritativeFactsUnchanged);
    }
}
