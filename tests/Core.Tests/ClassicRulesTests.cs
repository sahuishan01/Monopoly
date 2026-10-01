using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.State;

namespace Core.Tests;

public class ClassicRulesTests
{
    [Fact]
    public void Roll_moves_player_and_offers_unowned_property()
    {
        var g = new TestGame();
        g.Roll(1, 2);
        Assert.Equal(3, g.P(0).Position);
        Assert.Equal(TurnPhase.BuyDecision, g.S.Phase);
        Assert.Equal(60, g.Last<PurchaseOffered>().Price);
    }

    [Fact]
    public void Buying_transfers_ownership_and_money()
    {
        var g = new TestGame();
        g.Roll(1, 2);
        g.Ok(new BuyPropertyCommand(0));
        Assert.Equal(0, g.Prop(3).Owner);
        Assert.Equal(1440, g.P(0).Money);
        Assert.Equal(TurnPhase.PostRoll, g.S.Phase);
    }

    [Fact]
    public void Player_cannot_buy_property_without_enough_money()
    {
        var g = new TestGame();
        g.P(0).Money = 50;
        g.Roll(1, 2);
        Assert.Equal("Not enough money", g.Fail(new BuyPropertyCommand(0)));
        Assert.Equal(-1, g.Prop(3).Owner);
    }

    [Fact]
    public void Only_the_current_player_may_roll()
    {
        var g = new TestGame();
        Assert.Equal("Not your turn", g.Fail(new RollDiceCommand(1)));
    }

    [Fact]
    public void Stale_commands_are_rejected()
    {
        var g = new TestGame();
        Assert.Equal("Stale command", g.Fail(new RollDiceCommand(0) { ExpectedVersion = g.S.Version - 1 }));
        g.Dice.EnqueueDice(1, 2);
        g.Ok(new RollDiceCommand(0) { ExpectedVersion = g.S.Version });
    }

    [Fact]
    public void Cannot_end_turn_before_rolling()
    {
        var g = new TestGame();
        g.Fail(new EndTurnCommand(0));
    }

    [Fact]
    public void Doubles_grant_another_roll_and_three_doubles_send_to_jail()
    {
        var g = new TestGame();
        g.Give(1, 8);
        g.Roll(2, 2);
        Assert.Equal(TurnPhase.PreRoll, g.S.Phase);
        Assert.Equal(0, g.S.CurrentPlayer);
        g.Roll(2, 2);
        g.Roll(2, 2);
        Assert.True(g.P(0).InJail);
        Assert.Equal(g.S.Board.JailIndex, g.P(0).Position);
        Assert.Equal(TurnPhase.PostRoll, g.S.Phase);
    }

    [Fact]
    public void Passing_start_pays_salary()
    {
        var g = new TestGame();
        g.P(0).Position = 38;
        g.Give(1, 3);
        g.Prop(3).Mortgaged = true;
        g.Roll(2, 3);
        Assert.Equal(3, g.P(0).Position);
        Assert.Equal(1700, g.P(0).Money);
        Assert.True(g.Last<PlayerMoved>().PassedStart);
    }

    [Fact]
    public void Rent_is_paid_to_the_owner()
    {
        var g = new TestGame();
        g.Give(1, 3);
        g.Roll(1, 2);
        Assert.Equal(1496, g.P(0).Money);
        Assert.Equal(1504, g.P(1).Money);
        Assert.Equal(4, g.P(1).Stats.RentEarned);
    }

    [Fact]
    public void Owning_the_district_doubles_base_rent()
    {
        var g = new TestGame();
        g.Give(1, 1, 3);
        Assert.Equal(8, Calc.Rent(g.S, 3, 7));
    }

    [Fact]
    public void Mortgaged_property_cannot_charge_rent()
    {
        var g = new TestGame();
        g.Give(1, 3);
        g.Prop(3).Mortgaged = true;
        g.Roll(1, 2);
        Assert.Equal(1500, g.P(0).Money);
        Assert.Empty(g.Log.OfType<RentPaid>());
    }

    [Fact]
    public void Transit_rent_scales_with_lines_owned()
    {
        var g = new TestGame();
        g.Give(1, 5);
        Assert.Equal(25, Calc.Rent(g.S, 5, 7));
        g.Give(1, 15, 25);
        Assert.Equal(100, Calc.Rent(g.S, 5, 7));
        g.Give(1, 35);
        Assert.Equal(200, Calc.Rent(g.S, 5, 7));
    }

    [Fact]
    public void Utility_rent_uses_the_dice()
    {
        var g = new TestGame();
        g.Give(1, 12);
        Assert.Equal(28, Calc.Rent(g.S, 12, 7));
        g.Give(1, 28);
        Assert.Equal(70, Calc.Rent(g.S, 12, 7));
    }

    [Fact]
    public void Tax_tile_charges_the_bank()
    {
        var g = new TestGame();
        g.Roll(1, 3);
        Assert.Equal(1300, g.P(0).Money);
        Assert.Equal(200, g.P(0).Stats.TaxesPaid);
    }

