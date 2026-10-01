namespace Game.Core.Board;

public enum TileType
{
    Start,
    Street,
    Transit,
    Utility,
    Tax,
    Fortune,
    Civic,
    Jail,
    GoToJail,
    Plaza,
}

public enum CardEffect
{
    Collect,
    Pay,
    MoveTo,
    MoveRelative,
    MoveToNearestTransit,
    MoveToNearestUtility,
    GoToJail,
    JailCard,
    PayPerBuilding,
    CollectFromEach,
    PayEach,
}

public sealed class TileDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public TileType Type { get; set; }
    public string? District { get; set; }
    public int Price { get; set; }
    public int HouseCost { get; set; }
    /// <summary>Street rent table: [base, 1 house, 2, 3, 4, landmark].</summary>
    public int[] Rents { get; set; } = Array.Empty<int>();
    public int TaxAmount { get; set; }

    public bool IsOwnable => Type is TileType.Street or TileType.Transit or TileType.Utility;
}

public sealed class DistrictDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#888888";
    /// <summary>Non-colour indicator for accessibility (shape/pattern key).</summary>
    public string Glyph { get; set; } = "circle";
}

public sealed class CardDef
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public CardEffect Effect { get; set; }
    public int Amount { get; set; }
    public int Amount2 { get; set; }
    /// <summary>Tile id for <see cref="CardEffect.MoveTo"/>.</summary>
    public string? Target { get; set; }
}

public sealed class BoardDefinition
{
    public string BoardId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Theme { get; set; } = "city";
    public string Currency { get; set; } = "₹";
    public int JailFine { get; set; } = 50;
    public int[] TransitRents { get; set; } = { 25, 50, 100, 200 };
    public int[] UtilityMultipliers { get; set; } = { 4, 10 };
    public List<TileDef> Tiles { get; set; } = new();
    public List<DistrictDef> Districts { get; set; } = new();
    public List<CardDef> FortuneCards { get; set; } = new();
    public List<CardDef> CivicCards { get; set; } = new();

    private int[][]? _districtTiles;
    private Dictionary<string, int>? _districtIndex;
    private Dictionary<string, int>? _tileIndex;
    private int _jail = -2;
    private bool[]? _nearTransit;

    public int Count => Tiles.Count;
    public int SideLength => Tiles.Count / 4;
    public int SideOf(int tile) => Math.Min(3, tile / Math.Max(1, SideLength));

    public int JailIndex
    {
        get
        {
            if (_jail == -2) _jail = Tiles.FindIndex(t => t.Type == TileType.Jail);
            return _jail;
        }
    }

    public int IndexOfTile(string id)
    {
        _tileIndex ??= Tiles.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return _tileIndex.TryGetValue(id, out var i) ? i : -1;
    }

    public int DistrictIndex(string? id)
    {
        if (id == null) return -1;
        _districtIndex ??= Districts.Select((d, i) => (d.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return _districtIndex.TryGetValue(id, out var i) ? i : -1;
    }

    /// <summary>Tile indices of every street in the given district.</summary>
    public int[] DistrictTiles(string? district)
    {
        int di = DistrictIndex(district);
        if (di < 0) return Array.Empty<int>();
        _districtTiles ??= Districts
            .Select(d => Enumerable.Range(0, Tiles.Count).Where(i => Tiles[i].District == d.Id).ToArray())
            .ToArray();
        return _districtTiles[di];
    }

    public bool IsNearTransit(int tile)
    {
        if (_nearTransit == null)
        {
            var arr = new bool[Tiles.Count];
            for (int i = 0; i < Tiles.Count; i++)
            {
                if (Tiles[i].Type != TileType.Transit) continue;
                for (int d = -2; d <= 2; d++)
                {
                    int j = ((i + d) % Tiles.Count + Tiles.Count) % Tiles.Count;
                    if (Tiles[j].Type == TileType.Street) arr[j] = true;
                }
            }
            _nearTransit = arr;
        }
        return _nearTransit[tile];
    }

    public IEnumerable<int> TilesOfType(TileType type)
    {
        for (int i = 0; i < Tiles.Count; i++)
            if (Tiles[i].Type == type) yield return i;
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> when the definition cannot be played.</summary>
    public void Validate()
    {
        void Fail(string m) => throw new InvalidOperationException($"Board '{BoardId}': {m}");
        if (Tiles.Count < 12 || Tiles.Count % 4 != 0) Fail("tile count must be a multiple of 4 and at least 12");
        if (Tiles[0].Type != TileType.Start) Fail("tile 0 must be the start tile");
        if (Tiles.Count(t => t.Type == TileType.Jail) != 1) Fail("exactly one jail tile is required");
        if (Tiles.Select(t => t.Id).Distinct().Count() != Tiles.Count) Fail("tile ids must be unique");
        foreach (var t in Tiles)
        {
            if (t.Type == TileType.Street)
            {
                if (DistrictIndex(t.District) < 0) Fail($"street '{t.Id}' has unknown district");
                if (t.Rents.Length != 6) Fail($"street '{t.Id}' needs 6 rent values");
                if (t.Price <= 0 || t.HouseCost <= 0) Fail($"street '{t.Id}' needs price and house cost");
            }
            else if (t.IsOwnable && t.Price <= 0) Fail($"'{t.Id}' needs a price");
        }
        int transit = Tiles.Count(t => t.Type == TileType.Transit);
        if (transit > TransitRents.Length) Fail("not enough transit rent entries");
        int util = Tiles.Count(t => t.Type == TileType.Utility);
        if (util > UtilityMultipliers.Length) Fail("not enough utility multipliers");
        if (Tiles.Any(t => t.Type == TileType.Fortune) && FortuneCards.Count == 0) Fail("fortune deck is empty");
        if (Tiles.Any(t => t.Type == TileType.Civic) && CivicCards.Count == 0) Fail("civic deck is empty");
        foreach (var c in FortuneCards.Concat(CivicCards))
            if (c.Effect == CardEffect.MoveTo && (c.Target == null || IndexOfTile(c.Target) < 0))
                Fail($"card '{c.Id}' targets unknown tile");
    }
}
