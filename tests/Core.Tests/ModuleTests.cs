using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.Rules.Modules;
using Game.Core.State;

namespace Core.Tests;

public class ModuleTests
{
    // ------------------------------------------------------------ market

    [Fact]
    public void Effects_modify_rent_price_and_tax()
    {
        var g = new TestGame(configure: r => r.MarketEventsEnabled = true);
        g.Give(1, 3);
        g.S.Effects.Add(new ActiveEffect { Scope = EffectScope.District, ScopeValue = "harbor", RentPercent = 50, RoundsLeft = 2 });
        g.S.Effects.Add(new ActiveEffect { Scope = EffectScope.All, PricePercent = -25, TaxPercent = -50, RoundsLeft = 2 });
        Assert.Equal(6, Calc.Rent(g.S, 3, 7));
        Assert.Equal(45, Calc.PurchasePrice(g.S, 1));
        Assert.Equal(100, Calc.TaxAmount(g.S, 4));
        Assert.Equal(300, Calc.PurchasePrice(g.S, 39));
    }

    [Fact]
    public void Effects_expire_when_their_rounds_run_out()
    {
        var g = new TestGame();
        g.S.Effects.Add(new ActiveEffect { Scope = EffectScope.All, RentPercent = 10, RoundsLeft = 2 });
        g.S.Effects.Add(new ActiveEffect { Source = "economy", Scope = EffectScope.All, RentPercent = 10, RoundsLeft = -1 });
        new RoundStarted(2).Apply(g.S);
        Assert.Equal(2, g.S.Effects.Count);
        new RoundStarted(3).Apply(g.S);
        Assert.Single(g.S.Effects);
        Assert.Equal("economy", g.S.Effects[0].Source);
    }

    [Fact]
    public void Economy_change_replaces_the_previous_economy_effect()
    {
        var g = new TestGame();
        new EconomyChanged(EconomyPhase.Boom, MarketRuleModule.EconomyEffect(EconomyPhase.Boom)).Apply(g.S);
        new EconomyChanged(EconomyPhase.Recession, MarketRuleModule.EconomyEffect(EconomyPhase.Recession)).Apply(g.S);
        Assert.Equal(EconomyPhase.Recession, g.S.Economy);
        Assert.Single(g.S.Effects);
        Assert.Equal(-20, g.S.Effects[0].RentPercent);
        new EconomyChanged(EconomyPhase.Stable, null).Apply(g.S);
        Assert.Empty(g.S.Effects);
    }

    [Fact]
    public void Near_transit_scope_only_hits_streets_beside_transit()
    {
        var g = new TestGame();
        var e = new ActiveEffect { Scope = EffectScope.NearTransit, RentPercent = 15, RoundsLeft = 1 };
        Assert.True(Calc.EffectApplies(g.S, e, 6));
        Assert.True(Calc.EffectApplies(g.S, e, 3));
        Assert.False(Calc.EffectApplies(g.S, e, 1));
        Assert.False(Calc.EffectApplies(g.S, e, 39));
    }

    // ------------------------------------------------------------ objectives

    [Fact]
    public void Objectives_are_assigned_hidden_and_rewarded()
    {
        var g = new TestGame(configure: r => r.SecretObjectivesEnabled = true);
        Assert.All(g.S.Players, p => Assert.Equal(2, p.Objectives.Count));
        var view = g.S.RedactedFor(0);
        Assert.All(view.Players[1].Objectives, o => Assert.Equal(-1, o.Id));
        Assert.All(view.Players[0].Objectives, o => Assert.True(o.Id >= 0));
        var assigned = g.Log.OfType<ObjectivesAssigned>().First(e => e.Player == 1);
        Assert.All(((ObjectivesAssigned)assigned.RedactFor(0)).Ids, id => Assert.Equal(-1, id));

        g.P(0).Objectives[0].Id = 6; // hold 2,000 in cash
        g.P(0).Objectives[1].Id = 8;
        g.P(0).Money = 2100;
        g.Give(1, 3);
        g.Roll(1, 2);
        var done = g.Last<ObjectiveCompleted>();
        Assert.Equal(0, done.Slot);
        Assert.True(g.P(0).Objectives[0].Completed);
        Assert.Equal(2100 - 4 + 200, g.P(0).Money);
    }

