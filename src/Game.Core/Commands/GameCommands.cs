using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.State;

namespace Game.Core.Commands;

/// <summary>
/// A player's intent. Commands never carry results (no dice values, no money totals): the
/// authority validates them and answers with events.
/// </summary>
public abstract record GameCommand(int PlayerId)
{
    public string CommandId { get; init; } = Guid.NewGuid().ToString("N");
    public string MatchId { get; init; } = "";
    /// <summary>State version the sender acted on; -1 skips the stale check.</summary>
    public int ExpectedVersion { get; init; } = -1;
    public long Timestamp { get; init; }
}

public sealed record RollDiceCommand(int PlayerId) : GameCommand(PlayerId);

public sealed record EndTurnCommand(int PlayerId) : GameCommand(PlayerId);

public sealed record BuyPropertyCommand(int PlayerId) : GameCommand(PlayerId);

/// <summary>Declines the offered property, which starts an auction when auctions are enabled.</summary>
public sealed record DeclinePropertyCommand(int PlayerId) : GameCommand(PlayerId);

public sealed record PlaceBidCommand(int PlayerId, int Amount) : GameCommand(PlayerId);

public sealed record PassAuctionCommand(int PlayerId) : GameCommand(PlayerId);

/// <summary>Rapid auction bid; 0 means no bid.</summary>
public sealed record SubmitSealedBidCommand(int PlayerId, int Amount) : GameCommand(PlayerId);

public sealed record BuildHouseCommand(int PlayerId, int Tile, DevelopmentType DevType = DevelopmentType.Residential) : GameCommand(PlayerId);

public sealed record SellHouseCommand(int PlayerId, int Tile) : GameCommand(PlayerId);

public sealed record MortgageCommand(int PlayerId, int Tile) : GameCommand(PlayerId);

public sealed record UnmortgageCommand(int PlayerId, int Tile) : GameCommand(PlayerId);

public sealed record PayJailFineCommand(int PlayerId) : GameCommand(PlayerId);

public sealed record UseJailCardCommand(int PlayerId) : GameCommand(PlayerId);

public sealed record CreateTradeCommand(int PlayerId, int To, TradeSide Give, TradeSide Receive) : GameCommand(PlayerId);

/// <summary>The proposer replaces the contents of a pending offer.</summary>
public sealed record ModifyTradeCommand(int PlayerId, int TradeId, TradeSide Give, TradeSide Receive) : GameCommand(PlayerId);

/// <summary>The recipient answers with a new offer; Give is what the countering player hands over.</summary>
public sealed record CounterTradeCommand(int PlayerId, int TradeId, TradeSide Give, TradeSide Receive) : GameCommand(PlayerId);

public sealed record AcceptTradeCommand(int PlayerId, int TradeId) : GameCommand(PlayerId);

public sealed record RejectTradeCommand(int PlayerId, int TradeId) : GameCommand(PlayerId);

public sealed record CancelTradeCommand(int PlayerId, int TradeId) : GameCommand(PlayerId);

public sealed record ExerciseOptionCommand(int PlayerId, int ContractId) : GameCommand(PlayerId);

public sealed record UseAbilityCommand(int PlayerId) : GameCommand(PlayerId);

/// <summary>Team mode: hand cash to a teammate.</summary>
public sealed record TransferMoneyCommand(int PlayerId, int To, int Amount) : GameCommand(PlayerId);

public sealed record ContributeToProjectCommand(int PlayerId, int Amount) : GameCommand(PlayerId);

public sealed record DeclareBankruptcyCommand(int PlayerId) : GameCommand(PlayerId);

public sealed class CommandResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<GameEvent> Events { get; init; } = Array.Empty<GameEvent>();

    public static CommandResult Fail(string error) => new() { Ok = false, Error = error };

    public static CommandResult Success(IReadOnlyList<GameEvent> events) => new() { Ok = true, Events = events };
}
