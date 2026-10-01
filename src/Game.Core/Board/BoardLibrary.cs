using System.Text.Json;
using Game.Core.Serialization;

namespace Game.Core.Board;

/// <summary>Built-in, fully data-driven boards plus JSON loading for custom boards.</summary>
public static class BoardLibrary
{
    public const string DefaultBoardId = "neo_city";

    private static readonly Lazy<Dictionary<string, BoardDefinition>> Boards = new(() =>
    {
        var list = new[] { NeoCity(), PirateIsles(), MarsColony(), PocketCity() };
        foreach (var b in list) b.Validate();
        return list.ToDictionary(b => b.BoardId);
    });

    public static IReadOnlyCollection<string> BoardIds => Boards.Value.Keys;

    public static BoardDefinition Get(string boardId) =>
        Boards.Value.TryGetValue(boardId, out var b)
            ? b
            : throw new ArgumentException($"Unknown board '{boardId}'");

    public static IEnumerable<BoardDefinition> All => Boards.Value.Values;

    public static BoardDefinition FromJson(string json)
    {
        var board = JsonSerializer.Deserialize<BoardDefinition>(json, CoreJson.Options)
                    ?? throw new InvalidOperationException("Board JSON is empty");
        board.Validate();
        return board;
    }

    public static string ToJson(BoardDefinition board) => JsonSerializer.Serialize(board, CoreJson.Indented);

    // ---------------------------------------------------------------- themes

    private sealed record Skin(
        string Id, string Name, string Theme, string Currency,
        string Start, string Jail, string GoToJail, string Plaza, string Tax1, string Tax2,
        string FortuneName, string CivicName,
        (string Id, string Name, string Color, string Glyph)[] Districts,
        string[] Streets, string[] Transits, string[] Utilities);

    private static BoardDefinition NeoCity() => Classic40(new Skin(
        "neo_city", "Neo City", "city", "₹",
        "Launch Plaza", "Detention Block", "Customs Check", "Central Park", "Income Levy", "Luxury Levy",
        "Fortune", "City Fund",
        new[]
        {
            ("harbor", "Harbor Row", "#8d5a3b", "anchor"),
            ("market", "Old Market", "#69c8ec", "diamond"),
            ("garden", "Garden Quarter", "#e0559b", "leaf"),
            ("artisan", "Artisan Hill", "#f29a2e", "triangle"),
            ("civic", "Civic Centre", "#e23b3b", "square"),
            ("tech", "Tech Park", "#f2d53c", "hexagon"),
            ("riverside", "Riverside", "#2faa5f", "wave"),
            ("skyline", "Skyline", "#2f55c8", "star"),
        },
        new[]
        {
            "Dockside Lane", "Anchor Street",
            "Spice Bazaar", "Lantern Walk", "Tinker's Alley",
            "Lotus Court", "Banyan Avenue", "Marigold Square",
            "Potter's Rise", "Weaver's Bend", "Gallery Road",
            "Assembly Street", "Clocktower Plaza", "Library Circle",
            "Circuit Drive", "Data Boulevard", "Quantum Crescent",
            "Riverside Promenade", "Bridgeview Terrace", "Marina Heights",
            "Central Plaza", "Skyline Tower",
        },
        new[] { "South Metro", "West Metro", "North Metro", "East Metro" },
        new[] { "Solar Grid", "Water Works" }));

