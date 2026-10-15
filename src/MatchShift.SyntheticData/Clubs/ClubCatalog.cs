using MatchShift.Core.Domain;

namespace MatchShift.SyntheticData.Clubs;

/// <summary>
/// Entirely fictional clubs and players. Any resemblance to real clubs or people
/// is coincidental. No real Premier League branding, names or statistics are used.
/// </summary>
public static class ClubCatalog
{
    public const string NorthbridgeId = "NBR";
    public const string WesthavenId = "WHA";

    public static Team Northbridge { get; } = BuildNorthbridge();
    public static Team Westhaven { get; } = BuildWesthaven();

    /// <summary>Additional fictional clubs used for dataset expansion.</summary>
    public static IReadOnlyList<ClubIdentity> DatasetClubs { get; } =
    [
        new("RVR", "Ravensmoor Rovers", "Ravensmoor", "#5B2A86", "#F4D35E", "4-3-3"),
        new("STB", "Stonebury Town", "Stonebury", "#B23A48", "#FCEADE", "4-4-2"),
        new("LKU", "Larkfield United", "Larkfield", "#1D7874", "#F7F7F2", "4-2-3-1"),
        new("ASV", "Ashvale City", "Ashvale", "#2E4057", "#FF8C42", "3-4-3"),
        new("GLM", "Glenmarrow Albion", "Glenmarrow", "#3A5A40", "#DAD7CD", "4-3-3"),
        new("CPH", "Copperhythe FC", "Copperhythe", "#9C4F1C", "#2B2D42", "4-2-3-1"),
    ];

    public static IEnumerable<Team> AllTeams()
    {
        yield return Northbridge;
        yield return Westhaven;
        foreach (var club in DatasetClubs) yield return SquadBuilder.GenerateSquad(club);
    }

    public static Team ById(string id) =>
        AllTeams().FirstOrDefault(t => t.Id == id) ?? throw new ArgumentException($"Unknown club '{id}'");

    private static Team BuildNorthbridge()
    {
        const string t = NorthbridgeId;
        var squad = new List<Player>
        {
            SquadBuilder.Make(t, 1, "Aaron Holloway", Position.Goalkeeper, true),
            SquadBuilder.Make(t, 2, "Callum Brierley", Position.RightBack, true),
            SquadBuilder.Make(t, 5, "Rhys Penhallow", Position.CentreBack, true, side: PreferredSide.Right),
            SquadBuilder.Make(t, 6, "Marcus Oyelaran", Position.CentreBack, true, side: PreferredSide.Left),
            SquadBuilder.Make(t, 3, "Jonah Tremlett", Position.LeftBack, true),
            SquadBuilder.Make(t, 4, "Theo Castellane", Position.DefensiveMidfielder, true, pressing: 0.8),
            SquadBuilder.Make(t, 8, "Elias Varnholt", Position.CentralMidfielder, true, side: PreferredSide.Right, involvement: 0.85),
            SquadBuilder.Make(t, 10, "Luca Ferreiro", Position.AttackingMidfielder, true, shots: 0.3, involvement: 0.8),
            SquadBuilder.Make(t, 7, "Kofi Asante-Marr", Position.RightWinger, true, aggression: 0.8),
            SquadBuilder.Make(t, 11, "Samir Haddadi", Position.LeftWinger, true),
            SquadBuilder.Make(t, 9, "Owen Blackwood", Position.Striker, true, shots: 0.55),
            SquadBuilder.Make(t, 13, "Felix Dunmore", Position.Goalkeeper, false),
            SquadBuilder.Make(t, 14, "Isaac Pemberton-Lowe", Position.CentreBack, false),
            SquadBuilder.Make(t, 15, "Rafael Montiel", Position.CentralMidfielder, false),
            SquadBuilder.Make(t, 16, "Niall Corrigan", Position.CentralMidfielder, false),
            SquadBuilder.Make(t, 17, "Dario Sanzetti", Position.RightWinger, false),
            SquadBuilder.Make(t, 18, "Benji Okonkwo-Hale", Position.LeftWinger, false),
            SquadBuilder.Make(t, 19, "Mateo Quiroga", Position.Striker, false, shots: 0.5),
        };
        return new Team
        {
            Id = t,
            Name = "Northbridge FC",
            ShortName = "Northbridge",
            Code = "NBR",
            PrimaryColor = "#1F3A93",
            SecondaryColor = "#F2C14E",
            Formation = "4-3-3",
            Squad = squad,
        };
    }

    private static Team BuildWesthaven()
    {
        const string t = WesthavenId;
        var squad = new List<Player>
        {
            SquadBuilder.Make(t, 1, "Viktor Halvorsen", Position.Goalkeeper, true),
            SquadBuilder.Make(t, 2, "Tobias Ekwueme", Position.RightBack, true, aggression: 0.7),
            SquadBuilder.Make(t, 5, "Gareth Pollard", Position.CentreBack, true, side: PreferredSide.Right),
            SquadBuilder.Make(t, 6, "Yannick Delacroix-Bah", Position.CentreBack, true, side: PreferredSide.Left),
            SquadBuilder.Make(t, 3, "Ruben Esteves", Position.LeftBack, true),
            SquadBuilder.Make(t, 4, "Kwame Addo-Sterling", Position.DefensiveMidfielder, true, pressing: 0.75),
            SquadBuilder.Make(t, 8, "Henrik Solberg", Position.CentralMidfielder, true, involvement: 0.8),
            SquadBuilder.Make(t, 10, "Adrien Marchetti", Position.AttackingMidfielder, true, shots: 0.3, involvement: 0.8),
            SquadBuilder.Make(t, 7, "Jesse Arkwright", Position.RightWinger, true, aggression: 0.7),
            SquadBuilder.Make(t, 11, "Noah Penrose", Position.LeftWinger, true),
            SquadBuilder.Make(t, 9, "Ivo Rakovec", Position.Striker, true, shots: 0.55),
            SquadBuilder.Make(t, 13, "Lewis Atherby", Position.Goalkeeper, false),
            SquadBuilder.Make(t, 14, "Sam Okafor-Reid", Position.CentreBack, false),
            SquadBuilder.Make(t, 15, "Pablo Ibarguren", Position.CentralMidfielder, false),
            SquadBuilder.Make(t, 16, "Ciaran Doolan", Position.CentralMidfielder, false),
            SquadBuilder.Make(t, 17, "Tavian Brooks", Position.RightWinger, false, aggression: 0.9, involvement: 0.85, shots: 0.3),
            SquadBuilder.Make(t, 18, "Elliot Fairbairn", Position.AttackingMidfielder, false),
            SquadBuilder.Make(t, 19, "Mikel Garaiar", Position.Striker, false, shots: 0.5),
        };
        return new Team
        {
            Id = t,
            Name = "Westhaven Athletic",
            ShortName = "Westhaven",
            Code = "WHA",
            PrimaryColor = "#E4572E",
            SecondaryColor = "#17BEBB",
            Formation = "4-2-3-1",
            Squad = squad,
        };
    }
}

public sealed record ClubIdentity(
    string Id,
    string Name,
    string ShortName,
    string PrimaryColor,
    string SecondaryColor,
    string Formation);