    [Fact]
    public void Free_parking_jackpot_collects_penalties()
    {
        var g = new TestGame(configure: r => r.FreeParkingJackpot = true);
        g.Roll(1, 3);
        Assert.Equal(200, g.S.Bank.Jackpot);
        g.EndTurn();
        g.P(1).Position = 13;
        g.Roll(3, 4);
        Assert.Equal(1700, g.P(1).Money);
        Assert.Equal(0, g.S.Bank.Jackpot);
    }

    [Fact]
    public void Player_cannot_build_without_owning_the_district()
    {
        var g = new TestGame();
        g.Give(0, 1);
        Assert.Equal("You must control the whole district", g.Fail(new BuildHouseCommand(0, 1)));
    }

    [Fact]
    public void Building_is_even_and_costs_money()
    {
        var g = new TestGame();
        g.Give(0, 1, 3);
        g.Ok(new BuildHouseCommand(0, 1));
        Assert.Equal(1450, g.P(0).Money);
        Assert.Equal(31, g.S.Bank.HousesLeft);
        Assert.Equal("Build evenly across the district", g.Fail(new BuildHouseCommand(0, 1)));
        g.Ok(new BuildHouseCommand(0, 3));
        g.Ok(new BuildHouseCommand(0, 1));
        Assert.Equal(2, g.Prop(1).Level);
        Assert.Equal(30, Calc.Rent(g.S, 1, 7));
    }

    [Fact]
    public void Landmark_returns_four_houses_to_the_bank()
    {
        var g = new TestGame();
        g.Give(0, 1, 3);
        g.P(0).Money = 5000;
        for (int level = 0; level < 5; level++)
        {
            g.Ok(new BuildHouseCommand(0, 1));
            g.Ok(new BuildHouseCommand(0, 3));
        }
        Assert.Equal(5, g.Prop(1).Level);
        Assert.Equal(32, g.S.Bank.HousesLeft);
        Assert.Equal(10, g.S.Bank.HotelsLeft);
        Assert.Equal("Already fully developed", g.Fail(new BuildHouseCommand(0, 1)));
        g.Ok(new SellHouseCommand(0, 1));
        Assert.Equal(4, g.Prop(1).Level);
        Assert.Equal(28, g.S.Bank.HousesLeft);
        Assert.Equal(11, g.S.Bank.HotelsLeft);
    }

    [Fact]
    public void House_supply_is_limited()
    {
        var g = new TestGame(configure: r => r.HouseSupply = 1);
        g.Give(0, 1, 3);
        g.Ok(new BuildHouseCommand(0, 1));
        Assert.Equal("The bank has no buildings left", g.Fail(new BuildHouseCommand(0, 3)));
    }

    [Fact]
    public void Mortgage_and_unmortgage_with_interest()
    {
        var g = new TestGame();
        g.Give(0, 39);
        g.Ok(new MortgageCommand(0, 39));
        Assert.Equal(1700, g.P(0).Money);
        Assert.Equal("Already mortgaged", g.Fail(new MortgageCommand(0, 39)));
        g.Ok(new UnmortgageCommand(0, 39));
        Assert.Equal(1480, g.P(0).Money);
        Assert.False(g.Prop(39).Mortgaged);
    }

    [Fact]
    public void Cannot_mortgage_a_district_with_buildings()
    {
        var g = new TestGame();
        g.Give(0, 1, 3);
        g.Ok(new BuildHouseCommand(0, 1));
        Assert.Equal("Sell the district's buildings first", g.Fail(new MortgageCommand(0, 3)));
    }

    [Fact]
    public void Jail_fine_releases_and_allows_a_roll()
    {
        var g = new TestGame();
        g.P(0).InJail = true;
        g.P(0).Position = 10;
        g.Ok(new PayJailFineCommand(0));
        Assert.False(g.P(0).InJail);
        Assert.Equal(1450, g.P(0).Money);
        Assert.Equal(TurnPhase.PreRoll, g.S.Phase);
    }

    [Fact]
    public void Jail_doubles_release_without_extra_turn()
    {
        var g = new TestGame();
        g.P(0).InJail = true;
        g.P(0).Position = 10;
        g.Give(1, 14);
        g.Roll(2, 2);
        Assert.False(g.P(0).InJail);
        Assert.Equal(14, g.P(0).Position);
        Assert.Equal(TurnPhase.PostRoll, g.S.Phase);
    }

    [Fact]
    public void Third_failed_jail_roll_forces_the_fine()
    {
        var g = new TestGame(players: 2);
        g.P(0).InJail = true;
        g.P(0).Position = 10;
        g.Give(1, 13);
        g.Prop(13).Mortgaged = true;
        g.Roll(1, 2);
        Assert.True(g.P(0).InJail);
        g.EndTurn();
        g.P(1).Position = 10;
        g.Roll(1, 2);
        g.EndTurn();
        g.Roll(1, 2);
        g.EndTurn();
        g.P(1).Position = 10;
        g.Roll(1, 2);
        g.EndTurn();
        g.Roll(1, 2);
        Assert.False(g.P(0).InJail);
        Assert.Equal(1450, g.P(0).Money);
        Assert.Equal(13, g.P(0).Position);
    }

