using Game.Core.Events;

namespace Game.WebClient;

public enum CueKind { None, Dice, Move, Buy, Build, Rent, Cash, Jail, Card, Auction, Trade, Bankrupt, Win, Turn }

public readonly record struct Cue(CueKind Kind, string Sound, double Seconds);

public static class Cues
{
    public static Cue For(GameEvent e) => e switch
    {
        DiceRolled => new(CueKind.Dice, "dice", 0.9),
        PlayerMoved => new(CueKind.Move, "hop", 0.7),
        PropertyPurchased => new(CueKind.Buy, "buy", 0.5),
        BuildingConstructed => new(CueKind.Build, "build", 0.6),
        BuildingSold => new(CueKind.Build, "sell", 0.4),
        RentPaid or MoneyTransferred or SalaryPaid => new(CueKind.Cash, "cash", 0.5),
        PlayerSentToJail => new(CueKind.Jail, "jail", 0.8),
        CardDrawn => new(CueKind.Card, "card", 0.6),
        AuctionStarted or AuctionCompleted => new(CueKind.Auction, "hammer", 0.6),
        TradeAccepted => new(CueKind.Trade, "trade", 0.5),
        PlayerBankrupt => new(CueKind.Bankrupt, "bankrupt", 0.9),
        GameEnded => new(CueKind.Win, "win", 1.2),
        TurnStarted => new(CueKind.Turn, "chime", 0.3),
        _ => new(CueKind.None, "", 0.2),
    };

    /// <summary>For large bursts (resync) keeps at most <paramref name="maxPerKind"/> cues of each kind.</summary>
    public static IReadOnlyList<Cue> Collapse(IEnumerable<GameEvent> batch, int maxPerKind = 3)
    {
        var seen = new Dictionary<CueKind, int>();
        var result = new List<Cue>();
        foreach (var e in batch)
        {
            var c = For(e);
            if (c.Kind == CueKind.None) continue;
            seen[c.Kind] = seen.GetValueOrDefault(c.Kind) + 1;
            if (seen[c.Kind] <= maxPerKind) result.Add(c);
        }
        return result;
    }
}
