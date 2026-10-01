using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.State;

namespace Game.Core.Engine;

public sealed partial class GameEngine
{
    // ------------------------------------------------------------------ purchase

    private string? Buy(BuyPropertyCommand c)
    {
        if (!IsCurrent(c.PlayerId)) return "Not your turn";
        if (State.Phase != TurnPhase.BuyDecision) return "Nothing to buy";
        var p = Current;
        int tile = p.Position;
        int price = Calc.PurchasePrice(State, tile);
        if (p.Money < price) return "Not enough money";
        var before = DistrictOwners();
        Emit(new PropertyPurchased(p.Id, tile, price));
        EmitDistrictChanges(before);
        SettlePhase();
        return null;
    }

    private string? Decline(DeclinePropertyCommand c)
    {
        if (!IsCurrent(c.PlayerId)) return "Not your turn";
        if (State.Phase != TurnPhase.BuyDecision) return "Nothing to decline";
        if (Rules.AuctionsEnabled)
        {
            int[] participants = State.Players.Where(p => !p.Bankrupt).Select(p => p.Id).ToArray();
            Emit(new AuctionStarted(Current.Position, Rules.AuctionMode, c.PlayerId, participants));
        }
        SettlePhase();
        return null;
    }

    // ------------------------------------------------------------------ auctions

    private string? PlaceBid(PlaceBidCommand c)
    {
        var a = State.Auction;
        if (a == null || State.Phase != TurnPhase.Auction) return "No auction in progress";
        if (a.Mode != AuctionMode.Classic) return "This auction uses sealed bids";
        if (!a.Participants.Contains(c.PlayerId) || a.Passed.Contains(c.PlayerId)) return "You are out of this auction";
        if (a.HighBidder == c.PlayerId) return "You already hold the high bid";
        int min = a.HighBidder < 0 ? Rules.AuctionMinIncrement : a.HighBid + Rules.AuctionMinIncrement;
        if (c.Amount < min) return $"Minimum bid is {min}";
        if (c.Amount > State.Players[c.PlayerId].Money) return "Not enough money";
        Emit(new BidPlaced(c.PlayerId, c.Amount));
        CheckClassicAuctionEnd();
        return null;
    }

    private string? PassAuction(PassAuctionCommand c)
    {
        var a = State.Auction;
        if (a == null || State.Phase != TurnPhase.Auction) return "No auction in progress";
        if (a.Mode != AuctionMode.Classic) return "This auction uses sealed bids";
        if (!a.Participants.Contains(c.PlayerId) || a.Passed.Contains(c.PlayerId)) return "You are out of this auction";
        if (a.HighBidder == c.PlayerId) return "You hold the high bid";
        Emit(new AuctionPassed(c.PlayerId));
        CheckClassicAuctionEnd();
        return null;
    }

    private void CheckClassicAuctionEnd()
    {
        var a = State.Auction!;
        int remaining = 0;
        bool onlyLeader = true;
        foreach (int id in a.Participants)
        {
            if (a.Passed.Contains(id)) continue;
            remaining++;
            if (id != a.HighBidder) onlyLeader = false;
        }
        if (a.HighBidder >= 0 && onlyLeader) CompleteAuction(a.HighBidder, a.HighBid);
        else if (a.HighBidder < 0 && remaining == 0) CompleteAuction(-1, 0);
    }