    private static BoardDefinition PirateIsles() => Classic40(new Skin(
        "pirate_isles", "Pirate Isles", "pirate", "⚓",
        "Home Port", "The Brig", "Navy Patrol", "Castaway Cove", "Crown Tithe", "Governor's Tax",
        "Message in a Bottle", "Crew's Chest",
        new[]
        {
            ("shanty", "Shanty Shoals", "#8d5a3b", "anchor"),
            ("reef", "Coral Reef", "#69c8ec", "diamond"),
            ("lagoon", "Rose Lagoon", "#e0559b", "leaf"),
            ("dunes", "Amber Dunes", "#f29a2e", "triangle"),
            ("volcano", "Ember Peak", "#e23b3b", "square"),
            ("gold", "Gold Coast", "#f2d53c", "hexagon"),
            ("jungle", "Emerald Jungle", "#2faa5f", "wave"),
            ("fort", "Royal Fort", "#2f55c8", "star"),
        },
        new[]
        {
            "Barnacle Wharf", "Driftwood Shack",
            "Pearl Shallows", "Turtle Bank", "Siren's Rock",
            "Flamingo Flats", "Orchid Inlet", "Sunset Sandbar",
            "Scorpion Ridge", "Mirage Wells", "Caravan Camp",
            "Lava Steps", "Cinder Village", "Dragon's Mouth",
            "Doubloon Bay", "Treasure Grotto", "Idol Temple",
            "Parrot Canopy", "Vine Bridge", "Waterfall Hideout",
            "Admiral's Quarter", "Governor's Keep",
        },
        new[] { "South Ferry", "West Ferry", "North Ferry", "East Ferry" },
        new[] { "Windmill", "Freshwater Spring" }));

    private static BoardDefinition MarsColony() => Classic40(new Skin(
        "mars_colony", "Mars Colony", "space", "¢",
        "Landing Pad", "Quarantine Bay", "Airlock Breach", "Biodome Park", "Oxygen Levy", "Import Levy",
        "Signal", "Colony Fund",
        new[]
        {
            ("regolith", "Regolith Flats", "#8d5a3b", "anchor"),
            ("ice", "Ice Fields", "#69c8ec", "diamond"),
            ("hydro", "Hydroponics", "#e0559b", "leaf"),
            ("forge", "Forge Sector", "#f29a2e", "triangle"),
            ("reactor", "Reactor Ring", "#e23b3b", "square"),
            ("solar", "Solar Plains", "#f2d53c", "hexagon"),
            ("terra", "Terraform Zone", "#2faa5f", "wave"),
            ("orbital", "Orbital Spire", "#2f55c8", "star"),
        },
        new[]
        {
            "Dust Shelter", "Rover Depot",
            "Glacier Tap", "Frost Cavern", "Cryo Vault",
            "Algae Farm", "Seed Bank", "Orchard Dome",
            "Smelter Yard", "Printer Works", "Alloy Foundry",
            "Coolant Loop", "Fusion Hall", "Core Control",
            "Mirror Array", "Battery Fields", "Beam Station",
            "Moss Terraces", "Cloud Seeder", "Lake Genesis",
            "Sky Dock", "Olympus Tower",
        },
        new[] { "South Maglev", "West Maglev", "North Maglev", "East Maglev" },
        new[] { "Power Relay", "Water Reclaimer" }));

    private static readonly (int Price, int House, int[] Rents)[] ClassicStreets =
    {
        (60, 50, new[] { 2, 10, 30, 90, 160, 250 }),
        (60, 50, new[] { 4, 20, 60, 180, 320, 450 }),
        (100, 50, new[] { 6, 30, 90, 270, 400, 550 }),
        (100, 50, new[] { 6, 30, 90, 270, 400, 550 }),
        (120, 50, new[] { 8, 40, 100, 300, 450, 600 }),
        (140, 100, new[] { 10, 50, 150, 450, 625, 750 }),
        (140, 100, new[] { 10, 50, 150, 450, 625, 750 }),
        (160, 100, new[] { 12, 60, 180, 500, 700, 900 }),
        (180, 100, new[] { 14, 70, 200, 550, 750, 950 }),
        (180, 100, new[] { 14, 70, 200, 550, 750, 950 }),
        (200, 100, new[] { 16, 80, 220, 600, 800, 1000 }),
        (220, 150, new[] { 18, 90, 250, 700, 875, 1050 }),
        (220, 150, new[] { 18, 90, 250, 700, 875, 1050 }),
        (240, 150, new[] { 20, 100, 300, 750, 925, 1100 }),
        (260, 150, new[] { 22, 110, 330, 800, 975, 1150 }),
        (260, 150, new[] { 22, 110, 330, 800, 975, 1150 }),
        (280, 150, new[] { 24, 120, 360, 850, 1025, 1200 }),
        (300, 200, new[] { 26, 130, 390, 900, 1100, 1275 }),
        (300, 200, new[] { 26, 130, 390, 900, 1100, 1275 }),
        (320, 200, new[] { 28, 150, 450, 1000, 1200, 1400 }),
        (350, 200, new[] { 35, 175, 500, 1100, 1300, 1500 }),
        (400, 200, new[] { 50, 200, 600, 1400, 1700, 2000 }),
    };

