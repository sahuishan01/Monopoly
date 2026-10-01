using Game.Core.Board;
using Game.Core.State;

namespace Game.Core.Rules;

/// <summary>Pure read-only rule queries shared by the engine, the bots and the UI.</summary>
public static class Calc
{
    public static int DevCostPercent(DevelopmentType t) => t switch
    {
        DevelopmentType.Commercial => 125,
        DevelopmentType.Luxury => 150,
        DevelopmentType.Industrial => 70,
        _ => 100,
    };

    public static int DevRentPercent(DevelopmentType t) => t switch
    {
        DevelopmentType.Commercial => 130,
        DevelopmentType.Luxury => 160,
        DevelopmentType.Industrial => 80,
        _ => 100,
    };

    public static bool EffectApplies(GameState s, ActiveEffect e, int tile)
    {
        var def = s.Board.Tiles[tile];
        return e.Scope switch
        {
            EffectScope.All => true,
            EffectScope.District => def.District == e.ScopeValue,
            EffectScope.TileType => def.Type.ToString() == e.ScopeValue,
            EffectScope.Side => s.Board.SideOf(tile).ToString() == e.ScopeValue,
            EffectScope.NearTransit => s.Board.IsNearTransit(tile),
            _ => false,
        };
    }

    public static int EffectPercent(GameState s, int tile, Func<ActiveEffect, int> selector)
    {
        int pct = 0;
        foreach (var e in s.Effects)
        {
            int v = selector(e);
            if (v != 0 && EffectApplies(s, e, tile)) pct += v;
        }
        return pct;
    }

    public static bool OwnsDistrict(GameState s, int player, string? district)
    {
        var tiles = s.Board.DistrictTiles(district);
        if (tiles.Length == 0) return false;
        foreach (int t in tiles)
            if (s.Properties[t]!.Owner != player) return false;
        return true;
    }

    public static int CountOwned(GameState s, int player, TileType type)
    {
        int n = 0;
        for (int i = 0; i < s.Board.Count; i++)
            if (s.Board.Tiles[i].Type == type && s.Properties[i]!.Owner == player) n++;
        return n;
    }

    /// <summary>Total rent a visitor owes before immunities and share splitting.</summary>
    public static int Rent(GameState s, int tile, int diceSum, bool cardBonus = false)
    {
        var def = s.Board.Tiles[tile];
        var p = s.Properties[tile];
        if (p == null || p.Owner < 0 || p.Mortgaged) return 0;
        int rent;
        switch (def.Type)
        {
            case TileType.Street:
                if (p.Level > 0)
                {
                    rent = def.Rents[p.Level];
                    if (s.Rules.AdvancedDevelopment) rent = rent * DevRentPercent(p.DevType) / 100;
                }
                else
                {
                    rent = OwnsDistrict(s, p.Owner, def.District) ? def.Rents[0] * 2 : def.Rents[0];
                }
                break;
            case TileType.Transit:
                rent = s.Board.TransitRents[Math.Max(1, CountOwned(s, p.Owner, TileType.Transit)) - 1];
                if (cardBonus) rent *= 2;
                break;
            case TileType.Utility:
                int mult = cardBonus
                    ? 10
                    : s.Board.UtilityMultipliers[Math.Max(1, CountOwned(s, p.Owner, TileType.Utility)) - 1];
                rent = diceSum * mult;
                break;
            default:
                return 0;
        }
        int pct = EffectPercent(s, tile, e => e.RentPercent);
        if (s.Rules.AdvancedDevelopment && p.Level > 0 && p.DevType == DevelopmentType.Luxury) pct *= 2;
        return Math.Max(0, rent * (100 + pct) / 100);
    }

    public static int PurchasePrice(GameState s, int tile)
    {
        int price = s.Board.Tiles[tile].Price;
        int pct = EffectPercent(s, tile, e => e.PricePercent);
        return Math.Max(1, price * (100 + pct) / 100);
    }

    public static int MortgageValue(GameState s, int tile) => s.Board.Tiles[tile].Price / 2;

    public static int UnmortgageCost(GameState s, int tile, int player)
    {
        int value = MortgageValue(s, tile);
        int pct = s.Rules.MortgageInterestPercent;
        if (s.Rules.AbilitiesEnabled && s.Players[player].Ability == Ability.Banker) pct /= 2;
        return value + value * pct / 100;
    }

    public static bool BuilderDiscountAvailable(GameState s, int player) =>
        s.Rules.AbilitiesEnabled && s.Players[player].Ability == Ability.Builder &&
        s.Players[player].BuilderDiscountRound != s.Round;