    private string? SubmitSealedBid(SubmitSealedBidCommand c)
    {
        var a = State.Auction;
        if (a == null || State.Phase != TurnPhase.Auction) return "No auction in progress";
        if (a.Mode != AuctionMode.Rapid) return "This auction uses open bids";
        if (!a.Participants.Contains(c.PlayerId)) return "You are not in this auction";
        if (a.HasSubmitted(c.PlayerId)) return "You already submitted a bid";
        if (c.Amount < 0) return "Invalid bid";
        if (c.Amount > State.Players[c.PlayerId].Money) return "Not enough money";
        Emit(new SealedBidPlaced(c.PlayerId, c.Amount));
        if (a.SealedBids.Count < a.Participants.Count) return null;

        Emit(new SealedBidsRevealed(a.SealedBids.Select(b => b.Clone()).ToArray()));
        int n = State.Players.Count;
        int winner = -1, best = 0, bestOrder = int.MaxValue;
        foreach (var b in a.SealedBids)
        {
            if (b.Amount <= 0) continue;
            int order = ((b.Player - a.StartedBy - 1) % n + n) % n;
            if (b.Amount > best || (b.Amount == best && order < bestOrder))
            {
                winner = b.Player;
                best = b.Amount;
                bestOrder = order;
            }
        }
        CompleteAuction(winner, best);
        return null;
    }

    private void CompleteAuction(int winner, int bid)
    {
        int tile = State.Auction!.Tile;
        int paid = bid;
        if (winner >= 0)
        {
            var w = State.Players[winner];
            if (Rules.AbilitiesEnabled && w.Ability == Ability.Investor && w.AbilityArmed) paid = bid * 75 / 100;
        }
        var before = DistrictOwners();
        Emit(new AuctionCompleted(tile, winner, bid, paid));
        EmitDistrictChanges(before);
        SettlePhase();
    }

    // ------------------------------------------------------------------ development

    private string? Build(BuildHouseCommand c)
    {
        if (!CanInvest(c.PlayerId)) return "You cannot build now";
        string? error = Calc.BuildError(State, c.PlayerId, c.Tile);
        if (error != null) return error;
        var prop = State.Properties[c.Tile]!;
        var type = prop.Level > 0 ? prop.DevType
            : Rules.AdvancedDevelopment ? c.DevType : DevelopmentType.Residential;
        bool discount = Calc.BuilderDiscountAvailable(State, c.PlayerId);
        int cost = Calc.BuildCost(State, c.Tile, c.PlayerId, type);
        if (State.Players[c.PlayerId].Money < cost) return "Not enough money";
        Emit(new BuildingConstructed(c.PlayerId, c.Tile, prop.Level + 1, cost, type, discount));
        return null;
    }

    private string? Sell(SellHouseCommand c)
    {
        if (!CanLiquidate(c.PlayerId)) return "You cannot sell now";
        string? error = Calc.SellError(State, c.PlayerId, c.Tile);
        if (error != null) return error;
        var prop = State.Properties[c.Tile]!;
        // A landmark breaks down into as many buildings as the bank can supply.
        int newLevel = prop.Level == 5 ? Math.Min(4, State.Bank.HousesLeft) : prop.Level - 1;
        int refund = (prop.Level - newLevel) * Calc.SellRefund(State, c.Tile);
        Emit(new BuildingSold(c.PlayerId, c.Tile, newLevel, refund));
        TrySettleDebts();
        return null;
    }

    private string? Mortgage(MortgageCommand c)
    {
        if (!CanLiquidate(c.PlayerId)) return "You cannot mortgage now";
        string? error = Calc.MortgageError(State, c.PlayerId, c.Tile);
        if (error != null) return error;
        Emit(new PropertyMortgaged(c.PlayerId, c.Tile, Calc.MortgageValue(State, c.Tile)));
        TrySettleDebts();
        return null;
    }

    private string? Unmortgage(UnmortgageCommand c)
    {
        if (!CanInvest(c.PlayerId)) return "You cannot unmortgage now";
        var prop = State.Property(c.Tile);
        if (prop == null || prop.Owner != c.PlayerId) return "You do not control this property";
        if (!prop.Mortgaged) return "Not mortgaged";
        int cost = Calc.UnmortgageCost(State, c.Tile, c.PlayerId);
        if (State.Players[c.PlayerId].Money < cost) return "Not enough money";
        Emit(new PropertyUnmortgaged(c.PlayerId, c.Tile, cost));
        return null;
    }

    // ------------------------------------------------------------------ trades

