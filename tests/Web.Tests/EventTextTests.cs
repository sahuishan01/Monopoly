using Game.Core.Board;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.State;
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class EventTextTests
{
    private static readonly BoardDefinition Board = BoardLibrary.Get(BoardLibrary.DefaultBoardId);
    private static readonly string[] Names = ["Ada", "Bob", "Cy"];
    private static string Cur => Board.Currency;
    private static int Street => Board.Tiles.FindIndex(t => t.Type == TileType.Street);
    private static string StreetName => Board.Tiles[Street].Name;

    private static EventLine Line(GameEvent e)
    {
        var line = EventText.Describe(e, Names, Board);
        Assert.NotNull(line);
        return line!;
    }

    [Fact]
    public void Turn_flow_events_read_as_sentences()
    {
        Assert.Equal("Round 3 begins", Line(new RoundStarted(3)).Text);
        Assert.Equal("Bob's turn", Line(new TurnStarted(1, 7)).Text);
        Assert.Equal("Ada rolled 3 + 4", Line(new DiceRolled(0, 3, 4, 0)).Text);
        Assert.Equal("Ada rolled 5 + 5 (doubles)", Line(new DiceRolled(0, 5, 5, 1)).Text);
        Assert.Equal($"Cy landed on {StreetName}", Line(new TileLanded(2, Street)).Text);
        Assert.Equal($"Ada collected {Cur}200 salary", Line(new SalaryPaid(0, 200)).Text);
    }

    [Fact]
    public void Property_events_name_the_tile_and_amount()
    {
        var bought = Line(new PropertyPurchased(1, Street, 60));
        Assert.Equal($"Bob bought {StreetName} for {Cur}60", bought.Text);
        Assert.Equal(1, bought.Actor);
        Assert.True(bought.Notable);
        Assert.Equal($"Ada received {StreetName}", Line(new PropertyGranted(0, Street)).Text);
        Assert.Equal($"Ada built on {StreetName} (level 2)", Line(new BuildingConstructed(0, Street, 2, 50, DevelopmentType.Residential, false)).Text);
        Assert.Equal($"Ada built a tower on {StreetName}", Line(new BuildingConstructed(0, Street, 5, 50, DevelopmentType.Residential, false)).Text);
        Assert.True(Line(new BuildingConstructed(0, Street, 1, 50, DevelopmentType.Residential, false)).Notable);
        Assert.Equal($"Ada sold a building on {StreetName} for {Cur}25", Line(new BuildingSold(0, Street, 0, 25)).Text);
        Assert.Equal($"Ada mortgaged {StreetName} for {Cur}30", Line(new PropertyMortgaged(0, Street, 30)).Text);
        Assert.Equal($"Ada unmortgaged {StreetName} for {Cur}33", Line(new PropertyUnmortgaged(0, Street, 33)).Text);
        var district = Board.Tiles[Street].District!;
        Assert.Equal($"Cy owns all of {Board.Districts[Board.DistrictIndex(district)].Name}", Line(new DistrictCompleted(2, district)).Text);
    }

    [Fact]
    public void Money_events_describe_payer_and_payee()
    {
        var rent = Line(new RentPaid(0, 2, Street, 18));
        Assert.Equal($"Ada paid {Cur}18 rent to Cy", rent.Text);
        Assert.Equal(EventTone.Bad, rent.Tone);
        Assert.True(rent.Notable);
        Assert.Equal($"Ada owes no rent on {StreetName}", Line(new RentWaived(0, Street, 1, false)).Text);
        Assert.Equal($"Bob paid {Cur}100 in tax", Line(new MoneyTransferred(1, -1, 100, MoneyReason.Tax)).Text);
        Assert.Equal($"Bob received {Cur}50 from the jackpot", Line(new MoneyTransferred(-1, 1, 50, MoneyReason.Jackpot)).Text);
        Assert.Equal($"Bob paid Ada {Cur}10 (card)", Line(new MoneyTransferred(1, 0, 10, MoneyReason.Card)).Text);
        Assert.Null(EventText.Describe(new MoneyTransferred(1, 0, 0, MoneyReason.Gift), Names, Board));
        Assert.Equal($"Ada owes {Cur}120 to Bob", Line(new DebtIncurred(new DebtState { Debtor = 0, Creditor = 1, Amount = 120 }, null)).Text);
        Assert.Equal($"Ada owes {Cur}120 to the bank", Line(new DebtIncurred(new DebtState { Debtor = 0, Amount = 120 }, null)).Text);
        Assert.Equal($"Ada settled a debt of {Cur}120", Line(new DebtPaid(new DebtState { Debtor = 0, Amount = 120 })).Text);
    }

    [Fact]
    public void Cards_and_jail()
    {
        var card = Line(new CardDrawn(1, TileType.Fortune, 0));
        Assert.StartsWith("Bob drew a Fortune card: “", card.Text);
        Assert.Equal("Bob drew a Civic card", Line(new CardDrawn(1, TileType.Civic, 999)).Text);
        Assert.Equal("Bob got a get-out-of-jail card", Line(new JailCardReceived(1)).Text);
        var jailed = Line(new PlayerSentToJail(2, 10));
        Assert.Equal("Cy was sent to jail", jailed.Text);
        Assert.True(jailed.Notable);
        Assert.Equal("Cy stays in jail", Line(new JailTurnFailed(2, 1)).Text);
        Assert.Equal("Cy left jail by paying the fine", Line(new PlayerReleasedFromJail(2, JailExit.Fine)).Text);
        Assert.Equal("Cy left jail with doubles", Line(new PlayerReleasedFromJail(2, JailExit.Doubles)).Text);
        Assert.Equal("Cy left jail with a card", Line(new PlayerReleasedFromJail(2, JailExit.Card)).Text);
    }

    [Fact]
    public void Auctions()
    {
        Assert.Equal($"Auction for {StreetName} started", Line(new AuctionStarted(Street, AuctionMode.Rapid, 0, [0, 1, 2])).Text);
        Assert.Equal($"Bob bid {Cur}40", Line(new BidPlaced(1, 40)).Text);
        Assert.Equal("Cy passed on the auction", Line(new AuctionPassed(2)).Text);
        var won = Line(new AuctionCompleted(Street, 1, 40, 40));
        Assert.Equal($"Bob won {StreetName} at auction for {Cur}40", won.Text);
        Assert.True(won.Notable);
        var none = Line(new AuctionCompleted(Street, -1, 0, 0));
        Assert.Equal($"No one bought {StreetName} at auction", none.Text);
        Assert.False(none.Notable);
    }

    [Fact]
    public void Trades_control_bankruptcy_and_game_end()
    {
        var offer = new TradeOffer { Id = 4, From = 0, To = 2 };
        Assert.Equal("Ada offered Cy a trade", Line(new TradeProposed(offer)).Text);
        Assert.Equal("Ada countered with a new offer to Cy", Line(new TradeCountered(3, offer)).Text);
        Assert.Equal("Cy accepted Ada's trade", Line(new TradeAccepted(offer)).Text);
        Assert.Equal("Cy rejected a trade", Line(new TradeRejected(4, 2)).Text);
        Assert.Equal("A trade offer was withdrawn", Line(new TradeCancelled(4)).Text);
        Assert.Equal("A bot took over for Bob", Line(new PlayerControlChanged(1, true)).Text);
        Assert.Equal("Bob is back in control", Line(new PlayerControlChanged(1, false)).Text);
        var broke = Line(new PlayerBankrupt(1, 0));
        Assert.Equal("Bob went bankrupt to Ada", broke.Text);
        Assert.True(broke.Notable);
        Assert.Equal("Bob went bankrupt", Line(new PlayerBankrupt(1, -1)).Text);
        var end = Line(new GameEnded([2], GameEndReason.LastStanding, []));
        Assert.Equal("Cy wins the game!", end.Text);
        Assert.Equal(2, end.Actor);
        Assert.Equal("Ada & Cy win the game!", Line(new GameEnded([0, 2], GameEndReason.LastStanding, [])).Text);
    }

    [Fact]
    public void Unknown_or_internal_events_are_omitted()
    {
        Assert.Null(EventText.Describe(new MatchStarted(), Names, Board));
        Assert.Null(EventText.Describe(new PhaseChanged(TurnPhase.PostRoll), Names, Board));
        Assert.Null(EventText.Describe(new TurnEnded(0), Names, Board));
        Assert.Null(EventText.Describe(new PlayerMoved(0, 0, 5, MoveKind.Walk, false), Names, Board));
        Assert.Null(EventText.Describe(new PurchaseOffered(0, Street, 60), Names, Board));
    }

    [Fact]
    public void State_overload_uses_player_names_and_board()
    {
        var s = new GameState { Board = Board };
        s.Players.Add(new PlayerState { Id = 0, Name = "Zed" });
        Assert.Equal($"Zed bought {StreetName} for {Cur}60", EventText.Describe(new PropertyPurchased(0, Street, 60), s)!.Text);
        Assert.Equal("Player 4 was sent to jail", EventText.Describe(new PlayerSentToJail(3, 10), s)!.Text);
    }

    [Fact]
    public void Turn_actions_follow_phase_and_seat()
    {
        var s = new GameState { Board = Board, CurrentPlayer = 0, Phase = TurnPhase.PreRoll };
        s.Players.Add(new PlayerState { Id = 0, Name = "Ada", Position = Street });
        s.Players.Add(new PlayerState { Id = 1, Name = "Bob" });
        Assert.True(TurnActions.CanRoll(s, 0));
        Assert.False(TurnActions.CanRoll(s, 1));
        Assert.False(TurnActions.CanRoll(s, -1));
        Assert.False(TurnActions.CanBuy(s, 0));
        s.Phase = TurnPhase.BuyDecision;
        Assert.True(TurnActions.CanBuy(s, 0));
        Assert.Equal(Street, TurnActions.OfferedTile(s));
        s.Phase = TurnPhase.PostRoll;
        Assert.True(TurnActions.CanEndTurn(s, 0));
        Assert.Equal(-1, TurnActions.OfferedTile(s));
        Assert.Equal(-1, TurnActions.Me([]));
        Assert.Equal(2, TurnActions.Me([2, 3]));
    }
}
