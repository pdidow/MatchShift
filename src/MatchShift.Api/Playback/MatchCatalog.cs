using MatchShift.Core.Domain;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Scenarios;

namespace MatchShift.Api.Playback;

/// <summary>The synthetic matches the demo can play. Generated deterministically on first use.</summary>
public sealed class MatchCatalog
{
    private readonly Lazy<IReadOnlyList<LabelledMatch>> _matches = new(() =>
    [
        SyntheticMatchGenerator.GenerateFlagship(),
        SyntheticMatchGenerator.GenerateScenario(ScenarioKind.SustainedPressure, 4101),
        SyntheticMatchGenerator.GenerateScenario(ScenarioKind.AttackChannelShift, 6035),
        SyntheticMatchGenerator.GenerateScenario(ScenarioKind.ChaoticFinish, 9502),
        SyntheticMatchGenerator.GenerateScenario(ScenarioKind.Stationary, 12345),
        SyntheticMatchGenerator.GenerateScenario(ScenarioKind.PassingStyleShift, 7001),
    ]);

    public IReadOnlyList<LabelledMatch> All => _matches.Value;

    public Match Default => All[0].Match;

    public LabelledMatch? Find(string id) => All.FirstOrDefault(m => string.Equals(m.Match.Id, id, StringComparison.OrdinalIgnoreCase));
}
