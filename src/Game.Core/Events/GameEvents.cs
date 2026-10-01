using Game.Core.Board;
using Game.Core.Rules;
using Game.Core.State;

namespace Game.Core.Events;

/// <summary>
/// The only authoritative description of what happened. Every event carries enough data to
/// mutate a replica deterministically, so the authority and every client run the same reducer.
/// </summary>
public abstract record GameEvent
{
    /// <summary>State version after this event has been applied.</summary>
    public int Version { get; set; }

    public abstract void Apply(GameState s);

    /// <summary>Copy that is safe to send to the given viewer (-1 = spectator).</summary>
    public virtual GameEvent RedactFor(int viewer) => this;

    /// <summary>Copy for a device controlling several seats: visible if any of them may see it.</summary>
    public GameEvent RedactFor(IReadOnlyCollection<int> viewers)
    {
        foreach (int v in viewers)
        {
            var e = RedactFor(v);
            if (ReferenceEquals(e, this)) return this;
        }
        return RedactFor(-1);
    }
}

public enum MoveKind
{
    Walk,
    Jump,
    Backward,
}

public enum MoneyReason
{
    Tax,
    Card,
    JailFine,
    Gift,
    Contract,
    ProjectBonus,
    Jackpot,
}

public enum JailExit
{
    Doubles,
    Fine,
    Card,
}

internal static class Reduce
{
    public static void Pay(GameState s, int from, int to, int amount)
    {
        if (from >= 0)
        {
            var p = s.Players[from];
            p.Money -= amount;
            if (amount > p.Stats.LargestPayment) p.Stats.LargestPayment = amount;
        }
        if (to >= 0) s.Players[to].Money += amount;
    }

    public static bool FeedsJackpot(GameState s, int to, bool penalty) =>
        to < 0 && penalty && s.Rules.FreeParkingJackpot;

    public static void SetOwner(GameState s, PropertyState p, int owner)
    {
        p.Owner = owner;
        p.Shares.Clear();
        if (owner >= 0 && s.Rules.PropertySharesEnabled) p.Shares.Add(new Share { Player = owner, Percent = 100 });
    }

    public static void MoveShare(PropertyState p, int from, int to, int percent)
    {
        if (percent <= 0) return;
        var src = p.Shares.FirstOrDefault(x => x.Player == from);
        if (src == null) return;
        percent = Math.Min(percent, src.Percent);
        src.Percent -= percent;
        if (src.Percent == 0) p.Shares.Remove(src);
        var dst = p.Shares.FirstOrDefault(x => x.Player == to);
        if (dst == null) p.Shares.Add(new Share { Player = to, Percent = percent });
        else dst.Percent += percent;
    }

    /// <summary>Control follows the largest stake; ties keep the current controller.</summary>
    public static void RecomputeControl(PropertyState p)
    {
        if (p.Shares.Count == 0) return;
        int current = p.ShareOf(p.Owner);
        foreach (var sh in p.Shares)
        {
            if (sh.Percent > current)
            {
                current = sh.Percent;
                p.Owner = sh.Player;
            }
        }
    }

    /// <summary>Transfers control of a tile, keeping minority stakes held by third parties.</summary>
    public static void TransferTile(GameState s, PropertyState p, int from, int to)
    {
        if (p.Shares.Count > 0) MoveShare(p, from, to, p.ShareOf(from));
        p.Owner = to;
        RecomputeControl(p);
        s.Contracts.RemoveAll(c => c.Tile == p.Tile && c.Kind != ContractKind.Installment);
    }

    public static void ReturnBuildings(GameState s, PropertyState p, int newLevel)
    {
        if (p.Level == 5)
        {
            s.Bank.HotelsLeft++;
            s.Bank.HousesLeft -= newLevel;
        }
        else
        {
            s.Bank.HousesLeft += p.Level - newLevel;
        }
        p.Level = newLevel;
    }

    public static void AddEffect(GameState s, ActiveEffect e)
    {
        var c = e.Clone();
        c.Id = s.NextId++;
        s.Effects.Add(c);
    }
}

