using MatchShift.Core.Domain;
using MatchShift.SyntheticData.Scenarios;

namespace MatchShift.SyntheticData;

/// <summary>
/// Entry point for all synthetic football data in MatchShift.
/// Every match produced here is fictional and fully reproducible from its seed.
/// </summary>
public static class SyntheticMatchGenerator
{
    /// <summary>A seeded match with stable, randomly drawn team behaviour and random goals.</summary>
    public static Match GenerateMatch(long seed) => ScenarioLibrary.Generate(ScenarioKind.Stationary, seed).Match;

    /// <summary>The deterministic flagship demo match.</summary>
    public static LabelledMatch GenerateFlagship() => FlagshipScenario.Generate();

    public static LabelledMatch GenerateScenario(ScenarioKind kind, long seed) => ScenarioLibrary.Generate(kind, seed);

    /// <summary>
    /// Batch generation for testing, evaluation and analytics. Seeds are
    /// <c>seedStart .. seedStart + matchCount - 1</c>; the scenario of each match is
    /// chosen deterministically from its seed using <paramref name="mix"/> weights.
    /// </summary>
    public static SyntheticDataset GenerateDataset(int matchCount, long seedStart, ScenarioMix? mix = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(matchCount);
        mix ??= ScenarioMix.Default;
        var kinds = mix.Weights.Keys.ToList();
        var weights = kinds.Select(k => mix.Weights[k]).ToList();

        var matches = new List<LabelledMatch>(matchCount);
        for (var i = 0; i < matchCount; i++)
        {
            var seed = seedStart + i;
            var picker = new DeterministicRandom(seed * 7919 + 17);
            var kind = kinds[picker.PickWeighted(weights)];
            matches.Add(ScenarioLibrary.Generate(kind, seed));
        }

        return new SyntheticDataset(
            Name: $"matchshift-synthetic-{seedStart}-{matchCount}",
            SeedStart: seedStart,
            Mix: mix,
            Matches: matches);
    }
}

public sealed record ScenarioMix(IReadOnlyDictionary<ScenarioKind, double> Weights)
{
    public static ScenarioMix Default { get; } = new(new Dictionary<ScenarioKind, double>
    {
        [ScenarioKind.Stationary] = 0.25,
        [ScenarioKind.AttackChannelShift] = 0.2,
        [ScenarioKind.SustainedPressure] = 0.15,
        [ScenarioKind.CounterAttackSwing] = 0.15,
        [ScenarioKind.ChaoticFinish] = 0.1,
        [ScenarioKind.SubstitutionImpact] = 0.15,
    });

    public static ScenarioMix Only(ScenarioKind kind) => new(new Dictionary<ScenarioKind, double> { [kind] = 1 });
}

public sealed record SyntheticDataset(
    string Name,
    long SeedStart,
    ScenarioMix Mix,
    IReadOnlyList<LabelledMatch> Matches);
