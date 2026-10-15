using MatchShift.Core.Domain;
using MatchShift.Core.Insights;
using MatchShift.SyntheticData.Clubs;
using MatchShift.SyntheticData.Simulation;

namespace MatchShift.SyntheticData.Scenarios;

/// <summary>
/// The deterministic showcase match: Northbridge FC v Westhaven Athletic.
///
/// The script only changes BEHAVIOUR (phase parameters) and injects a handful of
/// fixed sequences (goals, substitutions, the 72' turnover). It never writes a
/// "shift happened" flag - the analytics engine has to discover every pattern.
///
///  0-15  balanced opening
/// 15-30  Northbridge gain territorial control
/// 30-38  sustained Northbridge pressure (goal 34')
/// 38-45  Westhaven counter-attack (goal 43')
/// 46-55  balance returns
///   55   Westhaven substitution: Brooks (right winger) on for Penrose
/// 56-65  Westhaven attack increasingly down the right
/// 65-72  Westhaven pressure rises
///   72   Northbridge lose the ball in their own third -> big chance
/// 72-75  major chance sequence (Westhaven goal 74')
/// 75-82  Northbridge respond with higher pressure
/// 82-90  high-tempo chaotic finish (Northbridge goal 87')
/// </summary>
public static class FlagshipScenario
{
    public const string MatchId = "MS-FLAGSHIP-001";
    public const string Name = "Flagship: Northbridge v Westhaven";
    public const long Seed = 20260327;

    public const int WesthavenSubstitutionSecond = 55 * 60;
    public const int DangerousTurnoverSecond = 72 * 60 + 5;