    private bool TradingOpen => InPhase(TurnPhase.PreRoll, TurnPhase.PostRoll, TurnPhase.BuyDecision, TurnPhase.DebtResolution);

    private string? CreateTrade(CreateTradeCommand c)
    {
        if (!Rules.TradingEnabled) return "Trading is disabled";
        if (!TradingOpen) return "Trading is closed right now";
        var p = State.Players[c.PlayerId];
        if (p.TradeProposalsThisTurn >= Rules.MaxTradeProposalsPerTurn) return "Too many proposals this turn";
        if (State.Trades.Any(t => t.From == c.PlayerId)) return "You already have a pending offer";
        string? error = ValidateTrade(c.PlayerId, c.To, c.Give, c.Receive);
        if (error != null) return error;
        Emit(new TradeProposed(new TradeOffer
        {
            Id = State.NextId, From = c.PlayerId, To = c.To, Give = c.Give.Clone(), Receive = c.Receive.Clone(),
        }));
        return null;
    }

    private string? ModifyTrade(ModifyTradeCommand c)
    {
        var offer = State.Trades.FirstOrDefault(t => t.Id == c.TradeId);
        if (offer == null) return "Offer no longer exists";
        if (offer.From != c.PlayerId) return "Only the proposer can modify an offer";
        if (!TradingOpen) return "Trading is closed right now";
        string? error = ValidateTrade(offer.From, offer.To, c.Give, c.Receive);
        if (error != null) return error;
        Emit(new TradeModified(new TradeOffer
        {
            Id = offer.Id, From = offer.From, To = offer.To, Give = c.Give.Clone(), Receive = c.Receive.Clone(),
        }));
        return null;
    }

    private string? CounterTrade(CounterTradeCommand c)
    {
        var offer = State.Trades.FirstOrDefault(t => t.Id == c.TradeId);
        if (offer == null) return "Offer no longer exists";
        if (offer.To != c.PlayerId) return "Only the recipient can counter an offer";
        if (!TradingOpen) return "Trading is closed right now";
        string? error = ValidateTrade(c.PlayerId, offer.From, c.Give, c.Receive);
        if (error != null) return error;
        Emit(new TradeCountered(offer.Id, new TradeOffer
        {
            Id = State.NextId, From = c.PlayerId, To = offer.From, Give = c.Give.Clone(), Receive = c.Receive.Clone(),
        }));
        return null;
    }

    private string? AcceptTrade(AcceptTradeCommand c)
    {
        var offer = State.Trades.FirstOrDefault(t => t.Id == c.TradeId);
        if (offer == null) return "Offer no longer exists";
        if (offer.To != c.PlayerId) return "This offer is not addressed to you";
        if (!TradingOpen) return "Trading is closed right now";
        string? error = ValidateTrade(offer.From, offer.To, offer.Give, offer.Receive);
        if (error != null) return error;
        var before = DistrictOwners();
        Emit(new TradeAccepted(offer.Clone()));
        EmitDistrictChanges(before);
        TrySettleDebts();
        return null;
    }

    private string? RejectTrade(RejectTradeCommand c)
    {
        var offer = State.Trades.FirstOrDefault(t => t.Id == c.TradeId);
        if (offer == null) return "Offer no longer exists";
        if (offer.To != c.PlayerId) return "This offer is not addressed to you";
        Emit(new TradeRejected(offer.Id, c.PlayerId));
        return null;
    }

    private string? CancelTrade(CancelTradeCommand c)
    {
        var offer = State.Trades.FirstOrDefault(t => t.Id == c.TradeId);
        if (offer == null) return "Offer no longer exists";
        if (offer.From != c.PlayerId) return "Only the proposer can cancel an offer";
        Emit(new TradeCancelled(offer.Id));
        return null;
    }

    /// <summary>Full legality check for a trade; also used right before acceptance.</summary>
    public string? ValidateTrade(int from, int to, TradeSide give, TradeSide receive) =>
        TradeRules.Validate(State, from, to, give, receive);

    // ------------------------------------------------------------------ advanced actions

