using System.Text.Json.Serialization;
using Game.Core.Board;
using Game.Core.Rng;
using Game.Core.Rules;

namespace Game.Core.State;

public enum TurnPhase
{
    /// <summary>Current player may manage assets and must roll (or deal with jail).</summary>
    PreRoll,
    /// <summary>Current player landed on an unowned property and must buy or decline.</summary>
    BuyDecision,
    Auction,
    /// <summary>The first entry of <see cref="GameState.Debts"/> must be settled by its debtor.</summary>
    DebtResolution,
    /// <summary>Current player may manage assets and must end the turn.</summary>
    PostRoll,
    GameOver,
}

public enum GameEndReason
{
    None,
    LastStanding,
    RoundLimit,
    FirstBankruptcy,
}

public enum DevelopmentType
{
    Residential,
    Commercial,
    Luxury,
    Industrial,
}

public enum EconomyPhase
{
    Stable,
    Boom,
    Recession,
    Bubble,
    Recovery,
}

public enum EffectScope
{
    All,
    District,
    TileType,
    Side,
    NearTransit,
}

public enum ContractKind
{
    /// <summary>Beneficiary pays no rent on the tile for the next N visits.</summary>
    RentImmunity,
    /// <summary>Beneficiary receives a percentage of the rent the tile earns for N rounds.</summary>
    RevenueShare,
    /// <summary>Grantor pays a fixed amount to the beneficiary at the start of each of N rounds.</summary>
    Installment,
    /// <summary>Beneficiary may buy the tile for a fixed price within N rounds.</summary>
    BuyOption,
}

public sealed class PlayerStats
{
    public int Rolls { get; set; }
    public int DiceTotal { get; set; }
    public int Doubles { get; set; }
    public int PropertiesBought { get; set; }
    public int AuctionsWon { get; set; }
    public int TradesProposed { get; set; }
    public int TradesCompleted { get; set; }
    public int RentEarned { get; set; }
    public int RentPaid { get; set; }
    public int LargestPayment { get; set; }
    public int HousesBuilt { get; set; }
    public int TimesJailed { get; set; }
    public int SalaryCollected { get; set; }
    public int TaxesPaid { get; set; }
    public int MoneySpentOnProperty { get; set; }
    public int ObjectivesCompleted { get; set; }
    public int ProjectContributions { get; set; }
    public int EliminatedOnTurn { get; set; }
    public int PlayersBankrupted { get; set; }

    public PlayerStats Clone() => (PlayerStats)MemberwiseClone();
}

public sealed class ObjectiveProgress
{
    /// <summary>Catalog id, or -1 when hidden from the viewer.</summary>
    public int Id { get; set; }
    public bool Completed { get; set; }

    public ObjectiveProgress Clone() => new() { Id = Id, Completed = Completed };
}

public sealed class PlayerState
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Token { get; set; }
    public int Team { get; set; } = -1;
    public bool IsBot { get; set; }
    public int Position { get; set; }
    public int Money { get; set; }
    public bool InJail { get; set; }
    public int JailTurns { get; set; }
    public int JailCards { get; set; }
    public bool Bankrupt { get; set; }
    public Ability Ability { get; set; }
    public bool AbilityUsed { get; set; }
    /// <summary>Investor financing or Negotiator waiver is primed for its next trigger.</summary>
    public bool AbilityArmed { get; set; }
    /// <summary>Round in which the Builder discount was last consumed.</summary>
    public int BuilderDiscountRound { get; set; }
    public int TradeProposalsThisTurn { get; set; }
    public List<ObjectiveProgress> Objectives { get; set; } = new();
    public PlayerStats Stats { get; set; } = new();

    public PlayerState Clone()
    {
        var c = (PlayerState)MemberwiseClone();
        c.Objectives = Objectives.Select(o => o.Clone()).ToList();
        c.Stats = Stats.Clone();
        return c;
    }
}

public sealed class Share
{
    public int Player { get; set; }
    public int Percent { get; set; }

    public Share Clone() => new() { Player = Player, Percent = Percent };
}

public sealed class PropertyState
{
    public int Tile { get; set; }
    /// <summary>Controlling player, or -1 when owned by the bank.</summary>
    public int Owner { get; set; } = -1;
    public bool Mortgaged { get; set; }
    /// <summary>0..4 buildings, 5 = landmark.</summary>
    public int Level { get; set; }
    public DevelopmentType DevType { get; set; }
    /// <summary>Fractional ownership; empty unless the syndicate module is enabled.</summary>
    public List<Share> Shares { get; set; } = new();
    public int RentCollected { get; set; }

    public PropertyState Clone()
    {
        var c = (PropertyState)MemberwiseClone();
        c.Shares = Shares.Select(s => s.Clone()).ToList();
        return c;
    }

    public int ShareOf(int player)
    {
        if (Owner < 0) return 0;
        if (Shares.Count == 0) return Owner == player ? 100 : 0;
        foreach (var s in Shares)
            if (s.Player == player) return s.Percent;
        return 0;
    }
}

