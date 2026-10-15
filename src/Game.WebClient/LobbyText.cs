using Game.Core.Rules;

namespace Game.WebClient;

/// <summary>Presentation text for the Home and Lobby screens: rule summary chips, ready status and input hints.</summary>
public static class LobbyText
{
    /// <summary>The server shortens display names to this many characters.</summary>
    public const int NameMax = 16;

    public sealed record Chip(string Label, string Value, string Kind = "rule");

    /// <summary>The rules that differ between presets or matter before a match, in reading order.</summary>
    public static IReadOnlyList<Chip> RuleChips(GameRules r, string currency)
    {
        var list = new List<Chip>
        {
            new("Mode", r.PresetName, "preset"),
            new("Start cash", currency + r.StartingMoney.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "money"),
            new("Salary", currency + r.Salary + (r.DoubleSalaryOnStart ? " ×2 on start" : ""), "money"),
            new("Auctions", !r.AuctionsEnabled ? "Off" : r.AuctionMode == AuctionMode.Rapid ? "Rapid" : "Classic"),
            new("Trading", r.TradingEnabled ? "On" : "Off"),
        };
        if (r.TurnTimeSeconds > 0) list.Add(new("Turn timer", r.TurnTimeSeconds + " s", "time"));
        if (r.MaximumRounds > 0) list.Add(new("Rounds", r.MaximumRounds.ToString(), "time"));
        if (r.EndOnFirstBankruptcy) list.Add(new("Ends", "First bankruptcy", "time"));
        if (r.StartingProperties > 0) list.Add(new("Deeds dealt", r.StartingProperties.ToString(), "money"));
        if (r.FreeParkingJackpot) list.Add(new("Jackpot", "On", "money"));
        foreach (var (on, name) in new[]
        {
            (r.MarketEventsEnabled, "Market events"), (r.SecretObjectivesEnabled, "Secret objectives"),
            (r.PropertySharesEnabled, "Shares"), (r.ContractsEnabled, "Contracts"), (r.AbilitiesEnabled, "Abilities"),
            (r.PublicProjectsEnabled, "Public projects"), (r.AdvancedDevelopment, "Advanced building"), (r.TeamsEnabled, "Teams"),
        })
            if (on) list.Add(new("Module", name, "module"));
        return list;
    }

    /// <summary>One line under the seat list telling the room what it is waiting for.</summary>
    public static string ReadyStatus(int seats, int ready, bool isHost)
    {
        if (seats < 2) return isHost ? "Add a bot or share the code — you need at least 2 players." : "Waiting for more players to join…";
        if (ready >= seats) return isHost ? "Everyone is ready. Start when you like." : "Everyone is ready. Waiting for the host to start.";
        int waiting = seats - ready;
        return $"Waiting for {waiting} player{(waiting == 1 ? "" : "s")} to get ready.";
    }

    /// <summary>Hint under the name field; Warn is true when the server will change what was typed.</summary>
    public static (string Text, bool Warn) NameHint(string? name)
    {
        string raw = name ?? "";
        string trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return (raw.Length > 0 ? "Only spaces — you'll play as Guest." : "Leave blank to play as Guest.", raw.Length > 0);
        if (trimmed.Any(char.IsControl)) return ("Hidden characters will be removed.", true);
        if (trimmed.Length > NameMax) return ($"Names are shortened to {NameMax} characters: “{trimmed[..NameMax]}”.", true);
        return ($"{trimmed.Length}/{NameMax}", false);
    }

    /// <summary>Normalised room code and a hint; Ok is false while the code cannot be a room code.</summary>
    public static (string Code, string? Hint, bool Ok) RoomCode(string? input)
    {
        string code = (input ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) return ("", null, false);
        if (!code.All(char.IsLetterOrDigit)) return (code, "Codes use letters and numbers only.", false);
        return (code, null, true);
    }
}
