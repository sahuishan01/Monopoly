using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.State;

namespace Core.Tests;

public class DebtAndTradeTests
{
    [Fact]
    public void Unpayable_rent_opens_debt_resolution_and_mortgage_settles_it()
    {
        var g = new TestGame();
        g.Give(1, 37, 39);
        g.Prop(39).Level = 5;
        g.Give(0, 21);
        g.P(0).Money = 1900;
        g.P(0).Position = 36;
        g.Roll(1, 2);
        Assert.Equal(TurnPhase.DebtResolution, g.S.Phase);
        Assert.Equal(2000, g.S.Debts[0].Amount);
        Assert.Equal(1900, g.P(0).Money);
        g.Fail(new EndTurnCommand(0));

        g.Ok(new MortgageCommand(0, 21));
        Assert.Empty(g.S.Debts);
        Assert.Equal(10, g.P(0).Money);
        Assert.Equal(3500, g.P(1).Money);
        Assert.Equal(TurnPhase.PostRoll, g.S.Phase);
    }

    [Fact]
    public void Bankruptcy_hands_assets_to_the_creditor()
    {
        var g = new TestGame(players: 3);
        g.Give(1, 37, 39);
        g.Prop(39).Level = 5;
        g.Give(0, 21, 23, 24);
        g.Prop(21).Level = 1;
        g.S.Bank.HousesLeft--;
        g.P(0).Money = 100;
        g.P(0).JailCards = 1;
        g.P(0).Position = 36;
        g.Roll(1, 2);
        g.Ok(new DeclareBankruptcyCommand(0));

        Assert.True(g.P(0).Bankrupt);
        Assert.Equal(1, g.Prop(21).Owner);
        Assert.Equal(0, g.Prop(21).Level);
        Assert.Equal(32, g.S.Bank.HousesLeft);
        Assert.Equal(1500 + 100 + 75, g.P(1).Money);
        Assert.Equal(1, g.P(1).JailCards);
        Assert.Equal(1, g.S.CurrentPlayer);
        Assert.Contains(g.Log.OfType<DistrictCompleted>(), e => e.Player == 1 && e.District == "civic");
    }

    [Fact]
    public void Bankruptcy_to_the_bank_frees_the_properties()
    {
        var g = new TestGame(players: 3);
        g.Give(0, 21);
        g.Prop(21).Mortgaged = true;
        g.P(0).Money = 10;
        g.Roll(1, 3);
        Assert.Equal(TurnPhase.DebtResolution, g.S.Phase);
        g.Ok(new DeclareBankruptcyCommand(0));
        Assert.Equal(-1, g.Prop(21).Owner);
        Assert.False(g.Prop(21).Mortgaged);
    }

    [Fact]
    public void Only_a_debtor_or_the_current_player_may_declare_bankruptcy()
    {
        var g = new TestGame();
        g.Fail(new DeclareBankruptcyCommand(2));
    }

    [Fact]
    public void Classic_auction_winner_pays_the_bid()
    {
        var g = new TestGame(players: 3);
        g.Roll(1, 2);
        g.Ok(new DeclinePropertyCommand(0));
        Assert.Equal(TurnPhase.Auction, g.S.Phase);
        g.Ok(new PlaceBidCommand(1, 30));
        Assert.Equal("Minimum bid is 40", g.Fail(new PlaceBidCommand(2, 35)));
        g.Ok(new PlaceBidCommand(2, 50));
        g.Ok(new PassAuctionCommand(0));
        Assert.Equal("You hold the high bid", g.Fail(new PassAuctionCommand(2)));
        g.Ok(new PassAuctionCommand(1));
        Assert.Null(g.S.Auction);
        Assert.Equal(2, g.Prop(3).Owner);
        Assert.Equal(1450, g.P(2).Money);
        Assert.Equal(TurnPhase.PostRoll, g.S.Phase);
        Assert.Equal(1, g.P(2).Stats.AuctionsWon);
    }

