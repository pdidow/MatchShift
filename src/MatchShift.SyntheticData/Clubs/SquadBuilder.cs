using MatchShift.Core.Domain;

namespace MatchShift.SyntheticData.Clubs;

/// <summary>Builds fictional players and squads with behaviour profiles derived from position.</summary>
public static class SquadBuilder
{
    // Invented name fragments for dataset clubs. Combined deterministically per club.
    private static readonly string[] FirstNames =
    [
        "Arlo", "Bastian", "Cormac", "Dariusz", "Emeka", "Fintan", "Gael", "Hamish", "Idris", "Joaquin",
        "Kasimir", "Leandro", "Malik", "Nils", "Oisin", "Pascal", "Quentin", "Rasmus", "Soren", "Teodor",
        "Ugo", "Valentin", "Wilmer", "Xavi", "Yusuf", "Zoltan", "Anselm", "Brodie", "Cyprian", "Dante",
    ];

    private static readonly string[] Surnames =
    [
        "Ashdown", "Belcourt", "Carrow", "Dunleavy", "Eskeland", "Fenwright", "Galloway-Ness", "Harrowby",
        "Ingleby", "Jarrett-Mbeki", "Kestrel", "Lomax", "Mortimer-Diaz", "Nettleford", "Orsini", "Pryce-Adeyemi",
        "Quarrington", "Roskilde", "Saltmarsh", "Thistlewood", "Underhill", "Vasquez-Rowe", "Whitcombe", "Yarborough",
        "Zelinski", "Abernethy", "Blackthorn", "Coldwell", "Draycott", "Elsworth", "Fairclough-Obi", "Greaves",
    ];

    private static readonly (Position Position, int Shirt, bool Starter)[] Template433 =
    [
        (Position.Goalkeeper, 1, true), (Position.RightBack, 2, true), (Position.CentreBack, 5, true),
        (Position.CentreBack, 6, true), (Position.LeftBack, 3, true), (Position.DefensiveMidfielder, 4, true),
        (Position.CentralMidfielder, 8, true), (Position.AttackingMidfielder, 10, true), (Position.RightWinger, 7, true),
        (Position.LeftWinger, 11, true), (Position.Striker, 9, true), (Position.Goalkeeper, 13, false),
        (Position.CentreBack, 14, false), (Position.CentralMidfielder, 15, false), (Position.CentralMidfielder, 16, false),
        (Position.RightWinger, 17, false), (Position.LeftWinger, 18, false), (Position.Striker, 19, false),
    ];

    public static Player Make(
        string teamId,
        int shirt,
        string name,
        Position position,
        bool starter,
        PreferredSide? side = null,
        double? involvement = null,
        double? shots = null,
        double? pressing = null,
        double? aggression = null)
    {
        var profile = DefaultProfile(position, side);
        profile = profile with
        {
            PassingInvolvement = involvement ?? profile.PassingInvolvement,
            ShotFrequency = shots ?? profile.ShotFrequency,
            PressingActivity = pressing ?? profile.PressingActivity,
            AttackingAggressiveness = aggression ?? profile.AttackingAggressiveness,
        };
        var parts = name.Split(' ', 2);
        return new Player
        {
            Id = $"{teamId}-{shirt:D2}",
            Name = name,
            ShortName = parts.Length > 1 ? parts[1] : name,
            ShirtNumber = shirt,
            Position = position,
            TeamId = teamId,
            IsStarter = starter,
            Profile = profile,
        };
    }

