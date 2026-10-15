using Game.Core.Board;
using Game.Core.Events;
using Game.Core.State;

namespace Game.WebClient;

public enum EventTone { Neutral, Good, Bad }

/// <summary>One human-readable log line. <see cref="Actor"/> is the seat the line is about (-1 for none) and colours its dot.
/// <see cref="Notable"/> lines are also shown as toasts.</summary>
public sealed record EventLine(string Text, int Actor, EventTone Tone, bool Notable);

/// <summary>Turns game events into short sentences for the event log and toasts. Pure: reads names from the given state only.</summary>
public static class EventText
{
    /// <summary>The sentence for <paramref name="e"/>, or null for events not worth showing (unknown or internal).</summary>
    public static EventLine? Describe(GameEvent e, GameState s) => Describe(e, s.Players.Select(p => p.Name).ToList(), s.Board);

    public static EventLine? Describe(GameEvent e, IReadOnlyList<string> names, BoardDefinition? board)
    {
        string cur = board?.Currency ?? "";
        string P(int id) => id >= 0 && id < names.Count ? names[id] : id < 0 ? "The bank" : "Player " + (id + 1);
        string T(int tile) => board != null && tile >= 0 && tile < board.Tiles.Count ? board.Tiles[tile].Name : "tile " + tile;
        string M(int amount) => cur + amount;
        EventLine L(string text, int actor, EventTone tone = EventTone.Neutral, bool notable = false) => new(text, actor, tone, notable);

        return e switch
        {
            RoundStarted r => L($"Round {r.Round} begins", -1),
            TurnStarted t => L($"{P(t.Player)}'s turn", t.Player),
            DiceRolled d => L($"{P(d.Player)} rolled {d.D1} + {d.D2}" + (d.D1 == d.D2 ? " (doubles)" : ""), d.Player),
            TileLanded l => L($"{P(l.Player)} landed on {T(l.Tile)}", l.Player),
            SalaryPaid sp => L($"{P(sp.Player)} collected {M(sp.Amount)} salary", sp.Player, EventTone.Good),
            PropertyGranted g => L($"{P(g.Player)} received {T(g.Tile)}", g.Player, EventTone.Good),
            PropertyPurchased b => L($"{P(b.Player)} bought {T(b.Tile)} for {M(b.Price)}", b.Player, EventTone.Good, true),
            DistrictCompleted dc => L($"{P(dc.Player)} owns all of {District(board, dc.District)}", dc.Player, EventTone.Good, true),
            BuildingConstructed bc => L(bc.Level >= 5
                    ? $"{P(bc.Player)} built a tower on {T(bc.Tile)}"
                    : $"{P(bc.Player)} built on {T(bc.Tile)} (level {bc.Level})", bc.Player, EventTone.Good, true),
            BuildingSold bs => L($"{P(bs.Player)} sold a building on {T(bs.Tile)} for {M(bs.Refund)}", bs.Player),
            PropertyMortgaged pm => L($"{P(pm.Player)} mortgaged {T(pm.Tile)} for {M(pm.Amount)}", pm.Player),
            PropertyUnmortgaged pu => L($"{P(pu.Player)} unmortgaged {T(pu.Tile)} for {M(pu.Cost)}", pu.Player),
            RentPaid rp => L($"{P(rp.Payer)} paid {M(rp.Amount)} rent to {P(rp.Payee)}", rp.Payer, EventTone.Bad, true),
            RentWaived rw => L($"{P(rw.Player)} owes no rent on {T(rw.Tile)}", rw.Player),
            MoneyTransferred mt when mt.Amount > 0 => Transfer(mt, P, M),
            DebtIncurred di => L($"{P(di.Debt.Debtor)} owes {M(di.Debt.Amount)}" + (di.Debt.Creditor >= 0 ? " to " + P(di.Debt.Creditor) : " to the bank"),
                di.Debt.Debtor, EventTone.Bad),
            DebtPaid dp => L($"{P(dp.Debt.Debtor)} settled a debt of {M(dp.Debt.Amount)}", dp.Debt.Debtor),
            CardDrawn cd => L($"{P(cd.Player)} drew a {(cd.Deck == TileType.Civic ? "Civic" : "Fortune")} card" + CardText(board, cd), cd.Player),
            JailCardReceived jc => L($"{P(jc.Player)} got a get-out-of-jail card", jc.Player, EventTone.Good),
            PlayerSentToJail j => L($"{P(j.Player)} was sent to jail", j.Player, EventTone.Bad, true),
            JailTurnFailed jf => L($"{P(jf.Player)} stays in jail", jf.Player),
            PlayerReleasedFromJail jr => L($"{P(jr.Player)} left jail" + jr.Reason switch
            {
                JailExit.Doubles => " with doubles",
                JailExit.Fine => " by paying the fine",
                JailExit.Card => " with a card",
                _ => "",
            }, jr.Player, EventTone.Good),
            AuctionStarted a => L($"Auction for {T(a.Tile)} started", a.StartedBy),
            BidPlaced bp => L($"{P(bp.Player)} bid {M(bp.Amount)}", bp.Player),
            AuctionPassed ap => L($"{P(ap.Player)} passed on the auction", ap.Player),
            AuctionCompleted ac => ac.Winner < 0
                ? L($"No one bought {T(ac.Tile)} at auction", -1)
                : L($"{P(ac.Winner)} won {T(ac.Tile)} at auction for {M(ac.Paid)}", ac.Winner, EventTone.Good, true),
            TradeProposed tp => L($"{P(tp.Offer.From)} offered {P(tp.Offer.To)} a trade", tp.Offer.From),
            TradeCountered tc => L($"{P(tc.Offer.From)} countered with a new offer to {P(tc.Offer.To)}", tc.Offer.From),
            TradeAccepted ta => L($"{P(ta.Offer.To)} accepted {P(ta.Offer.From)}'s trade", ta.Offer.To, EventTone.Good, true),
            TradeRejected tr => L($"{P(tr.By)} rejected a trade", tr.By),
            TradeCancelled => L("A trade offer was withdrawn", -1),
            PlayerControlChanged pc => L(pc.IsBot ? $"A bot took over for {P(pc.Player)}" : $"{P(pc.Player)} is back in control", pc.Player),
            PlayerBankrupt pb => L($"{P(pb.Player)} went bankrupt" + (pb.Creditor >= 0 ? " to " + P(pb.Creditor) : ""), pb.Player, EventTone.Bad, true),
            GameEnded ge => L(ge.Winners.Length == 0 ? "The game ended"
                    : string.Join(" & ", ge.Winners.Select(P)) + (ge.Winners.Length == 1 ? " wins the game!" : " win the game!"),
                ge.Winners.Length > 0 ? ge.Winners[0] : -1, EventTone.Good, true),
            _ => null,
        };
    }

