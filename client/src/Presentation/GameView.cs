using BoardEmpire.Core;
using Game.Core.Board;
using Game.Core.Events;
using Game.Core.State;
using Godot;

namespace BoardEmpire.Presentation;

public sealed record AnimContext(float Speed, bool ReduceMotion, bool CameraShake)
{
    public float Time(float seconds) => seconds / Mathf.Max(0.1f, Speed);
}

/// <summary>
/// A way of looking at the match. Views are pure presentation: they are given the display
/// state and one event at a time and never decide anything about the game.
/// </summary>
public interface IGameView
{
    Control Node { get; }

    event Action<int>? TileTapped;

    void Build(BoardDefinition board, Settings settings);

    /// <summary>Snap every visual to the given state without animating.</summary>
    void Sync(GameState state);

    /// <summary>Animate one event. The state has already been advanced past it.</summary>
    Task Play(GameEvent e, GameState after, AnimContext context);

    /// <summary>Look at one tile, or the whole board when negative.</summary>
    void Focus(int tile);

    void ApplySettings(Settings settings);
}

/// <summary>Human-readable line for the event feed, subtitles and the replay viewer.</summary>
public static class EventText
{
    public static string? Describe(GameEvent e, GameState s)
    {
        string N(int id) => id >= 0 && id < s.Players.Count ? s.Players[id].Name : Loc.T("Bank");
        string T(int tile) => s.Board.Tiles[tile].Name;
        string M(int amount) => Ui.Money(s.Board.Currency, amount);
        switch (e)
        {
            case DiceRolled x: return $"{N(x.Player)} rolls {x.D1} + {x.D2}" + (x.D1 == x.D2 ? " — doubles!" : "");
            case SalaryPaid x: return $"{N(x.Player)} collects {M(x.Amount)} salary";
            case PropertyPurchased x: return $"{N(x.Player)} buys {T(x.Tile)} for {M(x.Price)}";
            case PropertyGranted x: return $"{N(x.Player)} starts with {T(x.Tile)}";
            case DistrictCompleted x: return $"{N(x.Player)} now controls {s.Board.Districts[s.Board.DistrictIndex(x.District)].Name}";
            case BuildingConstructed x: return x.Level == 5 ? $"{N(x.Player)} raises a landmark on {T(x.Tile)}" : $"{N(x.Player)} builds on {T(x.Tile)}";
            case BuildingSold x: return $"{N(x.Player)} sells a building on {T(x.Tile)}";
            case PropertyMortgaged x: return $"{N(x.Player)} mortgages {T(x.Tile)}";
            case PropertyUnmortgaged x: return $"{N(x.Player)} lifts the mortgage on {T(x.Tile)}";
            case RentPaid x: return $"{N(x.Payer)} pays {M(x.Amount)} rent to {N(x.Payee)}";
            case RentWaived x: return $"{N(x.Player)} pays no rent at {T(x.Tile)}";
            case MoneyTransferred x when x.Reason == MoneyReason.Jackpot: return $"{N(x.To)} wins the {M(x.Amount)} jackpot";
            case MoneyTransferred x when x.Reason == MoneyReason.Gift: return $"{N(x.From)} sends {M(x.Amount)} to {N(x.To)}";
            case MoneyTransferred x when x.From >= 0: return $"{N(x.From)} pays {M(x.Amount)} to {N(x.To)}";
            case MoneyTransferred x: return $"{N(x.To)} receives {M(x.Amount)}";
            case DebtIncurred x: return $"{N(x.Debt.Debtor)} owes {M(x.Debt.Amount)} to {N(x.Debt.Creditor)}";
            case DebtPaid x: return $"{N(x.Debt.Debtor)} settles {M(x.Debt.Amount)} with {N(x.Debt.Creditor)}";
            case CardDrawn x:
                var card = (x.Deck == TileType.Fortune ? s.Board.FortuneCards : s.Board.CivicCards)[x.CardIndex];
                return $"{N(x.Player)}: {card.Text}";
            case PlayerSentToJail x: return $"{N(x.Player)} is sent to {T(x.JailTile)}";
            case PlayerReleasedFromJail x: return $"{N(x.Player)} is released";
            case JailTurnFailed x: return $"{N(x.Player)} stays in detention";
            case AuctionStarted x: return $"{T(x.Tile)} goes to auction";
            case BidPlaced x: return $"{N(x.Player)} bids {M(x.Amount)}";
            case AuctionPassed x: return $"{N(x.Player)} passes";
            case SealedBidPlaced x: return $"{N(x.Player)} submits a sealed bid";
            case AuctionCompleted x: return x.Winner >= 0 ? $"{N(x.Winner)} wins {T(x.Tile)} for {M(x.Paid)}" : $"Nobody bids on {T(x.Tile)}";
            case TradeProposed x: return $"{N(x.Offer.From)} offers {N(x.Offer.To)} a deal";
            case TradeCountered x: return $"{N(x.Offer.From)} makes a counter-offer";
            case TradeAccepted x: return $"{N(x.Offer.From)} and {N(x.Offer.To)} strike a deal";
            case TradeRejected x: return $"{N(x.By)} turns the deal down";
            case OptionExercised x: return $"{N(x.Buyer)} exercises an option on {T(x.Tile)}";
            case AbilityUsed x: return $"{N(x.Player)} uses the {x.Ability} ability";
            case ObjectiveCompleted x: return $"{N(x.Player)} completes a secret objective (+{M(x.Reward)})";
            case EconomyChanged x: return x.Effect?.Description ?? "The economy is stable again";
            case CityEventOccurred x: return $"{x.Effect.Name}: {x.Effect.Description}";
            case ProjectProposed x: return $"Public project proposed: {x.Project.Name} ({M(x.Project.Cost)})";
            case ProjectContribution x: return $"{N(x.Player)} contributes {M(x.Amount)} to the project";
            case ProjectCompleted x: return $"{x.Name} is completed";
            case ProjectFailed x: return $"{x.Name} is cancelled; contributions are refunded";
            case PlayerBankrupt x: return x.Creditor >= 0 ? $"{N(x.Player)} is bankrupted by {N(x.Creditor)}" : $"{N(x.Player)} goes bankrupt";
            case PlayerControlChanged x: return x.IsBot ? $"A bot takes over for {N(x.Player)}" : $"{N(x.Player)} is back";
            case RoundStarted x: return $"Round {x.Round}";
            case GameEnded x: return $"{string.Join(" & ", x.Winners.Select(N))} win{(x.Winners.Length == 1 ? "s" : "")} the match";
            default: return null;
        }
    }
}
