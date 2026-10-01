using Game.Core.Board;
using Game.Core.State;

namespace Game.Core.Rules;

public enum ObjectiveKind
{
    OwnAnyDistrict,
    EarnRent,
    CompleteTrades,
    OwnOnEverySide,
    WinAuctions,
    BuildHouses,
    HoldCash,
    OwnTransits,
    OwnProperties,
    CollectSalary,
}

public sealed record ObjectiveDef(int Id, string Text, ObjectiveKind Kind, int Target, int Reward);

public static class ObjectiveCatalog
{
    public static readonly ObjectiveDef[] All =
    {
        new(0, "Control an entire district.", ObjectiveKind.OwnAnyDistrict, 1, 250),
        new(1, "Earn 600 in rent.", ObjectiveKind.EarnRent, 600, 250),
        new(2, "Complete two trades.", ObjectiveKind.CompleteTrades, 2, 200),
        new(3, "Own property on every side of the board.", ObjectiveKind.OwnOnEverySide, 4, 200),
        new(4, "Win two auctions.", ObjectiveKind.WinAuctions, 2, 150),
        new(5, "Construct five buildings.", ObjectiveKind.BuildHouses, 5, 300),
        new(6, "Hold 2,000 in cash.", ObjectiveKind.HoldCash, 2000, 200),
        new(7, "Own two transit lines.", ObjectiveKind.OwnTransits, 2, 150),
        new(8, "Own six properties.", ObjectiveKind.OwnProperties, 6, 200),
        new(9, "Collect salary five times.", ObjectiveKind.CollectSalary, 5, 150),
    };

    public static ObjectiveDef Get(int id) => All[id];

    public static bool IsSatisfied(GameState s, int player, ObjectiveDef def)
    {
        var p = s.Players[player];
        switch (def.Kind)
        {
            case ObjectiveKind.OwnAnyDistrict:
                return s.Board.Districts.Any(d => Calc.OwnsDistrict(s, player, d.Id));
            case ObjectiveKind.EarnRent:
                return p.Stats.RentEarned >= def.Target;
            case ObjectiveKind.CompleteTrades:
                return p.Stats.TradesCompleted >= def.Target;
            case ObjectiveKind.OwnOnEverySide:
                int mask = 0;
                foreach (var prop in s.OwnedBy(player)) mask |= 1 << s.Board.SideOf(prop.Tile);
                return mask == 0b1111;
            case ObjectiveKind.WinAuctions:
                return p.Stats.AuctionsWon >= def.Target;
            case ObjectiveKind.BuildHouses:
                return p.Stats.HousesBuilt >= def.Target;
            case ObjectiveKind.HoldCash:
                return p.Money >= def.Target;
            case ObjectiveKind.OwnTransits:
                return Calc.CountOwned(s, player, TileType.Transit) >= def.Target;
            case ObjectiveKind.OwnProperties:
                return s.OwnedBy(player).Count() >= def.Target;
            case ObjectiveKind.CollectSalary:
                return p.Stats.SalaryCollected >= def.Target * s.Rules.Salary;
            default:
                return false;
        }
    }
}