    [Fact]
    public void Auction_with_no_bids_leaves_the_property_unowned()
    {
        var g = new TestGame(players: 2);
        g.Roll(1, 2);
        g.Ok(new DeclinePropertyCommand(0));
        g.Ok(new PassAuctionCommand(0));
        g.Ok(new PassAuctionCommand(1));
        Assert.Equal(-1, g.Prop(3).Owner);
        Assert.Equal(TurnPhase.PostRoll, g.S.Phase);
    }

    [Fact]
    public void Bids_cannot_exceed_cash()
    {
        var g = new TestGame(players: 2);
        g.Roll(1, 2);
        g.Ok(new DeclinePropertyCommand(0));
        Assert.Equal("Not enough money", g.Fail(new PlaceBidCommand(1, 1600)));
    }

    [Fact]
    public void Rapid_auction_reveals_bids_and_breaks_ties_in_turn_order()
    {
        var g = new TestGame(players: 3, configure: r => r.AuctionMode = AuctionMode.Rapid);
        g.Roll(1, 2);
        g.Ok(new DeclinePropertyCommand(0));
        g.Ok(new SubmitSealedBidCommand(0, 80));
        g.Ok(new SubmitSealedBidCommand(2, 80));
        Assert.Equal("You already submitted a bid", g.Fail(new SubmitSealedBidCommand(2, 90)));
        Assert.NotNull(g.S.Auction);
        g.Ok(new SubmitSealedBidCommand(1, 20));
        Assert.Null(g.S.Auction);
        Assert.Equal(2, g.Prop(3).Owner);
        Assert.Equal(1420, g.P(2).Money);
        Assert.Equal(3, g.Last<SealedBidsRevealed>().Bids.Length);
    }

    [Fact]
    public void Sealed_bids_are_hidden_from_other_players()
    {
        var g = new TestGame(players: 3, configure: r => r.AuctionMode = AuctionMode.Rapid);
        g.Roll(1, 2);
        g.Ok(new DeclinePropertyCommand(0));
        g.Ok(new SubmitSealedBidCommand(1, 77));
        var e = g.Last<SealedBidPlaced>();
        Assert.Equal(77, ((SealedBidPlaced)e.RedactFor(1)).Amount);
        Assert.Equal(-1, ((SealedBidPlaced)e.RedactFor(2)).Amount);
        Assert.Equal(-1, g.S.RedactedFor(0).Auction!.SealedBids[0].Amount);
        Assert.Equal(77, g.S.RedactedFor(1).Auction!.SealedBids[0].Amount);
    }

    [Fact]
    public void Trade_transfers_ownership_and_money()
    {
        var g = new TestGame();
        g.Give(0, 1);
        g.Give(1, 3);
        var give = new TradeSide { Money = 100, Tiles = { 1 } };
        var receive = new TradeSide { Tiles = { 3 } };
        g.Ok(new CreateTradeCommand(0, 1, give, receive));
        int id = g.S.Trades[0].Id;
        Assert.Equal("This offer is not addressed to you", g.Fail(new AcceptTradeCommand(2, id)));
        g.Ok(new AcceptTradeCommand(1, id));
        Assert.Equal(1, g.Prop(1).Owner);
        Assert.Equal(0, g.Prop(3).Owner);
        Assert.Equal(1400, g.P(0).Money);
        Assert.Equal(1600, g.P(1).Money);
        Assert.Empty(g.S.Trades);
        Assert.Equal(1, g.P(0).Stats.TradesCompleted);
    }

    [Fact]
    public void Trade_completing_a_district_is_reported()
    {
        var g = new TestGame();
        g.Give(0, 1);
        g.Give(1, 3);
        g.Ok(new CreateTradeCommand(0, 1, new TradeSide { Money = 200 }, new TradeSide { Tiles = { 3 } }));
        g.Ok(new AcceptTradeCommand(1, g.S.Trades[0].Id));
        Assert.Equal("harbor", g.Last<DistrictCompleted>().District);
    }

