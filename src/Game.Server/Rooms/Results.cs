using Game.Core.Rules;
using Game.Core.State;
using Game.Server.Data;

namespace Game.Server.Rooms;

public sealed record AchievementDef(string Id, string Title, string Description);

public static class Achievements
{
    public static readonly AchievementDef[] All =
    {
        new("first_win", "First Victory", "Win a match."),
        new("veteran", "Veteran", "Play 25 matches."),
        new("tycoon", "Tycoon", "Finish a match with a net worth of 5,000 or more."),
        new("landlord", "Landlord", "Collect 2,000 rent in a single match."),
        new("negotiator", "Deal Maker", "Complete three trades in a single match."),
        new("auctioneer", "Auction Shark", "Win three auctions in a single match."),
        new("architect", "Architect", "Construct ten buildings in a single match."),
        new("schemer", "Schemer", "Complete two secret objectives in a single match."),
        new("comeback", "Clean Hands", "Win without ever going to detention."),
        new("ranked_1200", "Rising Star", "Reach a rating of 1,200."),
    };

    public static IEnumerable<string> Earned(UserRecord user, PlayerState p, bool won, int netWorth)
    {
        if (won) yield return "first_win";
        if (user.Stats.GamesPlayed >= 25) yield return "veteran";
        if (netWorth >= 5000) yield return "tycoon";
        if (p.Stats.RentEarned >= 2000) yield return "landlord";
        if (p.Stats.TradesCompleted >= 3) yield return "negotiator";
        if (p.Stats.AuctionsWon >= 3) yield return "auctioneer";
        if (p.Stats.HousesBuilt >= 10) yield return "architect";
        if (p.Stats.ObjectivesCompleted >= 2) yield return "schemer";
        if (won && p.Stats.TimesJailed == 0) yield return "comeback";
        if (user.Rating >= 1200) yield return "ranked_1200";
    }
}

public static class Ratings
{
    public const int K = 32;

    /// <summary>
    /// Multiplayer Elo: every player is compared with every other player, a better finishing
    /// rank counting as a win. Returns the rating change per entry, in input order.
    /// </summary>
    public static int[] Changes(IReadOnlyList<(int Rating, int Rank)> players)
    {
        int n = players.Count;
        var result = new int[n];
        if (n < 2) return result;
        for (int i = 0; i < n; i++)
        {
            double expected = 0, actual = 0;
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                expected += 1.0 / (1.0 + Math.Pow(10, (players[j].Rating - players[i].Rating) / 400.0));
                actual += players[i].Rank < players[j].Rank ? 1.0 : players[i].Rank == players[j].Rank ? 0.5 : 0.0;
            }
            result[i] = (int)Math.Round(K * (actual - expected) / (n - 1) * 2);
        }
        return result;
    }

    public static void Accumulate(UserStats total, GameState s, PlayerState p, int rank, bool won)
    {
        total.GamesPlayed++;
        if (won) total.Wins++;
        total.RankTotal += rank;
        total.PropertiesBought += p.Stats.PropertiesBought;
        total.AuctionsWon += p.Stats.AuctionsWon;
        total.TradesProposed += p.Stats.TradesProposed;
        total.TradesCompleted += p.Stats.TradesCompleted;
        total.RentEarned += p.Stats.RentEarned;
        total.RentPaid += p.Stats.RentPaid;
        total.LargestPayment = Math.Max(total.LargestPayment, p.Stats.LargestPayment);
        total.BuildingsBuilt += p.Stats.HousesBuilt;
        total.ObjectivesCompleted += p.Stats.ObjectivesCompleted;
        if (p.Bankrupt) total.Bankruptcies++;
        foreach (var prop in s.Properties)
        {
            if (prop == null || prop.Owner != p.Id || prop.RentCollected <= total.MostProfitableRent) continue;
            total.MostProfitableRent = prop.RentCollected;
            total.MostProfitableProperty = s.Board.Tiles[prop.Tile].Name;
        }
    }
}
