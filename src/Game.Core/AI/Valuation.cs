using Game.Core.Board;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.State;

namespace Game.Core.AI;

/// <summary>Heuristic position evaluation shared by every bot difficulty.</summary>
public static class Valuation
{
    /// <summary>What completing the district is worth beyond the face value of its tiles.</summary>
    public static int DistrictBonus(GameState s, string district)
    {
        int bonus = 0;
        foreach (int t in s.Board.DistrictTiles(district))
        {
            var def = s.Board.Tiles[t];
            bonus += def.Rents[3] * 8 / 10;
            if (s.Properties[t]!.Mortgaged) bonus -= def.Rents[3] * 3 / 10;
        }
        return bonus;
    }

    /// <summary>How much the player would value acquiring the tile right now.</summary>
    public static int TileValue(GameState s, int tile, int player)
    {
        var def = s.Board.Tiles[tile];
        int value = def.Price;
        switch (def.Type)
        {
            case TileType.Street:
                var tiles = s.Board.DistrictTiles(def.District);
                int mine = 0, bank = 0;
                int firstOther = -1;
                bool split = false;
                foreach (int t in tiles)
                {
                    if (t == tile) continue;
                    int o = s.Properties[t]!.Owner;
                    if (o == player) mine++;
                    else if (o < 0) bank++;
                    else if (firstOther < 0) firstOther = o;
                    else if (firstOther != o) split = true;
                }
                int others = tiles.Length - 1 - mine - bank;
                if (mine == tiles.Length - 1) value += DistrictBonus(s, def.District!) * 6 / 10 + def.Price / 2;
                else if (mine > 0 && others == 0) value += def.Price * 3 / 10 * mine;
                else if (others == tiles.Length - 1 && !split) value += def.Price * 6 / 10;
                else if (others > 0 && mine > 0) value -= def.Price / 10;
                break;
            case TileType.Transit:
                value += 50 * Calc.CountOwned(s, player, TileType.Transit);
                break;
            case TileType.Utility:
                value = value * 9 / 10 + 30 * Calc.CountOwned(s, player, TileType.Utility);
                break;
        }
        int pct = Calc.EffectPercent(s, tile, e => e.RentPercent);
        return Math.Max(10, value * (100 + pct / 2) / 100);
    }

    public static int ContractValue(GameState s, Contract c)
    {
        switch (c.Kind)
        {
            case ContractKind.RentImmunity:
                int rent = c.Tile >= 0
                    ? Calc.Rent(s, c.Tile, 7)
                    : (int)s.OwnedBy(c.Grantor).Select(p => Calc.Rent(s, p.Tile, 7)).DefaultIfEmpty(0).Average();
                return rent * c.Remaining / 2;
            case ContractKind.RevenueShare:
                return Calc.Rent(s, c.Tile, 7) * c.Percent / 100 * c.Remaining * 4 / 10;
            case ContractKind.Installment:
                return c.Amount * c.Remaining * 9 / 10;
            case ContractKind.BuyOption:
                return Math.Max(0, TileValue(s, c.Tile, c.Beneficiary) - c.Amount) / 2;
            default:
                return 0;
        }
    }

    /// <summary>Overall strength of a player's position, in currency units.</summary>
    public static int Strength(GameState s, int player)
    {
        var pl = s.Players[player];
        if (pl.Bankrupt) return 0;
        int total = pl.Money + pl.JailCards * 30;
        foreach (var p in s.Properties)
        {
            if (p == null || p.Owner < 0) continue;
            int share = p.ShareOf(player);
            if (share == 0) continue;
            var def = s.Board.Tiles[p.Tile];
            total += (p.Mortgaged ? def.Price / 2 : def.Price) * share / 100;
            if (p.Owner != player) continue;
            total += Calc.BuildingValue(s, p);
            if (def.Type == TileType.Transit) total += 20 * Calc.CountOwned(s, player, TileType.Transit);
        }
        foreach (var d in s.Board.Districts)
        {
            var tiles = s.Board.DistrictTiles(d.Id);
            int mine = 0;
            foreach (int t in tiles)
                if (s.Properties[t]!.Owner == player) mine++;
            if (mine == tiles.Length) total += DistrictBonus(s, d.Id);
            else if (mine > 1) total += (mine - 1) * s.Board.Tiles[tiles[0]].Price / 5;
        }
        foreach (var c in s.Contracts)
        {
            if (c.Beneficiary == player) total += ContractValue(s, c);
            else if (c.Grantor == player) total -= ContractValue(s, c);
        }
        return total;
    }

    /// <summary>
    /// Raw change in strength for the proposer and the recipient, measured by applying the trade
    /// to a copy of the state.
    /// </summary>
    public static (int From, int To) TradeRawDeltas(GameState s, TradeOffer offer)
    {
        var after = s.Clone();
        new TradeAccepted(offer).Apply(after);
        return (Strength(after, offer.From) - Strength(s, offer.From),
                Strength(after, offer.To) - Strength(s, offer.To));
    }

    /// <summary>
    /// Net benefit of the offer for <paramref name="player"/>: own gain minus a share of the
    /// counterpart's gain (rivalry).
    /// </summary>
    public static int TradeDelta(GameState s, TradeOffer offer, int player, int rivalryPercent)
    {
        var (from, to) = TradeRawDeltas(s, offer);
        return offer.From == player ? from - to * rivalryPercent / 100 : to - from * rivalryPercent / 100;
    }

    /// <summary>Largest rent the player could be asked to pay on the next roll.</summary>
    public static int MaxRentExposure(GameState s, int player)
    {
        int max = 0;
        foreach (var p in s.Properties)
        {
            if (p == null || p.Owner < 0 || p.Owner == player || p.Mortgaged) continue;
            if (s.SameTeam(player, p.Owner) && s.Rules.TeamRentExempt) continue;
            max = Math.Max(max, Calc.Rent(s, p.Tile, 7));
        }
        return max;
    }
}