public sealed class BankState
{
    public int HousesLeft { get; set; }
    public int HotelsLeft { get; set; }
    public int Jackpot { get; set; }

    public BankState Clone() => (BankState)MemberwiseClone();
}

public sealed class SealedBid
{
    public int Player { get; set; }
    /// <summary>-1 when hidden from the viewer.</summary>
    public int Amount { get; set; }

    public SealedBid Clone() => new() { Player = Player, Amount = Amount };
}

public sealed class AuctionState
{
    public int Tile { get; set; }
    public AuctionMode Mode { get; set; }
    public int StartedBy { get; set; }
    public int HighBid { get; set; }
    public int HighBidder { get; set; } = -1;
    public List<int> Participants { get; set; } = new();
    public List<int> Passed { get; set; } = new();
    public List<SealedBid> SealedBids { get; set; } = new();

    public AuctionState Clone() => new()
    {
        Tile = Tile, Mode = Mode, StartedBy = StartedBy, HighBid = HighBid, HighBidder = HighBidder,
        Participants = new List<int>(Participants), Passed = new List<int>(Passed),
        SealedBids = SealedBids.Select(b => b.Clone()).ToList(),
    };

    public bool HasSubmitted(int player) => SealedBids.Any(b => b.Player == player);
}

public enum DebtReason
{
    Rent,
    Tax,
    Card,
    JailFine,
    Contract,
}

public sealed class DebtState
{
    public int Debtor { get; set; }
    /// <summary>-1 when owed to the bank.</summary>
    public int Creditor { get; set; } = -1;
    public int Amount { get; set; }
    public DebtReason Reason { get; set; }
    public int Tile { get; set; } = -1;

    public DebtState Clone() => (DebtState)MemberwiseClone();
}

public sealed class ShareTransfer
{
    public int Tile { get; set; }
    public int Percent { get; set; }

    public ShareTransfer Clone() => new() { Tile = Tile, Percent = Percent };
}

public sealed class ContractTerm
{
    public ContractKind Kind { get; set; }
    public int Tile { get; set; } = -1;
    public int Percent { get; set; }
    public int Amount { get; set; }
    /// <summary>Visits for immunity, rounds for everything else.</summary>
    public int Count { get; set; }

    public ContractTerm Clone() => (ContractTerm)MemberwiseClone();
}

/// <summary>What one party hands over in a trade.</summary>
public sealed class TradeSide
{
    public int Money { get; set; }
    public List<int> Tiles { get; set; } = new();
    public int JailCards { get; set; }
    public List<ShareTransfer> Shares { get; set; } = new();
    public List<ContractTerm> Terms { get; set; } = new();

    [JsonIgnore]
    public bool IsEmpty => Money == 0 && Tiles.Count == 0 && JailCards == 0 && Shares.Count == 0 && Terms.Count == 0;

    public TradeSide Clone() => new()
    {
        Money = Money, Tiles = new List<int>(Tiles), JailCards = JailCards,
        Shares = Shares.Select(s => s.Clone()).ToList(), Terms = Terms.Select(t => t.Clone()).ToList(),
    };
}

public sealed class TradeOffer
{
    public int Id { get; set; }
    public int From { get; set; }
    public int To { get; set; }
    public TradeSide Give { get; set; } = new();
    public TradeSide Receive { get; set; } = new();

    public TradeOffer Clone() => new() { Id = Id, From = From, To = To, Give = Give.Clone(), Receive = Receive.Clone() };
}

public sealed class Contract
{
    public int Id { get; set; }
    public ContractKind Kind { get; set; }
    public int Grantor { get; set; }
    public int Beneficiary { get; set; }
    public int Tile { get; set; } = -1;
    public int Percent { get; set; }
    public int Amount { get; set; }
    public int Remaining { get; set; }

    public Contract Clone() => (Contract)MemberwiseClone();
}

public sealed class ActiveEffect
{
    public int Id { get; set; }
    /// <summary>"economy", "event", "project".</summary>
    public string Source { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public EffectScope Scope { get; set; }
    /// <summary>District id, <see cref="TileType"/> name or side number depending on scope.</summary>
    public string ScopeValue { get; set; } = "";
    public int RentPercent { get; set; }
    public int PricePercent { get; set; }
    public int BuildCostPercent { get; set; }
    public int TaxPercent { get; set; }
    /// <summary>-1 lasts until replaced.</summary>
    public int RoundsLeft { get; set; }

    public ActiveEffect Clone() => (ActiveEffect)MemberwiseClone();
}

public sealed class Contribution
{
    public int Player { get; set; }
    public int Amount { get; set; }

    public Contribution Clone() => new() { Player = Player, Amount = Amount };
}

public sealed class ProjectState
{
    public string Name { get; set; } = "";
    public int Cost { get; set; }
    public int Side { get; set; }
    public int RentPercent { get; set; }
    public int EffectRounds { get; set; }
    public int RoundsLeft { get; set; }
    public List<Contribution> Contributions { get; set; } = new();

