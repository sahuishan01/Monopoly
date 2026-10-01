using Game.Core.Board;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.Serialization;
using Game.Core.State;

namespace Game.Core.Replay;

/// <summary>Everything needed to reconstruct a match: the public initial state plus its event log.</summary>
public sealed class ReplayFile
{
    public int FormatVersion { get; set; } = 1;
    public BoardDefinition Board { get; set; } = new();
    public GameState Initial { get; set; } = new();
    public List<GameEvent> Events { get; set; } = new();

    public string ToJson() => CoreJson.Serialize(this);

    public static ReplayFile FromJson(string json)
    {
        var file = CoreJson.Deserialize<ReplayFile>(json);
        file.Initial.Board = file.Board;
        return file;
    }
}

/// <summary>Steps through a replay. Seeking backwards replays from the initial state.</summary>
public sealed class ReplayPlayer
{
    private readonly ReplayFile _file;

    public GameState State { get; private set; }
    public int Position { get; private set; }
    public int Length => _file.Events.Count;
    public bool AtEnd => Position >= _file.Events.Count;

    public ReplayPlayer(ReplayFile file)
    {
        _file = file;
        _file.Initial.Board = file.Board;
        State = file.Initial.Clone();
        State.Board = file.Board;
    }

    public GameEvent? Step()
    {
        if (AtEnd) return null;
        var e = _file.Events[Position++];
        e.Apply(State);
        State.Version = e.Version;
        return e;
    }

    public void Seek(int position)
    {
        position = Math.Clamp(position, 0, Length);
        if (position < Position)
        {
            State = _file.Initial.Clone();
            State.Board = _file.Board;
            Position = 0;
        }
        while (Position < position) Step();
    }

    /// <summary>Advances to just after the next turn start. Returns false at the end.</summary>
    public bool SkipTurn()
    {
        while (!AtEnd)
            if (Step() is TurnStarted) return true;
        return false;
    }

    public bool JumpToTurn(int turnNumber)
    {
        Seek(0);
        while (!AtEnd)
            if (Step() is TurnStarted t && t.TurnNumber >= turnNumber) return true;
        return false;
    }
}

public sealed record StoryEntry(int Turn, int Round, string Text, int Weight);

public sealed record Award(string Title, int Player, string Detail);

/// <summary>Turns an event log into the post-match timeline and awards.</summary>
public static class MatchStory
{
    public static List<StoryEntry> Build(ReplayFile file, int maxEntries = 14)
    {
        var player = new ReplayPlayer(file);
        var s = player.State;
        var entries = new List<StoryEntry>();
        string Name(int id) => id >= 0 ? s.Players[id].Name : "the bank";
        string Tile(int t) => s.Board.Tiles[t].Name;
        void Add(string text, int weight) => entries.Add(new StoryEntry(player.State.TurnNumber, player.State.Round, text, weight));

        bool firstPurchase = true;
        while (!player.AtEnd)
        {
            var e = player.Step()!;
            s = player.State;
            switch (e)
            {
                case PropertyPurchased x when firstPurchase:
                    firstPurchase = false;
                    Add($"{Name(x.Player)} buys {Tile(x.Tile)}", 3);
                    break;
                case DistrictCompleted x:
                    Add($"{Name(x.Player)} completes {s.Board.Districts[s.Board.DistrictIndex(x.District)].Name}", 8);
                    break;
                case EconomyChanged x:
                    Add($"The economy enters {x.Phase}", 5);
                    break;
                case CityEventOccurred x:
                    Add($"{x.Effect.Name}: {x.Effect.Description}", 4);
                    break;
                case ProjectCompleted x:
                    Add($"{x.Name} is completed", 6);
                    break;
                case TradeAccepted x:
                    Add($"{Name(x.Offer.From)} and {Name(x.Offer.To)} strike a deal", 5);
                    break;
                case AuctionCompleted x when x.Winner >= 0 && x.Bid >= s.Board.Tiles[x.Tile].Price * 3 / 2:
                    Add($"{Name(x.Winner)} wins a bidding war for {Tile(x.Tile)} at {x.Bid}", 5);
                    break;
                case RentPaid x when x.Amount >= 500:
                    Add($"{Name(x.Payer)} pays {x.Amount} rent to {Name(x.Payee)} at {Tile(x.Tile)}", 6);
                    break;
                case BuildingConstructed x when x.Level == 5:
                    Add($"{Name(x.Player)} raises a landmark on {Tile(x.Tile)}", 6);
                    break;
                case ObjectiveCompleted x:
                    Add($"{Name(x.Player)} completes a secret objective", 4);
                    break;
                case PlayerBankrupt x:
                    Add(x.Creditor >= 0 ? $"{Name(x.Creditor)} bankrupts {Name(x.Player)}" : $"{Name(x.Player)} goes bankrupt", 10);
                    break;
                case GameEnded x:
                    Add($"{string.Join(" & ", x.Winners.Select(Name))} win{(x.Winners.Length == 1 ? "s" : "")} the match", 12);
                    break;
            }
        }
        if (entries.Count > maxEntries)
        {
            var keep = entries.OrderByDescending(x => x.Weight).ThenBy(x => x.Turn).Take(maxEntries).ToHashSet();
            entries = entries.Where(keep.Contains).ToList();
        }
        return entries;
    }

    public static List<Award> Awards(GameState s)
    {
        var awards = new List<Award>();
        var players = s.Players;
        void Best(string title, Func<PlayerState, int> metric, Func<int, string> detail, bool lowest = false)
        {
            var ordered = lowest ? players.OrderBy(metric) : players.OrderByDescending(metric);
            var top = ordered.First();
            int value = metric(top);
            if (value != 0 || lowest) awards.Add(new Award(title, top.Id, detail(value)));
        }

        Best("Biggest Investor", p => p.Stats.MoneySpentOnProperty, v => $"{v} spent on property");
        Best("Best Negotiator", p => p.Stats.TradesCompleted, v => $"{v} trades completed");
        Best("Highest Rent Collected", p => p.Stats.RentEarned, v => $"{v} in rent");
        Best("Most Aggressive Bidder", p => p.Stats.AuctionsWon, v => $"{v} auctions won");
        Best("Master Builder", p => p.Stats.HousesBuilt, v => $"{v} buildings");
        // Luck is measured against the expected dice total of 7 per roll, in hundredths.
        int Luck(PlayerState p) => p.Stats.Rolls == 0 ? 0 : (p.Stats.DiceTotal * 100 / p.Stats.Rolls) - 700;
        if (players.Any(p => p.Stats.Rolls >= 5))
        {
            var rolled = players.Where(p => p.Stats.Rolls >= 5).ToList();
            var lucky = rolled.OrderByDescending(Luck).First();
            var unlucky = rolled.OrderBy(Luck).First();
            awards.Add(new Award("Luckiest Roller", lucky.Id, $"average roll {(700 + Luck(lucky)) / 100.0:0.00}"));
            if (unlucky.Id != lucky.Id)
                awards.Add(new Award("Most Unlucky Player", unlucky.Id, $"average roll {(700 + Luck(unlucky)) / 100.0:0.00}"));
        }
        var best = s.Properties.Where(p => p != null && p.RentCollected > 0).OrderByDescending(p => p!.RentCollected).FirstOrDefault();
        if (best != null && best.Owner >= 0)
            awards.Add(new Award("Most Profitable Property", best.Owner, $"{s.Board.Tiles[best.Tile].Name}: {best.RentCollected}"));
        return awards;
    }
}