    // ------------------------------------------------------------ shares

    [Fact]
    public void Rent_is_split_between_shareholders()
    {
        var g = new TestGame(configure: r => r.PropertySharesEnabled = true);
        g.Give(1, 39);
        g.Prop(39).Level = 2;
        g.Prop(39).Shares = new List<Share>
        {
            new() { Player = 1, Percent = 60 }, new() { Player = 2, Percent = 25 }, new() { Player = 3, Percent = 15 },
        };
        g.P(0).Position = 36;
        g.Roll(1, 2);
        Assert.Equal(1500 - 600, g.P(0).Money);
        Assert.Equal(1500 + 360, g.P(1).Money);
        Assert.Equal(1500 + 150, g.P(2).Money);
        Assert.Equal(1500 + 90, g.P(3).Money);
    }

    [Fact]
    public void A_shareholder_does_not_pay_their_own_stake()
    {
        var g = new TestGame(configure: r => r.PropertySharesEnabled = true);
        g.Give(1, 39);
        g.Prop(39).Shares = new List<Share> { new() { Player = 1, Percent = 60 }, new() { Player = 0, Percent = 40 } };
        g.P(0).Position = 36;
        g.Roll(1, 2);
        Assert.Equal(1500 - 30, g.P(0).Money);
    }

    [Fact]
    public void Share_trades_move_stakes_and_control()
    {
        var g = new TestGame(configure: r => r.PropertySharesEnabled = true);
        g.Give(0, 39);
        g.Ok(new CreateTradeCommand(0, 1,
            new TradeSide { Shares = { new ShareTransfer { Tile = 39, Percent = 40 } } },
            new TradeSide { Money = 200 }));
        g.Ok(new AcceptTradeCommand(1, g.S.Trades[0].Id));
        Assert.Equal(0, g.Prop(39).Owner);
        Assert.Equal(40, g.Prop(39).ShareOf(1));
        Assert.Equal("Shared properties cannot be mortgaged", g.Fail(new MortgageCommand(0, 39)));

        g.Ok(new CreateTradeCommand(0, 1,
            new TradeSide { Shares = { new ShareTransfer { Tile = 39, Percent = 30 } } },
            new TradeSide { Money = 100 }));
        g.Ok(new AcceptTradeCommand(1, g.S.Trades[0].Id));
        Assert.Equal(1, g.Prop(39).Owner);
        Assert.Equal(100, g.Prop(39).Shares.Sum(s => s.Percent));
    }

    [Fact]
    public void Shares_require_the_module()
    {
        var g = new TestGame();
        g.Give(0, 39);
        Assert.Equal("Property shares are disabled", g.Fail(new CreateTradeCommand(0, 1,
            new TradeSide { Shares = { new ShareTransfer { Tile = 39, Percent = 40 } } }, new TradeSide { Money = 1 })));
    }

    // ------------------------------------------------------------ contracts

    private static TestGame ContractGame(int players = 4) => new(players, r => r.ContractsEnabled = true);

    private static void Deal(TestGame g, int from, int to, TradeSide give, TradeSide receive)
    {
        g.Ok(new CreateTradeCommand(from, to, give, receive));
        g.Ok(new AcceptTradeCommand(to, g.S.Trades[0].Id));
    }