    // S = street, C = civic deck, F = fortune deck, T = transit, U = utility, X = tax
    private const string ClassicLayout = "0SCSXTSFSS" + "JSUSSTSCSS" + "PSFSSTSSUS" + "GSSCSTFSXS";
    private static readonly int[] ClassicDistrictOfStreet =
        { 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6, 7, 7 };

    private static BoardDefinition Classic40(Skin skin)
    {
        var board = new BoardDefinition
        {
            BoardId = skin.Id, Name = skin.Name, Theme = skin.Theme, Currency = skin.Currency,
        };
        foreach (var d in skin.Districts)
            board.Districts.Add(new DistrictDef { Id = d.Id, Name = d.Name, Color = d.Color, Glyph = d.Glyph });

        int street = 0, transit = 0, utility = 0, tax = 0, fortune = 0, civic = 0;
        foreach (char c in ClassicLayout)
        {
            switch (c)
            {
                case '0':
                    board.Tiles.Add(new TileDef { Id = "start", Name = skin.Start, Type = TileType.Start });
                    break;
                case 'S':
                    var s = ClassicStreets[street];
                    board.Tiles.Add(new TileDef
                    {
                        Id = $"{skin.Id}_{street + 1:00}", Name = skin.Streets[street], Type = TileType.Street,
                        District = skin.Districts[ClassicDistrictOfStreet[street]].Id,
                        Price = s.Price, HouseCost = s.House, Rents = s.Rents,
                    });
                    street++;
                    break;
                case 'T':
                    board.Tiles.Add(new TileDef
                    {
                        Id = $"transit_{transit + 1}", Name = skin.Transits[transit], Type = TileType.Transit, Price = 200,
                    });
                    transit++;
                    break;
                case 'U':
                    board.Tiles.Add(new TileDef
                    {
                        Id = $"utility_{utility + 1}", Name = skin.Utilities[utility], Type = TileType.Utility, Price = 150,
                    });
                    utility++;
                    break;
                case 'X':
                    board.Tiles.Add(new TileDef
                    {
                        Id = $"tax_{tax + 1}", Name = tax == 0 ? skin.Tax1 : skin.Tax2, Type = TileType.Tax,
                        TaxAmount = tax == 0 ? 200 : 100,
                    });
                    tax++;
                    break;
                case 'F':
                    board.Tiles.Add(new TileDef { Id = $"fortune_{++fortune}", Name = skin.FortuneName, Type = TileType.Fortune });
                    break;
                case 'C':
                    board.Tiles.Add(new TileDef { Id = $"civic_{++civic}", Name = skin.CivicName, Type = TileType.Civic });
                    break;
                case 'J':
                    board.Tiles.Add(new TileDef { Id = "jail", Name = skin.Jail, Type = TileType.Jail });
                    break;
                case 'P':
                    board.Tiles.Add(new TileDef { Id = "plaza", Name = skin.Plaza, Type = TileType.Plaza });
                    break;
                case 'G':
                    board.Tiles.Add(new TileDef { Id = "gotojail", Name = skin.GoToJail, Type = TileType.GoToJail });
                    break;
            }
        }

        string lastStreet = board.Tiles.Last(t => t.Type == TileType.Street).Id;
        string midStreet = board.Tiles[24].Id;
        string earlyStreet = board.Tiles[11].Id;
        AddStandardDecks(board, lastStreet, midStreet, earlyStreet, "transit_1");
        return board;
    }

