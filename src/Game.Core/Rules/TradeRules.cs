using Game.Core.Board;
using Game.Core.State;

namespace Game.Core.Rules;

public static class TradeRules
{
    /// <summary>Returns why the trade is illegal, or null when it can be executed right now.</summary>
    public static string? Validate(GameState s, int from, int to, TradeSide give, TradeSide receive)
    {
        if (!s.Rules.TradingEnabled) return "Trading is disabled";
        if (from == to) return "You cannot trade with yourself";
        if (to < 0 || to >= s.Players.Count) return "Unknown player";
        if (s.Players[from].Bankrupt || s.Players[to].Bankrupt) return "Bankrupt players cannot trade";
        if (give.IsEmpty && receive.IsEmpty) return "The offer is empty";
        return ValidateSide(s, from, give) ?? ValidateSide(s, to, receive);
    }

    private static string? ValidateSide(GameState s, int owner, TradeSide side)
    {
        var p = s.Players[owner];
        if (side.Money < 0) return "Invalid amount";
        if (side.Money > p.Money) return $"{p.Name} cannot afford that";
        if (side.JailCards < 0 || side.JailCards > p.JailCards) return $"{p.Name} does not have that card";
        if (side.Tiles.Distinct().Count() != side.Tiles.Count) return "Duplicate property";

        foreach (int t in side.Tiles)
        {
            var prop = s.Property(t);
            if (prop == null || prop.Owner != owner) return $"{p.Name} does not control that property";
            var def = s.Board.Tiles[t];
            if (def.Type == TileType.Street && Calc.DistrictHasBuildings(s, def.District))
                return "Sell the district's buildings before trading it";
        }

        if (side.Shares.Count > 0)
        {
            if (!s.Rules.PropertySharesEnabled) return "Property shares are disabled";
            if (side.Shares.Select(x => x.Tile).Distinct().Count() != side.Shares.Count) return "Duplicate share";
            foreach (var sh in side.Shares)
            {
                var prop = s.Property(sh.Tile);
                if (prop == null || prop.Owner < 0) return "That property has no shares";
                if (side.Tiles.Contains(sh.Tile)) return "Trade the property or its shares, not both";
                if (sh.Percent <= 0 || sh.Percent > prop.ShareOf(owner)) return $"{p.Name} does not hold that stake";
            }
        }

        if (side.Terms.Count > 0)
        {
            if (!s.Rules.ContractsEnabled) return "Contracts are disabled";
            if (side.Terms.Count > 4) return "Too many contract terms";
            foreach (var term in side.Terms)
            {
                string? e = ValidateTerm(s, owner, side, term);
                if (e != null) return e;
            }
        }
        return null;
    }

    private static string? ValidateTerm(GameState s, int grantor, TradeSide side, ContractTerm term)
    {
        bool ownsTile = term.Tile >= 0 && s.Property(term.Tile)?.Owner == grantor && !side.Tiles.Contains(term.Tile);
        switch (term.Kind)
        {
            case ContractKind.RentImmunity:
                if (term.Tile >= 0 && !ownsTile) return "Immunity must cover a property the grantor keeps";
                if (term.Count < 1 || term.Count > 10) return "Immunity covers 1 to 10 visits";
                return null;
            case ContractKind.RevenueShare:
                if (!ownsTile) return "Revenue share must cover a property the grantor keeps";
                if (term.Percent < 1 || term.Count < 1 || term.Count > 20) return "Invalid revenue share";
                int existing = s.Contracts
                    .Where(c => c.Kind == ContractKind.RevenueShare && c.Tile == term.Tile)
                    .Sum(c => c.Percent);
                int offered = side.Terms.Where(x => x.Kind == ContractKind.RevenueShare && x.Tile == term.Tile).Sum(x => x.Percent);
                if (existing + offered > 100) return "More than 100% of the rent would be promised";
                return null;
            case ContractKind.Installment:
                if (term.Amount <= 0 || term.Count < 1 || term.Count > 12) return "Invalid instalment plan";
                return null;
            case ContractKind.BuyOption:
                if (!ownsTile) return "An option must cover a property the grantor keeps";
                if (term.Amount <= 0 || term.Count < 1 || term.Count > 20) return "Invalid option";
                if (s.Contracts.Any(c => c.Kind == ContractKind.BuyOption && c.Tile == term.Tile))
                    return "That property is already under option";
                return null;
            default:
                return "Unknown contract term";
        }
    }
}