// ------------------------------------------------------------------ match / turn flow

public sealed record MatchStarted : GameEvent
{
    public override void Apply(GameState s) { }
}

public sealed record PropertyGranted(int Player, int Tile) : GameEvent
{
    public override void Apply(GameState s) => Reduce.SetOwner(s, s.Properties[Tile]!, Player);
}

public sealed record RoundStarted(int Round) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Round = Round;
        foreach (var e in s.Effects)
            if (e.RoundsLeft > 0) e.RoundsLeft--;
        s.Effects.RemoveAll(e => e.RoundsLeft == 0);
        if (s.Project != null && s.Project.RoundsLeft > 0) s.Project.RoundsLeft--;
    }
}

public sealed record TurnStarted(int Player, int TurnNumber) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.CurrentPlayer = Player;
        s.TurnNumber = TurnNumber;
        s.DoublesCount = 0;
        s.Phase = TurnPhase.PreRoll;
        s.ResumePhase = null;
        s.Trades.Clear();
        foreach (var p in s.Players) p.TradeProposalsThisTurn = 0;
    }
}

public sealed record TurnEnded(int Player) : GameEvent
{
    public override void Apply(GameState s) { }
}

public sealed record PhaseChanged(TurnPhase Phase) : GameEvent
{
    public override void Apply(GameState s) => s.Phase = Phase;
}

public sealed record DiceRolled(int Player, int D1, int D2, int DoublesCount) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Die1 = D1;
        s.Die2 = D2;
        s.DoublesCount = DoublesCount;
        var st = s.Players[Player].Stats;
        st.Rolls++;
        st.DiceTotal += D1 + D2;
        if (D1 == D2) st.Doubles++;
    }
}

public sealed record PlayerMoved(int Player, int From, int To, MoveKind Kind, bool PassedStart) : GameEvent
{
    public override void Apply(GameState s) => s.Players[Player].Position = To;
}

public sealed record SalaryPaid(int Player, int Amount) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Players[Player].Money += Amount;
        s.Players[Player].Stats.SalaryCollected += Amount;
    }
}

public sealed record TileLanded(int Player, int Tile) : GameEvent
{
    public override void Apply(GameState s) { }
}

public sealed record PlayerControlChanged(int Player, bool IsBot) : GameEvent
{
    public override void Apply(GameState s) => s.Players[Player].IsBot = IsBot;
}

// ------------------------------------------------------------------ property

public sealed record PurchaseOffered(int Player, int Tile, int Price) : GameEvent
{
    public override void Apply(GameState s) { }
}

public sealed record PropertyPurchased(int Player, int Tile, int Price) : GameEvent
{
    public override void Apply(GameState s)
    {
        var p = s.Players[Player];
        Reduce.Pay(s, Player, -1, Price);
        Reduce.SetOwner(s, s.Properties[Tile]!, Player);
        p.Stats.PropertiesBought++;
        p.Stats.MoneySpentOnProperty += Price;
    }
}

public sealed record DistrictCompleted(int Player, string District) : GameEvent
{
    public override void Apply(GameState s) { }
}

public sealed record BuildingConstructed(int Player, int Tile, int Level, int Cost, DevelopmentType DevType, bool DiscountUsed) : GameEvent
{
    public override void Apply(GameState s)
    {
        var prop = s.Properties[Tile]!;
        var p = s.Players[Player];
        p.Money -= Cost;
        if (Level == 5)
        {
            s.Bank.HotelsLeft--;
            s.Bank.HousesLeft += 4;
        }
        else
        {
            s.Bank.HousesLeft--;
        }
        if (prop.Level == 0) prop.DevType = DevType;
        prop.Level = Level;
        p.Stats.HousesBuilt++;
        if (DiscountUsed) p.BuilderDiscountRound = s.Round;
    }
}

public sealed record BuildingSold(int Player, int Tile, int NewLevel, int Refund) : GameEvent
{
    public override void Apply(GameState s)
    {
        Reduce.ReturnBuildings(s, s.Properties[Tile]!, NewLevel);
        s.Players[Player].Money += Refund;
    }
}

