using MatchShift.Core.Domain;
using MatchShift.SyntheticData;
using MatchShift.SyntheticData.Scenarios;

namespace MatchShift.SyntheticData.Tests;

public class ReproducibilityTests
{
    [Fact]
    public void Same_seed_produces_identical_match()
    {
        var a = SyntheticMatchGenerator.GenerateMatch(12345);
        var b = SyntheticMatchGenerator.GenerateMatch(12345);

        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.HomeTeam.Id, b.HomeTeam.Id);
        Assert.Equal(a.AwayTeam.Id, b.AwayTeam.Id);
        Assert.Equal(a.Events, b.Events);
    }

    [Fact]
    public void Different_seeds_produce_different_matches()
    {
        var a = SyntheticMatchGenerator.GenerateMatch(1);
        var b = SyntheticMatchGenerator.GenerateMatch(2);

        Assert.NotEqual(
            a.Events.Select(e => (e.Type, e.MatchSecond, e.StartX)),
            b.Events.Select(e => (e.Type, e.MatchSecond, e.StartX)));
    }

    [Fact]
    public void Flagship_match_is_deterministic()
    {
        var a = SyntheticMatchGenerator.GenerateFlagship().Match;
        var b = SyntheticMatchGenerator.GenerateFlagship().Match;
        Assert.Equal(a.Events, b.Events);
    }

    [Theory]
    [InlineData(ScenarioKind.Stationary)]
    [InlineData(ScenarioKind.AttackChannelShift)]
    [InlineData(ScenarioKind.SustainedPressure)]
    [InlineData(ScenarioKind.CounterAttackSwing)]
    [InlineData(ScenarioKind.ChaoticFinish)]
    [InlineData(ScenarioKind.SubstitutionImpact)]
    public void Every_scenario_kind_is_reproducible(ScenarioKind kind)
    {
        var a = SyntheticMatchGenerator.GenerateScenario(kind, 777);
        var b = SyntheticMatchGenerator.GenerateScenario(kind, 777);
        Assert.Equal(a.Match.Events, b.Match.Events);
        Assert.Equal(a.GroundTruth.ExpectedPatterns, b.GroundTruth.ExpectedPatterns);
    }

    [Fact]
    public void Dataset_generation_is_reproducible_and_uses_seed_range()
    {
        var a = SyntheticMatchGenerator.GenerateDataset(6, 1000);
        var b = SyntheticMatchGenerator.GenerateDataset(6, 1000);

        Assert.Equal(6, a.Matches.Count);
        Assert.Equal(Enumerable.Range(1000, 6).Select(i => (long)i), a.Matches.Select(m => m.Match.Seed));
        Assert.Equal(a.Matches.Select(m => m.Match.Id), b.Matches.Select(m => m.Match.Id));
        Assert.Equal(a.Matches.Select(m => m.Match.Events.Count), b.Matches.Select(m => m.Match.Events.Count));
    }

    [Fact]
    public void Prng_sequence_is_pinned_so_seeds_are_stable_across_runtimes()
    {
        var rng = new DeterministicRandom(42);
        var first = Enumerable.Range(0, 3).Select(_ => rng.NextULong()).ToArray();

        var again = new DeterministicRandom(42);
        Assert.Equal(first, Enumerable.Range(0, 3).Select(_ => again.NextULong()));

        // Golden values: if these change, every published seed changes meaning.
        Assert.Equal(PinnedValues, first);
    }

    private static readonly ulong[] PinnedValues = PrngGolden.Seed42;
}