    [Fact]
    public void Jail_card_is_consumed()
    {
        var g = new TestGame();
        g.P(0).InJail = true;
        g.P(0).JailCards = 1;
        g.Ok(new UseJailCardCommand(0));
        Assert.False(g.P(0).InJail);
        Assert.Equal(0, g.P(0).JailCards);
    }

    [Fact]
    public void Go_to_jail_tile_jails_the_player()
    {
        var g = new TestGame();
        g.P(0).Position = 25;
        g.Roll(2, 3);
        Assert.True(g.P(0).InJail);
        Assert.Equal(10, g.P(0).Position);
    }

    [Fact]
    public void Cards_are_drawn_and_resolved()
    {
        var g = new TestGame();
        int cardIndex = g.S.Board.CivicCards.FindIndex(c => c.Id == "c_bank");
        g.Dice.EnqueueDice(1, 1);
        g.Dice.Enqueue(cardIndex);
        g.Ok(new RollDiceCommand(0));
        Assert.Equal(1700, g.P(0).Money);
        Assert.DoesNotContain(cardIndex, g.S.CivicDeck);
    }

    [Fact]
    public void Advance_card_moves_and_pays_salary()
    {
        var g = new TestGame();
        g.P(0).Position = 33;
        int cardIndex = g.S.Board.FortuneCards.FindIndex(c => c.Id == "f_start");
        g.Dice.EnqueueDice(1, 2);
        g.Dice.Enqueue(cardIndex);
        g.Ok(new RollDiceCommand(0));
        Assert.Equal(0, g.P(0).Position);
        Assert.Equal(1700, g.P(0).Money);
    }

    [Fact]
    public void Nearest_transit_card_charges_double_fare()
    {
        var g = new TestGame();
        g.Give(1, 15);
        g.P(0).Position = 4;
        int cardIndex = g.S.Board.FortuneCards.FindIndex(c => c.Id == "f_near_transit");
        g.Dice.EnqueueDice(1, 2);
        g.Dice.Enqueue(cardIndex);
        g.Ok(new RollDiceCommand(0));
        Assert.Equal(15, g.P(0).Position);
        Assert.Equal(1450, g.P(0).Money);
    }

    [Fact]
    public void Collect_from_each_card_charges_every_opponent()
    {
        var g = new TestGame(players: 3);
        int cardIndex = g.S.Board.CivicCards.FindIndex(c => c.Id == "c_festival");
        g.Dice.EnqueueDice(1, 1);
        g.Dice.Enqueue(cardIndex);
        g.Ok(new RollDiceCommand(0));
        Assert.Equal(1550, g.P(0).Money);
        Assert.Equal(1475, g.P(1).Money);
        Assert.Equal(1475, g.P(2).Money);
    }

    [Fact]
    public void Turn_order_skips_bankrupt_players_and_counts_rounds()
    {
        var g = new TestGame(players: 3);
        g.Ok(new DeclareBankruptcyCommand(0));
        Assert.Equal(1, g.S.CurrentPlayer);
        g.Give(0, 3);
        g.Prop(3).Owner = 2;
        g.Prop(3).Mortgaged = true;
        g.Roll(1, 2);
        g.EndTurn();
        Assert.Equal(2, g.S.CurrentPlayer);
        Assert.Equal(1, g.S.Round);
        g.P(2).Position = 1;
        g.Roll(1, 1);
        g.Roll(1, 2);
        Assert.Equal(TurnPhase.BuyDecision, g.S.Phase);
        g.Ok(new DeclinePropertyCommand(2));
    }

    [Fact]
    public void Bankrupt_player_cannot_act()
    {
        var g = new TestGame(players: 3);
        g.Ok(new DeclareBankruptcyCommand(0));
        Assert.Equal("Bankrupt players cannot act", g.Fail(new RollDiceCommand(0)));
    }

    [Fact]
    public void Last_player_standing_wins()
    {
        var g = new TestGame(players: 2);
        g.Ok(new DeclareBankruptcyCommand(0));
        Assert.True(g.S.IsOver);
        Assert.Equal(new[] { 1 }, g.S.Winners);
        Assert.Equal(GameEndReason.LastStanding, g.S.EndReason);
        Assert.Equal("The match is over", g.Fail(new RollDiceCommand(1)));
    }

    [Fact]
    public void Round_limit_ends_the_match_by_net_worth()
    {
        var g = new TestGame(players: 2, configure: r => r.MaximumRounds = 1);
        g.Give(1, 37, 39, 3);
        g.Prop(3).Mortgaged = true;
        g.Roll(1, 2);
        g.EndTurn();
        g.P(1).Position = 34;
        g.Roll(1, 2);
        g.EndTurn();
        Assert.True(g.S.IsOver);
        Assert.Equal(GameEndReason.RoundLimit, g.S.EndReason);
        Assert.Equal(new[] { 1 }, g.S.Winners);
    }
}