public sealed record PropertyMortgaged(int Player, int Tile, int Amount) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Properties[Tile]!.Mortgaged = true;
        s.Players[Player].Money += Amount;
    }
}

public sealed record PropertyUnmortgaged(int Player, int Tile, int Cost) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Properties[Tile]!.Mortgaged = false;
        s.Players[Player].Money -= Cost;
    }
}

// ------------------------------------------------------------------ money

public sealed record RentPaid(int Payer, int Payee, int Tile, int Amount) : GameEvent
{
    public override void Apply(GameState s)
    {
        Reduce.Pay(s, Payer, Payee, Amount);
        s.Players[Payer].Stats.RentPaid += Amount;
        s.Players[Payee].Stats.RentEarned += Amount;
        s.Properties[Tile]!.RentCollected += Amount;
    }
}

/// <summary>Rent was not charged. A positive contract id consumes one immunity visit.</summary>
public sealed record RentWaived(int Player, int Tile, int ContractId, bool ByAbility) : GameEvent
{
    public override void Apply(GameState s)
    {
        if (ContractId > 0)
        {
            var c = s.Contracts.FirstOrDefault(x => x.Id == ContractId);
            if (c != null && --c.Remaining <= 0) s.Contracts.Remove(c);
        }
        if (ByAbility) s.Players[Player].AbilityArmed = false;
    }
}

/// <summary>Any non-rent payment. -1 is the bank.</summary>
public sealed record MoneyTransferred(int From, int To, int Amount, MoneyReason Reason) : GameEvent
{
    public override void Apply(GameState s)
    {
        Reduce.Pay(s, From, To, Amount);
        if (Reason == MoneyReason.Jackpot) s.Bank.Jackpot -= Amount;
        else if (Reduce.FeedsJackpot(s, To, Reason is MoneyReason.Tax or MoneyReason.Card or MoneyReason.JailFine))
            s.Bank.Jackpot += Amount;
        if (Reason == MoneyReason.Tax && From >= 0) s.Players[From].Stats.TaxesPaid += Amount;
    }
}

/// <summary>Resume is the phase to restore once the queue empties (null = derive from dice).</summary>
public sealed record DebtIncurred(DebtState Debt, TurnPhase? Resume) : GameEvent
{
    public override void Apply(GameState s)
    {
        if (s.Debts.Count == 0) s.ResumePhase = Resume;
        s.Debts.Add(Debt.Clone());
    }
}

/// <summary>The debt at the front of the queue was settled in full.</summary>
public sealed record DebtPaid(DebtState Debt) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Debts.RemoveAt(0);
        Reduce.Pay(s, Debt.Debtor, Debt.Creditor, Debt.Amount);
        if (Debt.Reason == DebtReason.Rent && Debt.Creditor >= 0)
        {
            s.Players[Debt.Debtor].Stats.RentPaid += Debt.Amount;
            s.Players[Debt.Creditor].Stats.RentEarned += Debt.Amount;
            if (Debt.Tile >= 0) s.Properties[Debt.Tile]!.RentCollected += Debt.Amount;
        }
        else if (Reduce.FeedsJackpot(s, Debt.Creditor, Debt.Reason is DebtReason.Tax or DebtReason.Card or DebtReason.JailFine))
        {
            s.Bank.Jackpot += Debt.Amount;
        }
        if (Debt.Reason == DebtReason.Tax) s.Players[Debt.Debtor].Stats.TaxesPaid += Debt.Amount;
    }
}

// ------------------------------------------------------------------ cards / jail

public sealed record CardDrawn(int Player, TileType Deck, int CardIndex) : GameEvent
{
    public override void Apply(GameState s)
    {
        var deck = Deck == TileType.Fortune ? s.FortuneDeck : s.CivicDeck;
        deck.Remove(CardIndex);
        if (deck.Count == 0)
        {
            int n = Deck == TileType.Fortune ? s.Board.FortuneCards.Count : s.Board.CivicCards.Count;
            for (int i = 0; i < n; i++) deck.Add(i);
        }
    }
}