    [Fact]
    public void Rent_immunity_is_consumed_per_visit()
    {
        var g = ContractGame();
        g.Give(1, 3);
        Deal(g, 0, 1, new TradeSide { Money = 50 },
            new TradeSide { Terms = { new ContractTerm { Kind = ContractKind.RentImmunity, Tile = 3, Count = 1 } } });
        Assert.Single(g.S.Contracts);
        g.Roll(1, 2);
        Assert.Equal(1450, g.P(0).Money);
        Assert.Empty(g.S.Contracts);
        Assert.Equal(g.Last<RentWaived>().Tile, 3);
    }

    [Fact]
    public void Revenue_share_redirects_part_of_the_rent()
    {
        var g = ContractGame();
        g.Give(1, 39);
        g.Prop(39).Level = 1;
        Deal(g, 0, 1, new TradeSide(),
            new TradeSide { Terms = { new ContractTerm { Kind = ContractKind.RevenueShare, Tile = 39, Percent = 25, Count = 5 } } });
        g.S.Trades.Clear();
        Deal(g, 0, 1, new TradeSide { Money = 1 }, new TradeSide());
        g.EndTurnAfter(0, 37);
        g.P(1).Position = 0;
        g.Give(1, 6);
        g.RollTo(1, 6);
        g.EndTurn();
        g.P(2).Position = 36;
        g.Roll(1, 2);
        Assert.Equal(1500 - 200, g.P(2).Money);
        Assert.Equal(1500 + 150 + 1, g.P(1).Money);
        Assert.Equal(1500 + 50 - 1, g.P(0).Money);
    }

    [Fact]
    public void Instalments_are_charged_each_round()
    {
        var g = ContractGame(2);
        g.Give(1, 3, 6);
        Deal(g, 0, 1,
            new TradeSide { Terms = { new ContractTerm { Kind = ContractKind.Installment, Amount = 100, Count = 2 } } },
            new TradeSide { Tiles = { 6 } });
        g.Prop(3).Mortgaged = true;
        g.Roll(1, 2);
        g.EndTurn();
        g.P(1).Position = 0;
        g.Roll(1, 2);
        g.EndTurn();
        Assert.Equal(2, g.S.Round);
        Assert.Equal(1400, g.P(0).Money);
        Assert.Equal(1600, g.P(1).Money);
        Assert.Equal(1, g.S.Contracts[0].Remaining);
        Assert.Equal(TurnPhase.PreRoll, g.S.Phase);
    }

    [Fact]
    public void Unpayable_instalment_becomes_a_debt_before_the_roll()
    {
        var g = ContractGame(2);
        g.Give(1, 3, 6);
        Deal(g, 0, 1,
            new TradeSide { Terms = { new ContractTerm { Kind = ContractKind.Installment, Amount = 100, Count = 2 } } },
            new TradeSide { Tiles = { 6 } });
        g.Prop(3).Mortgaged = true;
        g.Roll(1, 2);
        g.EndTurn();
        g.P(1).Position = 0;
        g.Roll(1, 2);
        g.P(0).Money = 60;
        g.EndTurn();
        Assert.Equal(TurnPhase.DebtResolution, g.S.Phase);
        Assert.Equal(0, g.S.Debts[0].Debtor);
        g.Ok(new MortgageCommand(0, 6));
        Assert.Equal(TurnPhase.PreRoll, g.S.Phase);
        Assert.Equal(0, g.S.CurrentPlayer);
    }

    [Fact]
    public void Buy_option_can_be_exercised()
    {
        var g = ContractGame();
        g.Give(1, 39);
        Deal(g, 0, 1, new TradeSide { Money = 50 },
            new TradeSide { Terms = { new ContractTerm { Kind = ContractKind.BuyOption, Tile = 39, Amount = 300, Count = 5 } } });
        int id = g.S.Contracts[0].Id;
        Assert.Equal("No such option", g.Fail(new ExerciseOptionCommand(0, 999)));
        g.Ok(new ExerciseOptionCommand(0, id));
        Assert.Equal(0, g.Prop(39).Owner);
        Assert.Equal(1500 - 50 - 300, g.P(0).Money);
        Assert.Empty(g.S.Contracts);
    }