    private string? ExerciseOption(ExerciseOptionCommand c)
    {
        if (!Rules.ContractsEnabled) return "Contracts are disabled";
        if (!CanInvest(c.PlayerId)) return "You cannot do that now";
        var contract = State.Contracts.FirstOrDefault(x => x.Id == c.ContractId);
        if (contract == null || contract.Kind != ContractKind.BuyOption) return "No such option";
        if (contract.Beneficiary != c.PlayerId) return "This option is not yours";
        var prop = State.Properties[contract.Tile]!;
        if (prop.Owner != contract.Grantor) return "The property changed hands";
        if (Board.Tiles[contract.Tile].Type == TileType.Street && Calc.DistrictHasBuildings(State, Board.Tiles[contract.Tile].District))
            return "The district has buildings";
        if (State.Players[c.PlayerId].Money < contract.Amount) return "Not enough money";
        var before = DistrictOwners();
        Emit(new OptionExercised(contract.Id, c.PlayerId, contract.Grantor, contract.Tile, contract.Amount));
        EmitDistrictChanges(before);
        return null;
    }

    private string? UseAbility(UseAbilityCommand c)
    {
        if (!Rules.AbilitiesEnabled) return "Abilities are disabled";
        var p = State.Players[c.PlayerId];
        if (p.Ability is not (Ability.Investor or Ability.Negotiator)) return "Your ability is passive";
        if (p.AbilityUsed) return "Ability already used";
        Emit(new AbilityUsed(p.Id, p.Ability));
        return null;
    }

    private string? TransferMoney(TransferMoneyCommand c)
    {
        if (!Rules.TeamsEnabled) return "Transfers are only available in team games";
        if (!TradingOpen) return "You cannot transfer money now";
        if (c.To == c.PlayerId || c.To < 0 || c.To >= State.Players.Count) return "Invalid recipient";
        if (!State.SameTeam(c.PlayerId, c.To) || State.Players[c.To].Bankrupt) return "You can only pay an active teammate";
        if (c.Amount <= 0 || c.Amount > State.Players[c.PlayerId].Money) return "Invalid amount";
        Emit(new MoneyTransferred(c.PlayerId, c.To, c.Amount, MoneyReason.Gift));
        TrySettleDebts();
        return null;
    }

    private string? Contribute(ContributeToProjectCommand c)
    {
        if (!Rules.PublicProjectsEnabled) return "Public projects are disabled";
        if (!CanInvest(c.PlayerId)) return "You cannot contribute now";
        var project = State.Project;
        if (project == null) return "No project is seeking funds";
        int missing = project.Cost - project.Funded;
        if (c.Amount <= 0 || c.Amount > missing) return "Invalid amount";
        if (c.Amount > State.Players[c.PlayerId].Money) return "Not enough money";
        Emit(new ProjectContribution(c.PlayerId, c.Amount));
        if (project.Funded < project.Cost) return null;

        var top = project.Contributions.OrderByDescending(x => x.Amount).ThenBy(x => x.Player).First();
        var effect = new ActiveEffect
        {
            Source = "project", Name = project.Name,
            Description = $"{project.Name} completed: rent +{project.RentPercent}% nearby",
            Scope = EffectScope.Side, ScopeValue = project.Side.ToString(),
            RentPercent = project.RentPercent, RoundsLeft = project.EffectRounds,
        };
        Emit(new ProjectCompleted(project.Name, effect, top.Player, project.Cost / 10));
        return null;
    }

    /// <summary>Round-start bookkeeping for contracts: instalments are charged, durations run down.</summary>
    public void TickContracts()
    {
        foreach (var c in State.Contracts.ToList())
        {
            if (c.Kind == ContractKind.RentImmunity) continue;
            if (c.Kind == ContractKind.Installment)
                Charge(c.Grantor, c.Beneficiary, c.Amount, DebtReason.Contract, -1, TurnPhase.PreRoll);
            Emit(new ContractTicked(c.Id));
        }
    }
}