    public static BehaviourProfile DefaultProfile(Position position, PreferredSide? sideOverride = null)
    {
        var (x, y, side) = position switch
        {
            Position.Goalkeeper => (6.0, 50.0, PreferredSide.Centre),
            Position.RightBack => (35.0, 86.0, PreferredSide.Right),
            Position.CentreBack => (22.0, 50.0, PreferredSide.Centre),
            Position.LeftBack => (35.0, 14.0, PreferredSide.Left),
            Position.DefensiveMidfielder => (40.0, 50.0, PreferredSide.Centre),
            Position.CentralMidfielder => (52.0, 50.0, PreferredSide.Centre),
            Position.AttackingMidfielder => (64.0, 50.0, PreferredSide.Centre),
            Position.RightWinger => (72.0, 86.0, PreferredSide.Right),
            Position.LeftWinger => (72.0, 14.0, PreferredSide.Left),
            Position.Striker => (83.0, 50.0, PreferredSide.Centre),
            _ => (50.0, 50.0, PreferredSide.Centre),
        };
        var preferred = sideOverride ?? side;
        if (sideOverride is not null && position is Position.CentreBack or Position.CentralMidfielder)
        {
            y = sideOverride == PreferredSide.Left ? 35 : sideOverride == PreferredSide.Right ? 65 : 50;
        }

        return position switch
        {
            Position.Goalkeeper => new BehaviourProfile
            {
                PassingInvolvement = 0.2, ForwardPassTendency = 0.6, PreferredSide = preferred, ShotFrequency = 0,
                PressingActivity = 0.05, TurnoverTendency = 0.15, RecoveryTendency = 0.3, AttackingAggressiveness = 0.05,
                AverageX = x, AverageY = y,
            },
            Position.CentreBack => new BehaviourProfile
            {
                PassingInvolvement = 0.55, ForwardPassTendency = 0.35, PreferredSide = preferred, ShotFrequency = 0.03,
                PressingActivity = 0.35, TurnoverTendency = 0.2, RecoveryTendency = 0.75, AttackingAggressiveness = 0.15,
                AverageX = x, AverageY = y,
            },
            Position.RightBack or Position.LeftBack => new BehaviourProfile
            {
                PassingInvolvement = 0.6, ForwardPassTendency = 0.5, PreferredSide = preferred, ShotFrequency = 0.04,
                PressingActivity = 0.5, TurnoverTendency = 0.3, RecoveryTendency = 0.6, AttackingAggressiveness = 0.45,
                AverageX = x, AverageY = y,
            },
            Position.DefensiveMidfielder => new BehaviourProfile
            {
                PassingInvolvement = 0.8, ForwardPassTendency = 0.45, PreferredSide = preferred, ShotFrequency = 0.06,
                PressingActivity = 0.65, TurnoverTendency = 0.25, RecoveryTendency = 0.8, AttackingAggressiveness = 0.3,
                AverageX = x, AverageY = y,
            },
            Position.CentralMidfielder => new BehaviourProfile
            {
                PassingInvolvement = 0.75, ForwardPassTendency = 0.55, PreferredSide = preferred, ShotFrequency = 0.12,
                PressingActivity = 0.6, TurnoverTendency = 0.3, RecoveryTendency = 0.6, AttackingAggressiveness = 0.5,
                AverageX = x, AverageY = y,
            },
            Position.AttackingMidfielder => new BehaviourProfile
            {
                PassingInvolvement = 0.7, ForwardPassTendency = 0.65, PreferredSide = preferred, ShotFrequency = 0.25,
                PressingActivity = 0.55, TurnoverTendency = 0.4, RecoveryTendency = 0.4, AttackingAggressiveness = 0.75,
                AverageX = x, AverageY = y,
            },
            Position.RightWinger or Position.LeftWinger => new BehaviourProfile
            {
                PassingInvolvement = 0.55, ForwardPassTendency = 0.7, PreferredSide = preferred, ShotFrequency = 0.2,
                PressingActivity = 0.5, TurnoverTendency = 0.45, RecoveryTendency = 0.35, AttackingAggressiveness = 0.75,
                AverageX = x, AverageY = y,
            },
            Position.Striker => new BehaviourProfile
            {
                PassingInvolvement = 0.4, ForwardPassTendency = 0.5, PreferredSide = preferred, ShotFrequency = 0.45,
                PressingActivity = 0.6, TurnoverTendency = 0.45, RecoveryTendency = 0.3, AttackingAggressiveness = 0.9,
                AverageX = x, AverageY = y,
            },
            _ => new BehaviourProfile { AverageX = x, AverageY = y, PreferredSide = preferred },
        };
    }

    /// <summary>Deterministically generate a full fictional squad for a dataset club.</summary>
    public static Team GenerateSquad(ClubIdentity club)
    {
        var rng = new DeterministicRandom(StableHash(club.Id));
        var used = new HashSet<string>();
        var squad = new List<Player>();
        foreach (var (position, shirt, starter) in Template433)
        {
            string name;
            do
            {
                name = $"{rng.Pick(FirstNames)} {rng.Pick(Surnames)}";
            } while (!used.Add(name));

            var side = position == Position.CentreBack ? (shirt == 5 ? PreferredSide.Right : PreferredSide.Left) : (PreferredSide?)null;
            var player = Make(club.Id, shirt, name, position, starter, side);
            // Small deterministic individual variation so squads are not identical.
            player = player with
            {
                Profile = player.Profile with
                {
                    PassingInvolvement = Clamp01(player.Profile.PassingInvolvement + rng.Range(-0.08, 0.08)),
                    ShotFrequency = Clamp01(player.Profile.ShotFrequency + rng.Range(-0.04, 0.06)),
                    TurnoverTendency = Clamp01(player.Profile.TurnoverTendency + rng.Range(-0.08, 0.08)),
                },
            };
            squad.Add(player);
        }

        return new Team
        {
            Id = club.Id,
            Name = club.Name,
            ShortName = club.ShortName,
            Code = club.Id,
            PrimaryColor = club.PrimaryColor,
            SecondaryColor = club.SecondaryColor,
            Formation = club.Formation,
            Squad = squad,
        };
    }

    private static double Clamp01(double v) => Math.Clamp(v, 0.01, 0.99);

    /// <summary>FNV-1a; string.GetHashCode is randomized per process and must not be used for seeds.</summary>
    public static long StableHash(string value)
    {
        unchecked
        {
            var hash = 1469598103934665603UL;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return (long)hash;
        }
    }
}