    public static int BuildCost(GameState s, int tile, int player, DevelopmentType type)
    {
        int cost = s.Board.Tiles[tile].HouseCost * s.Rules.HouseCostPercent / 100;
        if (s.Rules.AdvancedDevelopment) cost = cost * DevCostPercent(type) / 100;
        cost = cost * (100 + EffectPercent(s, tile, e => e.BuildCostPercent)) / 100;
        if (BuilderDiscountAvailable(s, player)) cost = cost * 90 / 100;
        return Math.Max(1, cost);
    }

    public static int SellRefund(GameState s, int tile) =>
        s.Board.Tiles[tile].HouseCost * s.Rules.HouseCostPercent / 100 / 2;

    public static int TaxAmount(GameState s, int tile)
    {
        int pct = EffectPercent(s, tile, e => e.TaxPercent);
        return Math.Max(0, s.Board.Tiles[tile].TaxAmount * (100 + pct) / 100);
    }

    public static bool DistrictHasBuildings(GameState s, string? district)
    {
        foreach (int t in s.Board.DistrictTiles(district))
            if (s.Properties[t]!.Level > 0) return true;
        return false;
    }

    /// <summary>Why the player cannot build on the tile, or null when building is legal.</summary>
    public static string? BuildError(GameState s, int player, int tile)
    {
        var def = s.Board.Tiles[tile];
        var p = s.Property(tile);
        if (p == null || def.Type != TileType.Street) return "Only streets can be developed";
        if (p.Owner != player) return "You do not control this property";
        if (!OwnsDistrict(s, player, def.District)) return "You must control the whole district";
        if (p.Level >= 5) return "Already fully developed";
        int min = 5;
        foreach (int t in s.Board.DistrictTiles(def.District))
        {
            var q = s.Properties[t]!;
            if (q.Mortgaged) return "A property in this district is mortgaged";
            min = Math.Min(min, q.Level);
        }
        if (s.Rules.EvenBuild && p.Level > min) return "Build evenly across the district";
        if (p.Level == 4 ? s.Bank.HotelsLeft <= 0 : s.Bank.HousesLeft <= 0) return "The bank has no buildings left";
        return null;
    }

    public static string? SellError(GameState s, int player, int tile)
    {
        var def = s.Board.Tiles[tile];
        var p = s.Property(tile);
        if (p == null || p.Owner != player) return "You do not control this property";
        if (p.Level <= 0) return "Nothing to sell";
        if (s.Rules.EvenBuild)
        {
            int max = 0;
            foreach (int t in s.Board.DistrictTiles(def.District)) max = Math.Max(max, s.Properties[t]!.Level);
            if (p.Level < max) return "Sell evenly across the district";
        }
        return null;
    }

    public static string? MortgageError(GameState s, int player, int tile)
    {
        var def = s.Board.Tiles[tile];
        var p = s.Property(tile);
        if (p == null || p.Owner != player) return "You do not control this property";
        if (p.Mortgaged) return "Already mortgaged";
        if (p.ShareOf(player) < 100) return "Shared properties cannot be mortgaged";
        if (def.Type == TileType.Street && DistrictHasBuildings(s, def.District)) return "Sell the district's buildings first";
        return null;
    }

    public static int BuildingValue(GameState s, PropertyState p) =>
        p.Level * (s.Board.Tiles[p.Tile].HouseCost * s.Rules.HouseCostPercent / 100);

    public static int NetWorth(GameState s, int player)
    {
        var pl = s.Players[player];
        if (pl.Bankrupt) return 0;
        int total = pl.Money;
        foreach (var p in s.Properties)
        {
            if (p == null || p.Owner < 0) continue;
            int share = p.ShareOf(player);
            if (share == 0) continue;
            int price = s.Board.Tiles[p.Tile].Price;
            total += (p.Mortgaged ? price / 2 : price) * share / 100;
            if (p.Owner == player) total += BuildingValue(s, p);
        }
        return total;
    }

    /// <summary>Cash the player could raise by selling every building and mortgaging everything.</summary>
    public static int LiquidationValue(GameState s, int player)
    {
        int total = s.Players[player].Money;
        foreach (var p in s.OwnedBy(player))
        {
            total += BuildingValue(s, p) / 2;
            if (!p.Mortgaged && p.ShareOf(player) >= 100) total += MortgageValue(s, p.Tile);
        }
        return total;
    }

    public static List<Standing> Standings(GameState s)
    {
        var list = s.Players
            .Select(p => new Standing { Player = p.Id, NetWorth = NetWorth(s, p.Id) })
            .OrderByDescending(x => s.Players[x.Player].Bankrupt ? -1 : x.NetWorth)
            .ThenByDescending(x => s.Players[x.Player].Stats.EliminatedOnTurn)
            .ThenBy(x => x.Player)
            .ToList();
        for (int i = 0; i < list.Count; i++) list[i].Rank = i + 1;
        return list;
    }
}