    private static BoardDefinition PocketCity()
    {
        var board = new BoardDefinition
        {
            BoardId = "pocket_city", Name = "Pocket City", Theme = "city", Currency = "₹",
            TransitRents = new[] { 40, 100 }, UtilityMultipliers = new[] { 6 },
        };
        var districts = new[]
        {
            ("lane", "Lantern Lanes", "#8d5a3b", "anchor"),
            ("canal", "Canal Side", "#69c8ec", "diamond"),
            ("bloom", "Bloom Street", "#e0559b", "leaf"),
            ("brick", "Brickworks", "#f29a2e", "triangle"),
            ("glass", "Glass Mile", "#2faa5f", "wave"),
            ("crown", "Crown Heights", "#2f55c8", "star"),
        };
        foreach (var d in districts)
            board.Districts.Add(new DistrictDef { Id = d.Item1, Name = d.Item2, Color = d.Item3, Glyph = d.Item4 });

        TileDef Street(string id, string name, string district, int price) => new()
        {
            Id = id, Name = name, Type = TileType.Street, District = district, Price = price,
            HouseCost = price < 130 ? 50 : price < 230 ? 100 : 150, Rents = RentsFor(price),
        };

        board.Tiles.AddRange(new[]
        {
            new TileDef { Id = "start", Name = "Tram Stop Zero", Type = TileType.Start },
            Street("pocket_01", "Wick Lane", "lane", 60),
            new TileDef { Id = "civic_1", Name = "City Fund", Type = TileType.Civic },
            Street("pocket_02", "Ember Lane", "lane", 80),
            new TileDef { Id = "transit_1", Name = "Low Line", Type = TileType.Transit, Price = 150 },
            Street("pocket_03", "Barge Row", "canal", 100),
            new TileDef { Id = "jail", Name = "Detention Block", Type = TileType.Jail },
            Street("pocket_04", "Lock Keeper's Way", "canal", 120),
            new TileDef { Id = "utility_1", Name = "Solar Grid", Type = TileType.Utility, Price = 120 },
            Street("pocket_05", "Petal Passage", "bloom", 140),
            new TileDef { Id = "fortune_1", Name = "Fortune", Type = TileType.Fortune },
            Street("pocket_06", "Blossom Arcade", "bloom", 160),
            new TileDef { Id = "plaza", Name = "Pocket Park", Type = TileType.Plaza },
            Street("pocket_07", "Kiln Street", "brick", 180),
            new TileDef { Id = "transit_2", Name = "High Line", Type = TileType.Transit, Price = 150 },
            Street("pocket_08", "Mason's Yard", "brick", 200),
            new TileDef { Id = "tax_1", Name = "City Levy", Type = TileType.Tax, TaxAmount = 100 },
            Street("pocket_09", "Prism Walk", "glass", 220),
            new TileDef { Id = "gotojail", Name = "Customs Check", Type = TileType.GoToJail },
            Street("pocket_10", "Mirror Court", "glass", 240),
            new TileDef { Id = "fortune_2", Name = "Fortune", Type = TileType.Fortune },
            Street("pocket_11", "Regent Rise", "crown", 300),
            new TileDef { Id = "civic_2", Name = "City Fund", Type = TileType.Civic },
            Street("pocket_12", "Crown Terrace", "crown", 350),
        });
        AddStandardDecks(board, "pocket_12", "pocket_08", "pocket_04", "transit_1");
        return board;
    }

    /// <summary>Formula-based rent table so that custom boards only need a price.</summary>
    public static int[] RentsFor(int price)
    {
        int b = Math.Max(2, price / 10 - 4);
        return new[] { b, b * 5, b * 15, b * 40, b * 52, b * 64 };
    }

