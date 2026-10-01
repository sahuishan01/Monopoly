using System.Text.Json;
using Game.Core.State;
using Godot;

namespace BoardEmpire.Core;

/// <summary>Lifetime statistics for the player of this device; works without an account.</summary>
public sealed class LocalProfile
{
    private const string Path = "user://profile.json";

    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int RankTotal { get; set; }
    public int PropertiesBought { get; set; }
    public int AuctionsWon { get; set; }
    public int TradesProposed { get; set; }
    public int TradesCompleted { get; set; }
    public long RentEarned { get; set; }
    public long RentPaid { get; set; }
    public int LargestPayment { get; set; }
    public int Bankruptcies { get; set; }
    public int BuildingsBuilt { get; set; }
    public int ObjectivesCompleted { get; set; }
    public string MostProfitableProperty { get; set; } = "";
    public int MostProfitableRent { get; set; }
    public double SecondsPlayed { get; set; }
    public int TurnsTaken { get; set; }
    public List<string> Achievements { get; set; } = new();
    public HashSet<string> RecordedMatches { get; set; } = new();

    public double AveragePosition => GamesPlayed > 0 ? (double)RankTotal / GamesPlayed : 0;
    public double TradeSuccessRate => TradesProposed > 0 ? (double)TradesCompleted / TradesProposed : 0;
    public double SecondsPerTurn => TurnsTaken > 0 ? SecondsPlayed / TurnsTaken : 0;

    public static readonly (string Id, string Title, string Description)[] Catalog =
    {
        ("first_win", "First Victory", "Win a match."),
        ("veteran", "Veteran", "Play 25 matches."),
        ("tycoon", "Tycoon", "Finish with a net worth of 5,000 or more."),
        ("landlord", "Landlord", "Collect 2,000 rent in one match."),
        ("negotiator", "Deal Maker", "Complete three trades in one match."),
        ("auctioneer", "Auction Shark", "Win three auctions in one match."),
        ("architect", "Architect", "Construct ten buildings in one match."),
        ("schemer", "Schemer", "Complete two secret objectives in one match."),
        ("clean", "Clean Hands", "Win without visiting detention."),
    };

    public static LocalProfile Load()
    {
        try
        {
            if (FileAccess.FileExists(Path))
            {
                using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
                var loaded = JsonSerializer.Deserialize<LocalProfile>(file.GetAsText());
                if (loaded != null) return loaded;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("Profile could not be read: " + e.Message);
        }
        return new LocalProfile();
    }

    public void Save()
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(this));
    }

    /// <summary>Folds a finished match into the lifetime totals. Returns newly earned achievements.</summary>
    public List<string> Record(GameState s, int seat, double seconds)
    {
        var earned = new List<string>();
        if (!RecordedMatches.Add(s.MatchId)) return earned;
        var p = s.Players[seat];
        var standing = s.Standings.FirstOrDefault(x => x.Player == seat);
        bool won = s.Winners.Contains(seat);
        GamesPlayed++;
        if (won) Wins++;
        RankTotal += standing?.Rank ?? s.Players.Count;
        PropertiesBought += p.Stats.PropertiesBought;
        AuctionsWon += p.Stats.AuctionsWon;
        TradesProposed += p.Stats.TradesProposed;
        TradesCompleted += p.Stats.TradesCompleted;
        RentEarned += p.Stats.RentEarned;
        RentPaid += p.Stats.RentPaid;
        LargestPayment = Math.Max(LargestPayment, p.Stats.LargestPayment);
        BuildingsBuilt += p.Stats.HousesBuilt;
        ObjectivesCompleted += p.Stats.ObjectivesCompleted;
        if (p.Bankrupt) Bankruptcies++;
        SecondsPlayed += seconds;
        TurnsTaken += Math.Max(1, s.TurnNumber / Math.Max(1, s.Players.Count));
        foreach (var prop in s.Properties)
        {
            if (prop == null || prop.Owner != seat || prop.RentCollected <= MostProfitableRent) continue;
            MostProfitableRent = prop.RentCollected;
            MostProfitableProperty = s.Board.Tiles[prop.Tile].Name;
        }

        void Grant(string id, bool condition)
        {
            if (!condition || Achievements.Contains(id)) return;
            Achievements.Add(id);
            earned.Add(id);
        }

        Grant("first_win", won);
        Grant("veteran", GamesPlayed >= 25);
        Grant("tycoon", (standing?.NetWorth ?? 0) >= 5000);
        Grant("landlord", p.Stats.RentEarned >= 2000);
        Grant("negotiator", p.Stats.TradesCompleted >= 3);
        Grant("auctioneer", p.Stats.AuctionsWon >= 3);
        Grant("architect", p.Stats.HousesBuilt >= 10);
        Grant("schemer", p.Stats.ObjectivesCompleted >= 2);
        Grant("clean", won && p.Stats.TimesJailed == 0);
        if (RecordedMatches.Count > 200) RecordedMatches.Clear();
        Save();
        return earned;
    }
}