    [Fact]
    public void Contracts_require_the_module_and_valid_terms()
    {
        var g = new TestGame();
        g.Give(1, 39);
        var term = new TradeSide { Terms = { new ContractTerm { Kind = ContractKind.RentImmunity, Tile = 39, Count = 1 } } };
        Assert.Equal("Contracts are disabled", g.Fail(new CreateTradeCommand(0, 1, new TradeSide { Money = 1 }, term)));

        var c = ContractGame();
        c.Give(1, 39);
        var over = new TradeSide { Terms = { new ContractTerm { Kind = ContractKind.RevenueShare, Tile = 39, Percent = 120, Count = 3 } } };
        Assert.Contains("100%", c.Fail(new CreateTradeCommand(0, 1, new TradeSide { Money = 1 }, over)));
        var notOwned = new TradeSide { Terms = { new ContractTerm { Kind = ContractKind.BuyOption, Tile = 39, Amount = 10, Count = 3 } } };
        Assert.Contains("grantor keeps", c.Fail(new CreateTradeCommand(0, 1, notOwned, new TradeSide { Money = 1 })));
    }

    [Fact]
    public void Contracts_on_a_tile_are_voided_when_it_changes_hands()
    {
        var g = ContractGame();
        g.Give(1, 39);
        Deal(g, 0, 1, new TradeSide { Money = 50 },
            new TradeSide { Terms = { new ContractTerm { Kind = ContractKind.RentImmunity, Tile = 39, Count = 3 } } });
        Deal(g, 2, 1, new TradeSide { Money = 500 }, new TradeSide { Tiles = { 39 } });
        Assert.Empty(g.S.Contracts);
    }

    // ------------------------------------------------------------ abilities

    private static TestGame AbilityGame(Ability a, int players = 3) => new(players, r => r.AbilitiesEnabled = true,
        setup: c => c.Players[0].Ability = a);

    [Fact]
    public void Abilities_are_assigned_to_everyone()
    {
        var g = AbilityGame(Ability.Builder);
        Assert.Equal(Ability.Builder, g.P(0).Ability);
        Assert.All(g.S.Players, p => Assert.NotEqual(Ability.None, p.Ability));
    }

    [Fact]
    public void Builder_gets_one_discount_per_round()
    {
        var g = AbilityGame(Ability.Builder);
        g.Give(0, 37, 39);
        g.Ok(new BuildHouseCommand(0, 37));
        Assert.Equal(1500 - 180, g.P(0).Money);
        g.Ok(new BuildHouseCommand(0, 39));
        Assert.Equal(1500 - 180 - 200, g.P(0).Money);
    }

    [Fact]
    public void Banker_pays_half_the_mortgage_interest()
    {
        var g = AbilityGame(Ability.Banker);
        g.Give(0, 39);
        Assert.Equal(210, Calc.UnmortgageCost(g.S, 39, 0));
        g.Give(1, 37);
        Assert.Equal(Calc.MortgageValue(g.S, 37) * (g.P(1).Ability == Ability.Banker ? 105 : 110) / 100, Calc.UnmortgageCost(g.S, 37, 1));
    }

    [Fact]
    public void Investor_financing_discounts_one_auction()
    {
        var g = AbilityGame(Ability.Investor);
        g.Ok(new UseAbilityCommand(0));
        Assert.Equal("Ability already used", g.Fail(new UseAbilityCommand(0)));
        g.Roll(1, 2);
        g.Ok(new DeclinePropertyCommand(0));
        g.Ok(new PlaceBidCommand(0, 100));
        g.Ok(new PassAuctionCommand(1));
        g.Ok(new PassAuctionCommand(2));
        Assert.Equal(1425, g.P(0).Money);
        Assert.False(g.P(0).AbilityArmed);
        Assert.Equal(75, g.Last<AuctionCompleted>().Paid);
    }