    [Fact]
    public void Trade_validation_rejects_bad_offers()
    {
        var g = new TestGame();
        g.Give(0, 1, 3);
        g.Give(1, 6);
        Assert.Equal("The offer is empty", g.Fail(new CreateTradeCommand(0, 1, new TradeSide(), new TradeSide())));
        Assert.Contains("cannot afford", g.Fail(new CreateTradeCommand(0, 1, new TradeSide { Money = 5000 }, new TradeSide { Tiles = { 6 } })));
        Assert.Contains("does not control", g.Fail(new CreateTradeCommand(0, 1, new TradeSide { Tiles = { 6 } }, new TradeSide { Money = 1 })));
        g.Ok(new BuildHouseCommand(0, 1));
        Assert.Contains("buildings", g.Fail(new CreateTradeCommand(0, 1, new TradeSide { Tiles = { 3 } }, new TradeSide { Money = 1 })));
        Assert.Equal("You cannot trade with yourself", g.Fail(new CreateTradeCommand(0, 0, new TradeSide { Money = 1 }, new TradeSide())));
    }

    [Fact]
    public void Trade_can_be_rejected_countered_modified_and_cancelled()
    {
        var g = new TestGame();
        g.Give(0, 1);
        g.Give(1, 3);
        g.Ok(new CreateTradeCommand(0, 1, new TradeSide { Money = 10 }, new TradeSide { Tiles = { 3 } }));
        int id = g.S.Trades[0].Id;
        Assert.Equal("You already have a pending offer", g.Fail(new CreateTradeCommand(0, 2, new TradeSide { Money = 10 }, new TradeSide())));
        g.Ok(new ModifyTradeCommand(0, id, new TradeSide { Money = 50 }, new TradeSide { Tiles = { 3 } }));
        Assert.Equal(50, g.S.Trades[0].Give.Money);

        g.Ok(new CounterTradeCommand(1, id, new TradeSide { Tiles = { 3 } }, new TradeSide { Money = 300 }));
        var counter = g.S.Trades.Single();
        Assert.Equal(1, counter.From);
        Assert.Equal(0, counter.To);
        g.Ok(new RejectTradeCommand(0, counter.Id));
        Assert.Empty(g.S.Trades);

        g.Ok(new CreateTradeCommand(0, 1, new TradeSide { Money = 10 }, new TradeSide { Tiles = { 3 } }));
        g.Ok(new CancelTradeCommand(0, g.S.Trades[0].Id));
        Assert.Empty(g.S.Trades);
    }

    [Fact]
    public void Trade_proposals_are_rate_limited_per_turn()
    {
        var g = new TestGame();
        g.Give(1, 3);
        for (int i = 0; i < 3; i++)
        {
            g.Ok(new CreateTradeCommand(0, 1, new TradeSide { Money = 10 }, new TradeSide { Tiles = { 3 } }));
            g.Ok(new CancelTradeCommand(0, g.S.Trades[0].Id));
        }
        Assert.Equal("Too many proposals this turn", g.Fail(new CreateTradeCommand(0, 1, new TradeSide { Money = 10 }, new TradeSide { Tiles = { 3 } })));
    }

    [Fact]
    public void Accepting_a_trade_can_settle_a_debt()
    {
        var g = new TestGame(players: 3);
        g.Give(1, 37, 39);
        g.Prop(39).Level = 5;
        g.Give(0, 21);
        g.P(0).Money = 1900;
        g.P(0).Position = 36;
        g.Roll(1, 2);
        g.Ok(new CreateTradeCommand(0, 2, new TradeSide { Tiles = { 21 } }, new TradeSide { Money = 300 }));
        g.Ok(new AcceptTradeCommand(2, g.S.Trades[0].Id));
        Assert.Empty(g.S.Debts);
        Assert.Equal(200, g.P(0).Money);
        Assert.Equal(TurnPhase.PostRoll, g.S.Phase);
    }

    [Fact]
    public void Trades_are_closed_during_auctions()
    {
        var g = new TestGame(players: 2);
        g.Give(1, 6);
        g.Roll(1, 2);
        g.Ok(new DeclinePropertyCommand(0));
        Assert.Equal("Trading is closed right now", g.Fail(new CreateTradeCommand(0, 1, new TradeSide { Money = 10 }, new TradeSide { Tiles = { 6 } })));
    }
}