    public static ScenarioScript Script()
    {
        var balanced = new TeamBehaviour();
        var balancedAway = new TeamBehaviour { Channels = new ChannelWeights(0.35, 0.33, 0.32) };

        var phases = new List<ScenarioPhase>
        {
            new(0, 15, "Balanced opening", balanced, balancedAway),

            new(15, 30, "Northbridge territorial control",
                new TeamBehaviour { Control = 0.70, PassAccuracy = 0.86, Progression = 0.52, Press = 0.66 },
                new TeamBehaviour { Control = 0.36, PassAccuracy = 0.76, Progression = 0.38, Press = 0.38, Directness = 0.28,
                    Channels = balancedAway.Channels }),

            new(30, 38, "Sustained Northbridge pressure",
                new TeamBehaviour { Control = 0.74, PassAccuracy = 0.86, Progression = 0.66, Press = 0.85, ShotPropensity = 1.7 },
                new TeamBehaviour { Control = 0.30, PassAccuracy = 0.72, Progression = 0.32, Press = 0.32, Directness = 0.3,
                    Channels = balancedAway.Channels }),

            new(38, 45, "Westhaven counter-attacks",
                new TeamBehaviour { Control = 0.55, PassAccuracy = 0.80, Progression = 0.62, Press = 0.50 },
                new TeamBehaviour { Control = 0.48, Progression = 0.72, Press = 0.66, CounterAttack = 0.85, Directness = 0.4,
                    ShotPropensity = 1.5, Tempo = 1.15, Channels = balancedAway.Channels }),

            new(45, 55, "Second half balance", balanced, balancedAway),

            new(55, 65, "Westhaven move the attack to the right",
                new TeamBehaviour { Control = 0.46, Progression = 0.48, Press = 0.46 },
                new TeamBehaviour { Control = 0.64, PassAccuracy = 0.87, Progression = 0.70, Press = 0.58,
                    Channels = new ChannelWeights(0.05, 0.12, 0.83) }),

            new(65, 72, "Westhaven pressure rises",
                new TeamBehaviour { Control = 0.38, PassAccuracy = 0.77, Progression = 0.40, Press = 0.40 },
                new TeamBehaviour { Control = 0.68, PassAccuracy = 0.85, Progression = 0.64, Press = 0.80, ShotPropensity = 1.6,
                    Channels = new ChannelWeights(0.06, 0.16, 0.78) }),

            new(72, 75, "Major chance sequence",
                new TeamBehaviour { Control = 0.40, Progression = 0.45, Press = 0.45 },
                new TeamBehaviour { Control = 0.62, Progression = 0.62, Press = 0.72, ShotPropensity = 1.5,
                    Channels = new ChannelWeights(0.15, 0.25, 0.60) }),

            new(75, 82, "Northbridge respond",
                new TeamBehaviour { Control = 0.66, PassAccuracy = 0.85, Progression = 0.64, Press = 0.82, ShotPropensity = 1.5 },
                new TeamBehaviour { Control = 0.38, Progression = 0.45, Press = 0.40, CounterAttack = 0.3,
                    Channels = new ChannelWeights(0.2, 0.3, 0.5) }),

            new(82, 90, "Chaotic finish",
                new TeamBehaviour { Control = 0.40, PassAccuracy = 0.70, Progression = 0.78, Press = 0.80, Tempo = 1.45,
                    Directness = 0.38, CounterAttack = 0.6, ShotPropensity = 1.4 },
                new TeamBehaviour { Control = 0.40, PassAccuracy = 0.70, Progression = 0.78, Press = 0.80, Tempo = 1.45,
                    Directness = 0.38, CounterAttack = 0.6, ShotPropensity = 1.4, Channels = new ChannelWeights(0.25, 0.3, 0.45) }),
        };

        var beats = new List<ScriptedBeat>
        {
            new(34 * 60 + 20, "Northbridge goal after sustained pressure", ctx => ctx
                .Gain(TeamSide.Home, 4, 61, 46)
                .Pass(8, 67, 63, 2)
                .Pass(7, 81, 86, 3)
                .Carry(90, 82, 3)
                .Pass(9, 93, 53, 2, "Cross")
                .Shot(EventOutcome.Goal, "Header")),

            new(43 * 60 + 5, "Westhaven counter-attack goal", ctx => ctx
                .Gain(TeamSide.Away, 4, 31, 56)
                .Pass(10, 47, 51, 2)
                .Pass(7, 68, 81, 2)
                .Carry(81, 75, 2)
                .Pass(9, 90, 46, 2)
                .Shot(EventOutcome.Goal)),

            new(WesthavenSubstitutionSecond, "Westhaven substitution: Brooks on for Penrose",
                ctx => ctx.Substitute(TeamSide.Away, 11, 17)),

            new(DangerousTurnoverSecond, "Northbridge lose possession in their own third", ctx => ctx
                .Gain(TeamSide.Home, 5, 17, 60)
                .Pass(4, 25, 50, 2)
                .PassIntercepted(8, 31, 30, 17, 1)
                .Carry(79, 74, 3)
                .Pass(9, 90, 55, 2)
                .Shot(EventOutcome.OnTarget)),

            new(74 * 60 + 10, "Westhaven goal from the right", ctx => ctx
                .Gain(TeamSide.Away, 8, 60, 62)
                .Pass(17, 77, 87, 3)
                .Carry(89, 82, 3)
                .Pass(10, 88, 49, 2, "Cutback")
                .Shot(EventOutcome.Goal)),

            new(76 * 60, "Northbridge substitution: Okonkwo-Hale on", ctx => ctx.Substitute(TeamSide.Home, 11, 18)),
            new(80 * 60, "Northbridge substitution: Quiroga on", ctx => ctx.Substitute(TeamSide.Home, 9, 19)),

            new(87 * 60 + 30, "Northbridge late equaliser", ctx => ctx
                .Gain(TeamSide.Home, 4, 64, 40)
                .Pass(10, 76, 51, 2)
                .Pass(19, 89, 44, 2)
                .Shot(EventOutcome.Goal)),
        };

        return new ScenarioScript
        {
            Name = Name,
            MatchId = MatchId,
            Home = ClubCatalog.Northbridge,
            Away = ClubCatalog.Westhaven,
            Phases = phases,
            Beats = beats,
            AllowRandomGoals = false,
            Venue = "Northbridge Park (synthetic)",
            Date = new DateOnly(2026, 3, 14),
        };
    }

    public static GroundTruth GroundTruth()
    {
        var nb = ClubCatalog.NorthbridgeId;
        var wh = ClubCatalog.WesthavenId;
        return new GroundTruth(MatchId, Name, Seed,
        [
            new(InsightType.ControlShift, nb, 15, 32, "Northbridge take territorial control"),
            new(InsightType.PressureIncrease, nb, 28, 40, "Sustained Northbridge pressure"),
            new(InsightType.CounterAttackThreat, wh, 38, 47, "Westhaven counter-attacks before half-time"),
            new(InsightType.AttackChannelShift, wh, 58, 67, "Westhaven attack down the right after the substitution"),
            new(InsightType.PostSubstitutionChange, wh, 58, 70, "Pattern change after Brooks comes on"),
            new(InsightType.PressureIncrease, wh, 65, 75, "Westhaven pressure rises"),
            new(InsightType.DangerousTurnover, nb, 72, 73, "Northbridge lose the ball in their own third"),
            new(InsightType.PressureIncrease, nb, 75, 84, "Northbridge respond"),
            new(InsightType.ChaosIncrease, "*", 82, 90, "High-tempo chaotic finish"),
        ],
        []);
    }

    public static LabelledMatch Generate() => new(MatchSimulator.Simulate(Script(), Seed), GroundTruth());
}