    [JsonIgnore]
    public int Funded => Contributions.Sum(c => c.Amount);

    public ProjectState Clone()
    {
        var c = (ProjectState)MemberwiseClone();
        c.Contributions = Contributions.Select(x => x.Clone()).ToList();
        return c;
    }
}

public sealed class Standing
{
    public int Player { get; set; }
    public int NetWorth { get; set; }
    public int Rank { get; set; }

    public Standing Clone() => (Standing)MemberwiseClone();
}

/// <summary>
/// The complete deterministic state of a match. The authority holds the full state; replicas hold
/// the same object with private fields redacted (see <see cref="RedactedFor"/>).
/// </summary>
public sealed class GameState
{
    public string MatchId { get; set; } = "";
    public string BoardId { get; set; } = "";
    public GameRules Rules { get; set; } = new();
    public int Version { get; set; }
    public int Round { get; set; } = 1;
    public int TurnNumber { get; set; }
    public int CurrentPlayer { get; set; }
    public TurnPhase Phase { get; set; }
    /// <summary>Phase to restore once all debts are settled; null resolves from the dice state.</summary>
    public TurnPhase? ResumePhase { get; set; }
    public int DoublesCount { get; set; }
    public int Die1 { get; set; }
    public int Die2 { get; set; }
    public List<PlayerState> Players { get; set; } = new();
    /// <summary>Indexed by tile; null for tiles that cannot be owned.</summary>
    public List<PropertyState?> Properties { get; set; } = new();
    public BankState Bank { get; set; } = new();
    public List<int> FortuneDeck { get; set; } = new();
    public List<int> CivicDeck { get; set; } = new();
    public AuctionState? Auction { get; set; }
    public List<DebtState> Debts { get; set; } = new();
    public List<TradeOffer> Trades { get; set; } = new();
    public List<Contract> Contracts { get; set; } = new();
    public List<ActiveEffect> Effects { get; set; } = new();
    public EconomyPhase Economy { get; set; }
    public ProjectState? Project { get; set; }
    public int NextId { get; set; } = 1;
    public List<int> Winners { get; set; } = new();
    public List<Standing> Standings { get; set; } = new();
    public GameEndReason EndReason { get; set; }
    /// <summary>Authority only. Zeroed in every redacted view.</summary>
    public RngState Rng { get; set; } = new();

    [JsonIgnore]
    public BoardDefinition Board { get; set; } = null!;

    [JsonIgnore]
    public bool IsOver => Phase == TurnPhase.GameOver;

    public PlayerState Player(int id) => Players[id];

    public PropertyState? Property(int tile) => tile >= 0 && tile < Properties.Count ? Properties[tile] : null;

    public IEnumerable<PlayerState> ActivePlayers => Players.Where(p => !p.Bankrupt);

    public IEnumerable<PropertyState> OwnedBy(int player)
    {
        foreach (var p in Properties)
            if (p != null && p.Owner == player) yield return p;
    }

    public bool SameTeam(int a, int b) =>
        a == b || (Rules.TeamsEnabled && a >= 0 && b >= 0 && Players[a].Team >= 0 && Players[a].Team == Players[b].Team);

    public GameState Clone()
    {
        var c = (GameState)MemberwiseClone();
        c.Rules = Rules;
        c.Players = Players.Select(p => p.Clone()).ToList();
        c.Properties = Properties.Select(p => p?.Clone()).ToList();
        c.Bank = Bank.Clone();
        c.FortuneDeck = new List<int>(FortuneDeck);
        c.CivicDeck = new List<int>(CivicDeck);
        c.Auction = Auction?.Clone();
        c.Debts = Debts.Select(d => d.Clone()).ToList();
        c.Trades = Trades.Select(t => t.Clone()).ToList();
        c.Contracts = Contracts.Select(x => x.Clone()).ToList();
        c.Effects = Effects.Select(e => e.Clone()).ToList();
        c.Project = Project?.Clone();
        c.Winners = new List<int>(Winners);
        c.Standings = Standings.Select(s => s.Clone()).ToList();
        c.Rng = Rng.Clone();
        return c;
    }

    /// <summary>
    /// Copy with everything the viewer must not know removed. Pass -1 for the public
    /// (spectator) view, which is also the view state hashes are computed over.
    /// </summary>
    public GameState RedactedFor(int viewer) => RedactedFor(id => id == viewer);

    /// <summary>Redacted copy for a device that controls several seats (pass and play).</summary>
    public GameState RedactedFor(IReadOnlyCollection<int> viewers) => RedactedFor(viewers.Contains);

    private GameState RedactedFor(Func<int, bool> canSee)
    {
        var c = Clone();
        c.Rng = new RngState();
        if (c.Auction != null)
            foreach (var b in c.Auction.SealedBids)
                if (!canSee(b.Player)) b.Amount = -1;
        foreach (var p in c.Players)
        {
            if (canSee(p.Id)) continue;
            foreach (var o in p.Objectives)
                if (!o.Completed) o.Id = -1;
        }
        return c;
    }
}