    private static EventLine Transfer(MoneyTransferred t, Func<int, string> p, Func<int, string> m)
    {
        string why = t.Reason switch
        {
            MoneyReason.Tax => " in tax",
            MoneyReason.JailFine => " jail fine",
            MoneyReason.Card => " (card)",
            MoneyReason.Contract => " (contract)",
            MoneyReason.ProjectBonus => " project bonus",
            MoneyReason.Jackpot => " from the jackpot",
            _ => "",
        };
        if (t.From >= 0 && t.To >= 0) return new($"{p(t.From)} paid {p(t.To)} {m(t.Amount)}{why}", t.From, EventTone.Neutral, false);
        if (t.From >= 0) return new($"{p(t.From)} paid {m(t.Amount)}{why}", t.From, EventTone.Bad, false);
        return new($"{p(t.To)} received {m(t.Amount)}{why}", t.To, EventTone.Good, false);
    }

    private static string District(BoardDefinition? board, string id)
    {
        int i = board?.DistrictIndex(id) ?? -1;
        return i >= 0 ? board!.Districts[i].Name : id;
    }

    private static string CardText(BoardDefinition? board, CardDrawn cd)
    {
        var deck = cd.Deck == TileType.Civic ? board?.CivicCards : board?.FortuneCards;
        return deck != null && cd.CardIndex >= 0 && cd.CardIndex < deck.Count && deck[cd.CardIndex].Text.Length > 0
            ? ": “" + deck[cd.CardIndex].Text + "”" : "";
    }
}
