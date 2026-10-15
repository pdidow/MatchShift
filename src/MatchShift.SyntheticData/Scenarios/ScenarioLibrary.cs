using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData.Clubs;
using MatchShift.SyntheticData.Simulation;

namespace MatchShift.SyntheticData.Scenarios;

public enum ScenarioKind
{
    /// <summary>Stable behaviour for 90 minutes. Negative control: no tactical shifts should fire.</summary>
    Stationary,
    AttackChannelShift,
    SustainedPressure,
    CounterAttackSwing,
    ChaoticFinish,
    SubstitutionImpact,
}

/// <summary>
/// Parameterised, seeded scenario templates. Each produces a labelled match:
/// the events are simulated from behaviour, and the label records what the
/// behaviour change was designed to cause.
/// </summary>
public static class ScenarioLibrary
{
    public static LabelledMatch Generate(ScenarioKind kind, long seed)
    {
        var rng = new DeterministicRandom(seed ^ 0x5EED_CAFE);
        var (home, away) = PickFixture(rng);
        var matchId = $"MS-{Abbrev(kind)}-{seed}";

        var homeBase = RandomBase(rng);
        var awayBase = RandomBase(rng);
        var phases = new List<ScenarioPhase>();
        var beats = RandomSubstitutions(rng, home, away);
        var expected = new List<ExpectedPattern>();
        var forbidden = new List<InsightType>();

        switch (kind)
        {
            case ScenarioKind.Stationary:
                phases.Add(new ScenarioPhase(0, 90, "Stable", homeBase, awayBase));
                forbidden.AddRange([InsightType.AttackChannelShift, InsightType.PostSubstitutionChange]);
                break;

            case ScenarioKind.AttackChannelShift:
            {
                var side = rng.Chance(0.5) ? TeamSide.Home : TeamSide.Away;
                var start = rng.Next(25, 62);
                var length = rng.Next(14, 20);
                var toRight = rng.Chance(0.5);
                var shifted = new ChannelWeights(toRight ? 0.05 : 0.83, 0.12, toRight ? 0.83 : 0.05);
                phases.Add(new ScenarioPhase(0, start, "Baseline", homeBase, awayBase));
                phases.Add(side == TeamSide.Home
                    ? new ScenarioPhase(start, start + length, "Channel shift", Commit(homeBase, shifted), awayBase)
                    : new ScenarioPhase(start, start + length, "Channel shift", homeBase, Commit(awayBase, shifted)));
                phases.Add(new ScenarioPhase(start + length, 90, "Baseline", homeBase, awayBase));
                expected.Add(new ExpectedPattern(InsightType.AttackChannelShift, TeamOf(side, home, away).Id,
                    start + 3, start + length, toRight ? "Shift to the right" : "Shift to the left"));
                break;
            }

            case ScenarioKind.SustainedPressure:
            {
                var side = rng.Chance(0.5) ? TeamSide.Home : TeamSide.Away;
                var start = rng.Next(20, 65);
                var dominant = new TeamBehaviour { Control = 0.74, PassAccuracy = 0.86, Progression = 0.66, Press = 0.85, ShotPropensity = 1.7 };
                var pinned = new TeamBehaviour { Control = 0.30, PassAccuracy = 0.72, Progression = 0.32, Press = 0.32, Directness = 0.3 };
                phases.Add(new ScenarioPhase(0, start, "Baseline", homeBase, awayBase));
                phases.Add(side == TeamSide.Home
                    ? new ScenarioPhase(start, start + 12, "Pressure", dominant, pinned)
                    : new ScenarioPhase(start, start + 12, "Pressure", pinned, dominant));
                phases.Add(new ScenarioPhase(start + 12, 90, "Baseline", homeBase, awayBase));
                expected.Add(new ExpectedPattern(InsightType.PressureIncrease, TeamOf(side, home, away).Id,
                    start + 1, start + 12, "Sustained pressure spell"));
                break;
            }

            case ScenarioKind.CounterAttackSwing:
            {
                var side = rng.Chance(0.5) ? TeamSide.Home : TeamSide.Away;
                var start = rng.Next(20, 65);
                var counter = new TeamBehaviour { Control = 0.48, Progression = 0.72, Press = 0.66, CounterAttack = 0.85,
                    Directness = 0.4, ShotPropensity = 1.5, Tempo = 1.15 };
                var possessive = new TeamBehaviour { Control = 0.55, PassAccuracy = 0.80, Progression = 0.62, Press = 0.5 };
                phases.Add(new ScenarioPhase(0, start, "Baseline", homeBase, awayBase));
                phases.Add(side == TeamSide.Home
                    ? new ScenarioPhase(start, start + 12, "Counter-attacks", counter, possessive)
                    : new ScenarioPhase(start, start + 12, "Counter-attacks", possessive, counter));
                phases.Add(new ScenarioPhase(start + 12, 90, "Baseline", homeBase, awayBase));
                expected.Add(new ExpectedPattern(InsightType.CounterAttackThreat, TeamOf(side, home, away).Id,
                    start + 2, start + 14, "Counter-attacking spell"));
                break;
            }

            case ScenarioKind.ChaoticFinish:
            {
                var chaos = new TeamBehaviour { Control = 0.40, PassAccuracy = 0.70, Progression = 0.78, Press = 0.80, Tempo = 1.45,
                    Directness = 0.38, CounterAttack = 0.6, ShotPropensity = 1.4 };
                phases.Add(new ScenarioPhase(0, 76, "Baseline", homeBase, awayBase));
                phases.Add(new ScenarioPhase(76, 90, "Chaos", chaos, chaos));
                expected.Add(new ExpectedPattern(InsightType.ChaosIncrease, "*", 77, 90, "Chaotic finish"));
                break;
            }

            case ScenarioKind.SubstitutionImpact:
            {
                var side = rng.Chance(0.5) ? TeamSide.Home : TeamSide.Away;
                var subMinute = rng.Next(50, 66);
                var team = TeamOf(side, home, away);
                beats.RemoveAll(b => b.Description.StartsWith(team.Id, StringComparison.Ordinal));
                beats.Add(new ScriptedBeat(subMinute * 60, $"{team.Id} impact substitution",
                    ctx => ctx.Substitute(side, 11, 17)));
                var shifted = new ChannelWeights(0.05, 0.12, 0.83);
                phases.Add(new ScenarioPhase(0, subMinute, "Baseline", homeBase, awayBase));
                phases.Add(side == TeamSide.Home
                    ? new ScenarioPhase(subMinute, 90, "After substitution", Commit(homeBase, shifted), awayBase)
                    : new ScenarioPhase(subMinute, 90, "After substitution", homeBase, Commit(awayBase, shifted)));
                expected.Add(new ExpectedPattern(InsightType.PostSubstitutionChange, team.Id, subMinute + 3, subMinute + 15,
                    "Pattern change after substitution"));
                expected.Add(new ExpectedPattern(InsightType.AttackChannelShift, team.Id, subMinute + 3, subMinute + 18,
                    "Shift to the right after substitution"));
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        var script = new ScenarioScript
        {
            Name = kind.ToString(),
            MatchId = matchId,
            Home = home,
            Away = away,
            Phases = phases,
            Beats = beats,
            AllowRandomGoals = true,
            Venue = $"{home.ShortName} Ground (synthetic)",
            Date = new DateOnly(2026, 1, 1).AddDays((int)(Math.Abs(seed) % 300)),
        };

        var match = MatchSimulator.Simulate(script, seed);
        return new LabelledMatch(match, new GroundTruth(matchId, kind.ToString(), seed, expected, forbidden));
    }

    /// <summary>A team committing to one flank also commits more players forward.</summary>
    private static TeamBehaviour Commit(TeamBehaviour baseline, ChannelWeights channels) => baseline with
    {
        Channels = channels,
        Control = Math.Min(0.66, baseline.Control + 0.12),
        Progression = Math.Min(0.72, baseline.Progression + 0.15),
    };

    private static (Team Home, Team Away) PickFixture(DeterministicRandom rng)
    {
        var teams = ClubCatalog.AllTeams().ToList();
        var home = rng.Pick(teams);
        Team away;
        do { away = rng.Pick(teams); } while (away.Id == home.Id);
        return (home, away);
    }

    private static TeamBehaviour RandomBase(DeterministicRandom rng)
    {
        var strength = rng.Range(0.44, 0.56);
        var left = rng.Range(0.28, 0.40);
        var right = rng.Range(0.28, 0.40);
        return new TeamBehaviour
        {
            Control = strength,
            PassAccuracy = 0.86 + (strength - 0.5) * 0.3,
            Progression = rng.Range(0.42, 0.58),
            Press = rng.Range(0.42, 0.58),
            Channels = new ChannelWeights(left, 1 - left - right + 0.05, right),
            Directness = rng.Range(0.08, 0.2),
            CounterAttack = rng.Range(0.1, 0.22),
        };
    }

    private static List<ScriptedBeat> RandomSubstitutions(DeterministicRandom rng, Team home, Team away)
    {
        // Routine substitutions with NO behaviour change: a negative control for the post-substitution detector.
        var beats = new List<ScriptedBeat>();
        foreach (var (team, side) in new[] { (home, TeamSide.Home), (away, TeamSide.Away) })
        {
            var minute = rng.Next(58, 72);
            beats.Add(new ScriptedBeat(minute * 60 + rng.Next(0, 50), $"{team.Id} routine substitution",
                ctx => ctx.Substitute(side, 8, 15)));
            var later = rng.Next(74, 84);
            beats.Add(new ScriptedBeat(later * 60 + rng.Next(0, 50), $"{team.Id} routine substitution",
                ctx => ctx.Substitute(side, 9, 19)));
        }
        return beats;
    }

    private static Team TeamOf(TeamSide side, Team home, Team away) => side == TeamSide.Home ? home : away;

    private static string Abbrev(ScenarioKind kind) => kind switch
    {
        ScenarioKind.Stationary => "STAT",
        ScenarioKind.AttackChannelShift => "ACS",
        ScenarioKind.SustainedPressure => "PRES",
        ScenarioKind.CounterAttackSwing => "CTR",
        ScenarioKind.ChaoticFinish => "CHAOS",
        ScenarioKind.SubstitutionImpact => "SUB",
        _ => "GEN",
    };
}