public sealed record JailCardReceived(int Player) : GameEvent
{
    public override void Apply(GameState s) => s.Players[Player].JailCards++;
}

public sealed record PlayerSentToJail(int Player, int JailTile) : GameEvent
{
    public override void Apply(GameState s)
    {
        var p = s.Players[Player];
        p.Position = JailTile;
        p.InJail = true;
        p.JailTurns = 0;
        p.Stats.TimesJailed++;
        s.DoublesCount = 0;
    }
}

public sealed record JailTurnFailed(int Player, int Attempts) : GameEvent
{
    public override void Apply(GameState s) => s.Players[Player].JailTurns = Attempts;
}

public sealed record PlayerReleasedFromJail(int Player, JailExit Reason) : GameEvent
{
    public override void Apply(GameState s)
    {
        var p = s.Players[Player];
        p.InJail = false;
        p.JailTurns = 0;
        if (Reason == JailExit.Card) p.JailCards--;
    }
}

// ------------------------------------------------------------------ auctions

public sealed record AuctionStarted(int Tile, AuctionMode Mode, int StartedBy, int[] Participants) : GameEvent
{
    public override void Apply(GameState s) => s.Auction = new AuctionState
    {
        Tile = Tile, Mode = Mode, StartedBy = StartedBy, Participants = Participants.ToList(),
    };
}

public sealed record BidPlaced(int Player, int Amount) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Auction!.HighBid = Amount;
        s.Auction.HighBidder = Player;
    }
}

public sealed record AuctionPassed(int Player) : GameEvent
{
    public override void Apply(GameState s) => s.Auction!.Passed.Add(Player);
}

public sealed record SealedBidPlaced(int Player, int Amount) : GameEvent
{
    public override void Apply(GameState s) => s.Auction!.SealedBids.Add(new SealedBid { Player = Player, Amount = Amount });

    public override GameEvent RedactFor(int viewer) => viewer == Player ? this : this with { Amount = -1 };
}

public sealed record SealedBidsRevealed(SealedBid[] Bids) : GameEvent
{
    public override void Apply(GameState s) => s.Auction!.SealedBids = Bids.Select(b => b.Clone()).ToList();
}

/// <summary>Winner is -1 when nobody bid. Paid differs from Bid when financing was used.</summary>
public sealed record AuctionCompleted(int Tile, int Winner, int Bid, int Paid) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Auction = null;
        if (Winner < 0) return;
        var p = s.Players[Winner];
        Reduce.Pay(s, Winner, -1, Paid);
        Reduce.SetOwner(s, s.Properties[Tile]!, Winner);
        p.Stats.AuctionsWon++;
        p.Stats.MoneySpentOnProperty += Paid;
        if (Paid < Bid) p.AbilityArmed = false;
    }
}

// ------------------------------------------------------------------ trades / contracts

public sealed record TradeProposed(TradeOffer Offer) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Trades.Add(Offer.Clone());
        if (Offer.Id >= s.NextId) s.NextId = Offer.Id + 1;
        var p = s.Players[Offer.From];
        p.TradeProposalsThisTurn++;
        p.Stats.TradesProposed++;
    }
}

public sealed record TradeModified(TradeOffer Offer) : GameEvent
{
    public override void Apply(GameState s)
    {
        int i = s.Trades.FindIndex(t => t.Id == Offer.Id);
        if (i >= 0) s.Trades[i] = Offer.Clone();
    }
}

public sealed record TradeCountered(int OldId, TradeOffer Offer) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Trades.RemoveAll(t => t.Id == OldId);
        s.Trades.Add(Offer.Clone());
        if (Offer.Id >= s.NextId) s.NextId = Offer.Id + 1;
    }
}

public sealed record TradeRejected(int TradeId, int By) : GameEvent
{
    public override void Apply(GameState s) => s.Trades.RemoveAll(t => t.Id == TradeId);
}

public sealed record TradeCancelled(int TradeId) : GameEvent
{
    public override void Apply(GameState s) => s.Trades.RemoveAll(t => t.Id == TradeId);
}

