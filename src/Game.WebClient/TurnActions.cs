using Game.Core.State;

namespace Game.WebClient;

/// <summary>Which primary turn actions the local seat may take right now. Mirrors the buttons the HUD shows; the host still validates.</summary>
public static class TurnActions
{
    /// <summary>The seat this browser controls (first seat only), or -1 for spectators.</summary>
    public static int Me(int[] seats) => seats.Length > 0 ? seats[0] : -1;

    static bool MyTurn(GameState? s, int me) =>
        s != null && me >= 0 && me < s.Players.Count && s.CurrentPlayer == me && !s.Players[me].Bankrupt && !s.IsOver;

    public static bool CanRoll(GameState? s, int me) => MyTurn(s, me) && s!.Phase == TurnPhase.PreRoll;
    public static bool CanBuy(GameState? s, int me) => MyTurn(s, me) && s!.Phase == TurnPhase.BuyDecision;
    public static bool CanEndTurn(GameState? s, int me) => MyTurn(s, me) && s!.Phase == TurnPhase.PostRoll;

    /// <summary>The tile a pending purchase offer is for (the current player's square), or -1.</summary>
    public static int OfferedTile(GameState? s) =>
        s != null && s.Phase == TurnPhase.BuyDecision && s.CurrentPlayer >= 0 && s.CurrentPlayer < s.Players.Count
            ? s.Players[s.CurrentPlayer].Position : -1;
}
