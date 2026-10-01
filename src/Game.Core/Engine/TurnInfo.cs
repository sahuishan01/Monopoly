using Game.Core.Rules;
using Game.Core.State;

namespace Game.Core.Engine;

/// <summary>Read-only helpers answering "who has to do something right now?".</summary>
public static class TurnInfo
{
    /// <summary>Players the match is waiting on, most urgent first. Trade recipients come last.</summary>
    public static List<int> PendingActors(GameState s)
    {
        var list = new List<int>();
        if (s.IsOver) return list;
        switch (s.Phase)
        {
            case TurnPhase.Auction when s.Auction != null:
                var a = s.Auction;
                foreach (int id in a.Participants)
                {
                    bool waiting = a.Mode == AuctionMode.Rapid
                        ? !a.HasSubmitted(id)
                        : !a.Passed.Contains(id) && a.HighBidder != id;
                    if (waiting) list.Add(id);
                }
                break;
            case TurnPhase.DebtResolution when s.Debts.Count > 0:
                list.Add(s.Debts[0].Debtor);
                break;
            default:
                list.Add(s.CurrentPlayer);
                break;
        }
        foreach (var t in s.Trades)
            if (!list.Contains(t.To)) list.Add(t.To);
        return list;
    }

    /// <summary>The player whose decision blocks the turn (ignores optional trade answers).</summary>
    public static int BlockingActor(GameState s)
    {
        if (s.IsOver) return -1;
        if (s.Phase == TurnPhase.DebtResolution && s.Debts.Count > 0) return s.Debts[0].Debtor;
        if (s.Phase == TurnPhase.Auction) return -1;
        return s.CurrentPlayer;
    }
}