public sealed record TradeAccepted(TradeOffer Offer) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Trades.RemoveAll(t => t.Id == Offer.Id);
        MoveAssets(s, Offer.From, Offer.To, Offer.Give);
        MoveAssets(s, Offer.To, Offer.From, Offer.Receive);
        AddContracts(s, Offer.From, Offer.To, Offer.Give);
        AddContracts(s, Offer.To, Offer.From, Offer.Receive);
        s.Players[Offer.From].Stats.TradesCompleted++;
        s.Players[Offer.To].Stats.TradesCompleted++;
    }

    private static void MoveAssets(GameState s, int from, int to, TradeSide side)
    {
        s.Players[from].Money -= side.Money;
        s.Players[to].Money += side.Money;
        s.Players[from].JailCards -= side.JailCards;
        s.Players[to].JailCards += side.JailCards;
        foreach (int t in side.Tiles) Reduce.TransferTile(s, s.Properties[t]!, from, to);
        foreach (var sh in side.Shares)
        {
            var p = s.Properties[sh.Tile]!;
            int before = p.Owner;
            Reduce.MoveShare(p, from, to, sh.Percent);
            Reduce.RecomputeControl(p);
            if (p.Owner != before)
                s.Contracts.RemoveAll(c => c.Tile == p.Tile && c.Kind != ContractKind.Installment);
        }
    }

    private static void AddContracts(GameState s, int grantor, int beneficiary, TradeSide side)
    {
        foreach (var t in side.Terms)
        {
            s.Contracts.Add(new Contract
            {
                Id = s.NextId++, Kind = t.Kind, Grantor = grantor, Beneficiary = beneficiary,
                Tile = t.Tile, Percent = t.Percent, Amount = t.Amount, Remaining = t.Count,
            });
        }
    }
}

public sealed record ContractTicked(int ContractId) : GameEvent
{
    public override void Apply(GameState s)
    {
        var c = s.Contracts.FirstOrDefault(x => x.Id == ContractId);
        if (c != null && --c.Remaining <= 0) s.Contracts.Remove(c);
    }
}

public sealed record ContractExpired(int ContractId) : GameEvent
{
    public override void Apply(GameState s) => s.Contracts.RemoveAll(c => c.Id == ContractId);
}

public sealed record OptionExercised(int ContractId, int Buyer, int Seller, int Tile, int Price) : GameEvent
{
    public override void Apply(GameState s)
    {
        Reduce.Pay(s, Buyer, Seller, Price);
        Reduce.TransferTile(s, s.Properties[Tile]!, Seller, Buyer);
        s.Contracts.RemoveAll(c => c.Id == ContractId);
    }
}

// ------------------------------------------------------------------ advanced modules

public sealed record AbilityAssigned(int Player, Ability Ability) : GameEvent
{
    public override void Apply(GameState s) => s.Players[Player].Ability = Ability;
}

public sealed record AbilityUsed(int Player, Ability Ability) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Players[Player].AbilityUsed = true;
        s.Players[Player].AbilityArmed = true;
    }
}

public sealed record ObjectivesAssigned(int Player, int[] Ids) : GameEvent
{
    public override void Apply(GameState s) =>
        s.Players[Player].Objectives = Ids.Select(i => new ObjectiveProgress { Id = i }).ToList();

    public override GameEvent RedactFor(int viewer) =>
        viewer == Player ? this : this with { Ids = Ids.Select(_ => -1).ToArray() };
}

public sealed record ObjectiveCompleted(int Player, int Slot, int ObjectiveId, int Reward) : GameEvent
{
    public override void Apply(GameState s)
    {
        var p = s.Players[Player];
        p.Objectives[Slot].Id = ObjectiveId;
        p.Objectives[Slot].Completed = true;
        p.Money += Reward;
        p.Stats.ObjectivesCompleted++;
    }
}

public sealed record EconomyChanged(EconomyPhase Phase, ActiveEffect? Effect) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Economy = Phase;
        s.Effects.RemoveAll(e => e.Source == "economy");
        if (Effect != null) Reduce.AddEffect(s, Effect);
    }
}

