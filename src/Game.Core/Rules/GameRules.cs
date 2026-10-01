namespace Game.Core.Rules;

public enum AuctionMode
{
    Classic,
    Rapid,
}

public enum Ability
{
    None,
    Investor,
    Builder,
    Negotiator,
    Banker,
}

public enum BotLevel
{
    Easy,
    Medium,
    Hard,
    Expert,
}

/// <summary>
/// Every game mode is a configuration of this class; modes are never code forks.
/// </summary>
public sealed class GameRules
{
    public string PresetId { get; set; } = "classic";
    public string PresetName { get; set; } = "Classic";

    public int StartingMoney { get; set; } = 1500;
    public int Salary { get; set; } = 200;
    public bool DoubleSalaryOnStart { get; set; }
    public bool FreeParkingJackpot { get; set; }

    public bool AuctionsEnabled { get; set; } = true;
    public AuctionMode AuctionMode { get; set; } = AuctionMode.Classic;
    public int AuctionMinIncrement { get; set; } = 10;

    public bool TradingEnabled { get; set; } = true;
    public int MaxTradeProposalsPerTurn { get; set; } = 3;

    public int MaxJailTurns { get; set; } = 3;
    public bool CollectRentInJail { get; set; } = true;

    public bool EvenBuild { get; set; } = true;
    public int HouseSupply { get; set; } = 32;
    public int HotelSupply { get; set; } = 12;
    public int HouseCostPercent { get; set; } = 100;
    public int MortgageInterestPercent { get; set; } = 10;

    /// <summary>0 disables the turn timer.</summary>
    public int TurnTimeSeconds { get; set; }
    /// <summary>0 means play until one player (or team) remains.</summary>
    public int MaximumRounds { get; set; }
    /// <summary>End the match as soon as anyone goes bankrupt and rank by net worth.</summary>
    public bool EndOnFirstBankruptcy { get; set; }
    /// <summary>Random properties dealt to each player at match start.</summary>
    public int StartingProperties { get; set; }

    public bool MarketEventsEnabled { get; set; }
    public bool SecretObjectivesEnabled { get; set; }
    public int ObjectivesPerPlayer { get; set; } = 2;
    public bool PropertySharesEnabled { get; set; }
    public bool ContractsEnabled { get; set; }
    public bool AbilitiesEnabled { get; set; }
    public bool PublicProjectsEnabled { get; set; }
    public bool AdvancedDevelopment { get; set; }

    public bool TeamsEnabled { get; set; }
    public bool TeamRentExempt { get; set; } = true;

    public GameRules Clone() => (GameRules)MemberwiseClone();
}

public static class RulePresets
{
    public static readonly string[] Ids =
        { "classic", "classic_plus", "quick", "blitz", "market_mayhem", "tycoon", "team_empire" };

    public static GameRules Get(string id) => id switch
    {
        "classic" => new GameRules(),
        "classic_plus" => new GameRules
        {
            PresetId = id, PresetName = "Classic+",
            AuctionMode = AuctionMode.Rapid, SecretObjectivesEnabled = true,
        },
        "quick" => new GameRules
        {
            PresetId = id, PresetName = "Quick Match",
            StartingMoney = 1000, StartingProperties = 2, HouseCostPercent = 75,
            AuctionMode = AuctionMode.Rapid, TurnTimeSeconds = 45, MaximumRounds = 25,
            EndOnFirstBankruptcy = true,
        },
        "blitz" => new GameRules
        {
            PresetId = id, PresetName = "Blitz",
            StartingMoney = 1000, StartingProperties = 3, HouseCostPercent = 50,
            AuctionMode = AuctionMode.Rapid, TurnTimeSeconds = 30, MaximumRounds = 12,
            SecretObjectivesEnabled = true, EvenBuild = false,
        },
        "market_mayhem" => new GameRules
        {
            PresetId = id, PresetName = "Market Mayhem",
            MarketEventsEnabled = true, PublicProjectsEnabled = true, AuctionMode = AuctionMode.Rapid,
        },
        "tycoon" => new GameRules
        {
            PresetId = id, PresetName = "Tycoon",
            PropertySharesEnabled = true, ContractsEnabled = true, AdvancedDevelopment = true,
            AbilitiesEnabled = true,
        },
        "team_empire" => new GameRules
        {
            PresetId = id, PresetName = "Team Empire",
            TeamsEnabled = true, AuctionMode = AuctionMode.Rapid,
        },
        _ => throw new ArgumentException($"Unknown preset '{id}'"),
    };
}

public sealed class PlayerSetup
{
    public string Name { get; set; } = "Player";
    public int Token { get; set; }
    public bool IsBot { get; set; }
    public BotLevel BotLevel { get; set; } = BotLevel.Medium;
    public int Team { get; set; } = -1;
    public Ability Ability { get; set; } = Ability.None;
}

public sealed class MatchConfig
{
    public string MatchId { get; set; } = "";
    public ulong Seed { get; set; }
    public string BoardId { get; set; } = Board.BoardLibrary.DefaultBoardId;
    public GameRules Rules { get; set; } = new();
    public List<PlayerSetup> Players { get; set; } = new();
}