    [Fact]
    public void Negotiator_waives_one_rent()
    {
        var g = AbilityGame(Ability.Negotiator);
        g.Give(1, 3);
        g.Ok(new UseAbilityCommand(0));
        g.Roll(1, 2);
        Assert.Equal(1500, g.P(0).Money);
        Assert.True(g.Last<RentWaived>().ByAbility);
        Assert.False(g.P(0).AbilityArmed);
    }

    [Fact]
    public void Passive_abilities_cannot_be_activated()
    {
        var g = AbilityGame(Ability.Banker);
        Assert.Equal("Your ability is passive", g.Fail(new UseAbilityCommand(0)));
    }

    // ------------------------------------------------------------ public projects

    [Fact]
    public void Funded_project_boosts_rent_and_rewards_the_top_contributor()
    {
        var g = new TestGame(players: 2, configure: r => r.PublicProjectsEnabled = true);
        new ProjectProposed(new ProjectState { Name = "New Airport", Cost = 400, Side = 0, RentPercent = 20, EffectRounds = 6, RoundsLeft = 4 }).Apply(g.S);
        Assert.Equal("Invalid amount", g.Fail(new ContributeToProjectCommand(0, 500)));
        g.Ok(new ContributeToProjectCommand(0, 300));
        Assert.NotNull(g.S.Project);
        g.Give(1, 3);
        g.Prop(3).Mortgaged = true;
        g.Roll(1, 2);
        g.EndTurn();
        g.Ok(new ContributeToProjectCommand(1, 100));
        Assert.Null(g.S.Project);
        Assert.Equal(1500 - 300 + 40, g.P(0).Money);
        var effect = g.S.Effects.Single();
        Assert.Equal(EffectScope.Side, effect.Scope);
        g.Prop(3).Mortgaged = false;
        Assert.Equal(4, Calc.Rent(g.S, 3, 7));
        g.Prop(3).Level = 3;
        Assert.Equal(216, Calc.Rent(g.S, 3, 7));
    }

    [Fact]
    public void Failed_project_refunds_contributions()
    {
        var g = new TestGame(players: 2, configure: r => r.PublicProjectsEnabled = true);
        new ProjectProposed(new ProjectState { Name = "Metro Line", Cost = 800, Side = 1, RentPercent = 20, EffectRounds = 6, RoundsLeft = 1 }).Apply(g.S);
        g.Ok(new ContributeToProjectCommand(0, 200));
        g.Give(1, 3);
        g.Prop(3).Mortgaged = true;
        g.Roll(1, 2);
        g.EndTurn();
        g.P(1).Position = 0;
        g.Roll(1, 2);
        g.EndTurn();
        Assert.Null(g.S.Project);
        Assert.Equal(1500, g.P(0).Money);
        Assert.Contains(g.Log, e => e is ProjectFailed);
    }

    // ------------------------------------------------------------ teams

    [Fact]
    public void Teammates_do_not_charge_each_other_rent_and_can_transfer_cash()
    {
        var g = new TestGame(players: 4, configure: r => r.TeamsEnabled = true);
        Assert.Equal(g.P(0).Team, g.P(2).Team);
        g.Give(2, 3);
        g.Roll(1, 2);
        Assert.Equal(1500, g.P(0).Money);
        g.Ok(new TransferMoneyCommand(0, 2, 400));
        Assert.Equal(1900, g.P(2).Money);
        Assert.Contains("teammate", g.Fail(new TransferMoneyCommand(0, 1, 10)));
    }

    [Fact]
    public void A_team_wins_when_the_other_team_is_eliminated()
    {
        var g = new TestGame(players: 4, configure: r => r.TeamsEnabled = true);
        g.Ok(new DeclareBankruptcyCommand(0));
        Assert.False(g.S.IsOver);
        g.Give(3, 3);
        g.Prop(3).Mortgaged = true;
        g.Roll(1, 2);
        g.EndTurn();
        g.Ok(new DeclareBankruptcyCommand(2));
        Assert.True(g.S.IsOver);
        Assert.Equal(new[] { 1, 3 }, g.S.Winners);
    }