    private static void AddStandardDecks(BoardDefinition board, string lastStreet, string midStreet, string earlyStreet, string transit)
    {
        board.FortuneCards.AddRange(new[]
        {
            new CardDef { Id = "f_start", Text = "A fresh start! Advance to the start tile.", Effect = CardEffect.MoveTo, Target = "start" },
            new CardDef { Id = "f_prime", Text = "A VIP invitation arrives. Advance to the most exclusive address in town.", Effect = CardEffect.MoveTo, Target = lastStreet },
            new CardDef { Id = "f_mid", Text = "You are called to a ribbon cutting. Advance there; collect salary if you pass start.", Effect = CardEffect.MoveTo, Target = midStreet },
            new CardDef { Id = "f_early", Text = "A friend needs help moving. Advance there; collect salary if you pass start.", Effect = CardEffect.MoveTo, Target = earlyStreet },
            new CardDef { Id = "f_transit", Text = "Catch the express. Advance to the first transit line.", Effect = CardEffect.MoveTo, Target = transit },
            new CardDef { Id = "f_near_transit", Text = "Rush hour! Advance to the nearest transit line. If owned, pay double fare.", Effect = CardEffect.MoveToNearestTransit },
            new CardDef { Id = "f_near_utility", Text = "Meter inspection. Advance to the nearest utility. If owned, pay ten times your roll.", Effect = CardEffect.MoveToNearestUtility },
            new CardDef { Id = "f_dividend", Text = "Your startup stake pays a dividend. Collect 50.", Effect = CardEffect.Collect, Amount = 50 },
            new CardDef { Id = "f_back3", Text = "Wrong turn. Go back three tiles.", Effect = CardEffect.MoveRelative, Amount = -3 },
            new CardDef { Id = "f_jail", Text = "Paperwork irregularities. Go directly to detention.", Effect = CardEffect.GoToJail },
            new CardDef { Id = "f_card", Text = "A lawyer owes you a favour. Keep this card to leave detention for free.", Effect = CardEffect.JailCard },
            new CardDef { Id = "f_repairs", Text = "Storm damage. Pay 25 per building and 100 per landmark.", Effect = CardEffect.PayPerBuilding, Amount = 25, Amount2 = 100 },
            new CardDef { Id = "f_fine", Text = "Speeding ticket. Pay 15.", Effect = CardEffect.Pay, Amount = 15 },
            new CardDef { Id = "f_chair", Text = "You host the neighbourhood gala. Pay each player 50.", Effect = CardEffect.PayEach, Amount = 50 },
            new CardDef { Id = "f_loan", Text = "Your investment matures. Collect 150.", Effect = CardEffect.Collect, Amount = 150 },
        });
        board.CivicCards.AddRange(new[]
        {
            new CardDef { Id = "c_start", Text = "The city thanks you. Advance to the start tile.", Effect = CardEffect.MoveTo, Target = "start" },
            new CardDef { Id = "c_bank", Text = "Accounting error in your favour. Collect 200.", Effect = CardEffect.Collect, Amount = 200 },
            new CardDef { Id = "c_doctor", Text = "Clinic visit. Pay 50.", Effect = CardEffect.Pay, Amount = 50 },
            new CardDef { Id = "c_stock", Text = "You sell some shares. Collect 50.", Effect = CardEffect.Collect, Amount = 50 },
            new CardDef { Id = "c_card", Text = "Community service record. Keep this card to leave detention for free.", Effect = CardEffect.JailCard },
            new CardDef { Id = "c_jail", Text = "Caught jaywalking, again. Go directly to detention.", Effect = CardEffect.GoToJail },
            new CardDef { Id = "c_festival", Text = "You organise a street festival. Collect 25 from every player.", Effect = CardEffect.CollectFromEach, Amount = 25 },
            new CardDef { Id = "c_refund", Text = "Tax refund. Collect 20.", Effect = CardEffect.Collect, Amount = 20 },
            new CardDef { Id = "c_insurance", Text = "Insurance payout. Collect 100.", Effect = CardEffect.Collect, Amount = 100 },
            new CardDef { Id = "c_hospital", Text = "Hospital fees. Pay 100.", Effect = CardEffect.Pay, Amount = 100 },
            new CardDef { Id = "c_school", Text = "School fund drive. Pay 50.", Effect = CardEffect.Pay, Amount = 50 },
            new CardDef { Id = "c_consult", Text = "Consulting fee received. Collect 25.", Effect = CardEffect.Collect, Amount = 25 },
            new CardDef { Id = "c_street", Text = "Street upgrade assessment. Pay 40 per building and 115 per landmark.", Effect = CardEffect.PayPerBuilding, Amount = 40, Amount2 = 115 },
            new CardDef { Id = "c_prize", Text = "Second prize in a design contest. Collect 10.", Effect = CardEffect.Collect, Amount = 10 },
            new CardDef { Id = "c_inherit", Text = "A distant relative remembers you. Collect 100.", Effect = CardEffect.Collect, Amount = 100 },
        });
    }
}