public sealed record CityEventOccurred(ActiveEffect Effect) : GameEvent
{
    public override void Apply(GameState s) => Reduce.AddEffect(s, Effect);
}

public sealed record ProjectProposed(ProjectState Project) : GameEvent
{
    public override void Apply(GameState s) => s.Project = Project.Clone();
}

public sealed record ProjectContribution(int Player, int Amount) : GameEvent
{
    public override void Apply(GameState s)
    {
        var p = s.Players[Player];
        p.Money -= Amount;
        p.Stats.ProjectContributions += Amount;
        var c = s.Project!.Contributions.FirstOrDefault(x => x.Player == Player);
        if (c == null) s.Project.Contributions.Add(new Contribution { Player = Player, Amount = Amount });
        else c.Amount += Amount;
    }
}

public sealed record ProjectCompleted(string Name, ActiveEffect Effect, int TopContributor, int Bonus) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Project = null;
        Reduce.AddEffect(s, Effect);
        if (TopContributor >= 0) s.Players[TopContributor].Money += Bonus;
    }
}

public sealed record ProjectFailed(string Name) : GameEvent
{
    public override void Apply(GameState s)
    {
        if (s.Project == null) return;
        foreach (var c in s.Project.Contributions)
            if (!s.Players[c.Player].Bankrupt) s.Players[c.Player].Money += c.Amount;
        s.Project = null;
    }
}

// ------------------------------------------------------------------ end of game

/// <summary>
/// Liquidates the player: buildings are sold back at half price, then everything goes to the
/// creditor (another player) or back to the bank (-1).
/// </summary>
public sealed record PlayerBankrupt(int Player, int Creditor) : GameEvent
{
    public override void Apply(GameState s)
    {
        var p = s.Players[Player];
        foreach (var prop in s.Properties)
        {
            if (prop == null) continue;
            if (prop.Owner == Player)
            {
                if (prop.Level > 0)
                {
                    p.Money += Calc.BuildingValue(s, prop) / 2;
                    Reduce.ReturnBuildings(s, prop, 0);
                }
                if (Creditor >= 0)
                {
                    Reduce.TransferTile(s, prop, Player, Creditor);
                }
                else
                {
                    s.Contracts.RemoveAll(c => c.Tile == prop.Tile && c.Kind != ContractKind.Installment);
                    prop.Owner = -1;
                    prop.Mortgaged = false;
                    prop.Shares.Clear();
                    prop.DevType = DevelopmentType.Residential;
                }
            }
            else if (prop.Owner >= 0 && prop.ShareOf(Player) > 0)
            {
                Reduce.MoveShare(prop, Player, Creditor >= 0 ? Creditor : prop.Owner, prop.ShareOf(Player));
                Reduce.RecomputeControl(prop);
            }
        }
        if (Creditor >= 0)
        {
            var c = s.Players[Creditor];
            c.Money += Math.Max(0, p.Money);
            c.JailCards += p.JailCards;
            c.Stats.PlayersBankrupted++;
        }
        p.Money = 0;
        p.JailCards = 0;
        p.InJail = false;
        p.Bankrupt = true;
        p.Stats.EliminatedOnTurn = s.TurnNumber;
        s.Debts.RemoveAll(d => d.Debtor == Player);
        foreach (var d in s.Debts)
            if (d.Creditor == Player) d.Creditor = Creditor;
        s.Trades.RemoveAll(t => t.From == Player || t.To == Player);
        s.Contracts.RemoveAll(c => c.Grantor == Player || c.Beneficiary == Player);
        if (s.Project != null) s.Project.Contributions.RemoveAll(c => c.Player == Player);
    }
}

public sealed record GameEnded(int[] Winners, GameEndReason Reason, Standing[] Standings) : GameEvent
{
    public override void Apply(GameState s)
    {
        s.Phase = TurnPhase.GameOver;
        s.Winners = Winners.ToList();
        s.EndReason = Reason;
        s.Standings = Standings.Select(x => x.Clone()).ToList();
        s.Auction = null;
        s.Trades.Clear();
    }
}