    [Fact]
    public void Transfers_require_team_mode()
    {
        var g = new TestGame();
        g.Fail(new TransferMoneyCommand(0, 1, 10));
    }

    // ------------------------------------------------------------ advanced development

    [Fact]
    public void Development_types_change_cost_and_rent()
    {
        var g = new TestGame(configure: r => r.AdvancedDevelopment = true);
        g.Give(0, 37, 39);
        g.Ok(new BuildHouseCommand(0, 37, DevelopmentType.Luxury));
        Assert.Equal(1500 - 300, g.P(0).Money);
        Assert.Equal(175 * 160 / 100, Calc.Rent(g.S, 37, 7));
        g.Ok(new BuildHouseCommand(0, 39, DevelopmentType.Industrial));
        Assert.Equal(1500 - 300 - 140, g.P(0).Money);
        Assert.Equal(200 * 80 / 100, Calc.Rent(g.S, 39, 7));
        g.Ok(new BuildHouseCommand(0, 37, DevelopmentType.Industrial));
        Assert.Equal(DevelopmentType.Luxury, g.Prop(37).DevType);
    }

    [Fact]
    public void Luxury_developments_are_twice_as_volatile()
    {
        var g = new TestGame(configure: r => r.AdvancedDevelopment = true);
        g.Give(0, 37, 39);
        g.Prop(37).Level = 1;
        g.Prop(37).DevType = DevelopmentType.Luxury;
        g.S.Effects.Add(new ActiveEffect { Scope = EffectScope.All, RentPercent = -20, RoundsLeft = 3 });
        Assert.Equal(175 * 160 / 100 * 60 / 100, Calc.Rent(g.S, 37, 7));
    }

    // ------------------------------------------------------------ presets

    [Fact]
    public void Quick_preset_deals_starting_properties_and_ends_on_first_bankruptcy()
    {
        var g = new TestGame(players: 3, configure: r =>
        {
            r.StartingProperties = 2;
            r.EndOnFirstBankruptcy = true;
        });
        Assert.All(g.S.Players, p => Assert.Equal(2, g.S.OwnedBy(p.Id).Count()));
        g.Ok(new DeclareBankruptcyCommand(0));
        Assert.True(g.S.IsOver);
        Assert.Equal(GameEndReason.FirstBankruptcy, g.S.EndReason);
        Assert.DoesNotContain(0, g.S.Winners);
    }

    [Theory]
    [MemberData(nameof(PresetIds))]
    public void Every_preset_resolves(string id)
    {
        var rules = RulePresets.Get(id);
        Assert.Equal(id, rules.PresetId);
        Assert.NotEmpty(RuleModules.For(rules));
    }

    public static IEnumerable<object[]> PresetIds() => RulePresets.Ids.Select(i => new object[] { i });
}

internal static class TestGameExtensions
{
    /// <summary>Teleports the current player so that the next roll of 1+2 lands on <paramref name="tile"/>.</summary>
    public static void RollTo(this TestGame g, int player, int tile)
    {
        Assert.Equal(player, g.S.CurrentPlayer);
        int n = g.S.Board.Count;
        g.P(player).Position = ((tile - 3) % n + n) % n;
        g.Roll(1, 2);
    }

    public static void EndTurnAfter(this TestGame g, int player, int tile)
    {
        g.RollTo(player, tile);
        if (g.S.Phase == TurnPhase.BuyDecision) g.Ok(new DeclinePropertyCommand(player));
        if (g.S.Phase == TurnPhase.Auction)
            foreach (int id in g.S.Auction!.Participants.ToList()) g.Ok(new PassAuctionCommand(id));
        g.EndTurn();
    }
}
