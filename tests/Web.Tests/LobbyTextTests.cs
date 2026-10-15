using Game.Core.Rules;
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class LobbyTextTests
{
    [Fact]
    public void Classic_rules_show_core_chips_and_no_modules()
    {
        var chips = LobbyText.RuleChips(RulePresets.Get("classic"), "$");
        Assert.Equal(["Mode", "Start cash", "Salary", "Auctions", "Trading"], chips.Select(c => c.Label));
        Assert.Equal("$1,500", chips[1].Value);
        Assert.Equal("Classic", chips[3].Value);
        Assert.DoesNotContain(chips, c => c.Kind == "module");
    }

    [Fact]
    public void Blitz_shows_timer_rounds_deeds_and_objectives()
    {
        var chips = LobbyText.RuleChips(RulePresets.Get("blitz"), "₵");
        Assert.Contains(chips, c => c.Label == "Turn timer" && c.Value == "30 s");
        Assert.Contains(chips, c => c.Label == "Rounds" && c.Value == "12");
        Assert.Contains(chips, c => c.Label == "Deeds dealt" && c.Value == "3");
        Assert.Contains(chips, c => c.Label == "Auctions" && c.Value == "Rapid");
        Assert.Contains(chips, c => c.Kind == "module" && c.Value == "Secret objectives");
        Assert.Equal("₵1,000", chips.Single(c => c.Label == "Start cash").Value);
    }

    [Fact]
    public void Disabled_auctions_and_trading_say_off()
    {
        var chips = LobbyText.RuleChips(new GameRules { AuctionsEnabled = false, TradingEnabled = false }, "$");
        Assert.Equal("Off", chips.Single(c => c.Label == "Auctions").Value);
        Assert.Equal("Off", chips.Single(c => c.Label == "Trading").Value);
    }

    [Theory]
    [InlineData(1, 1, true, "Add a bot")]
    [InlineData(1, 0, false, "Waiting for more players")]
    [InlineData(3, 1, true, "Waiting for 2 players")]
    [InlineData(3, 2, false, "Waiting for 1 player to")]
    [InlineData(3, 3, true, "Start when you like")]
    [InlineData(3, 3, false, "Waiting for the host")]
    public void Ready_status(int seats, int ready, bool host, string expected) =>
        Assert.Contains(expected, LobbyText.ReadyStatus(seats, ready, host));

    [Theory]
    [InlineData(null, "Leave blank", false)]
    [InlineData("", "Leave blank", false)]
    [InlineData("   ", "Only spaces", true)]
    [InlineData("Ishan", "5/16", false)]
    [InlineData("  Ishan  ", "5/16", false)]
    [InlineData("ABCDEFGHIJKLMNOPQRS", "shortened to 16", true)]
    public void Name_hint(string? name, string expected, bool warn)
    {
        var (text, w) = LobbyText.NameHint(name);
        Assert.Contains(expected, text);
        Assert.Equal(warn, w);
    }

    [Theory]
    [InlineData(" ab12cd ", "AB12CD", true)]
    [InlineData("", "", false)]
    [InlineData("AB-12", "AB-12", false)]
    public void Room_code(string input, string code, bool ok)
    {
        var r = LobbyText.RoomCode(input);
        Assert.Equal(code, r.Code);
        Assert.Equal(ok, r.Ok);
    }
}
