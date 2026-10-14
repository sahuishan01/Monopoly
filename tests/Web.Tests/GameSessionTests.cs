using Game.Core.Rules;
using Game.Net;
using Game.Net.Transport;
using Game.Protocol;
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class GameSessionTests
{
    private sealed class Rig
    {
        public LoopbackHub Hub = new();
        public HostSession Host;
        public double Now;

        public Rig()
        {
            var o = new RoomHostOptions { RoomCode = "TEST", BotDelaySeconds = 0, FixedSeed = 5, RapidAuctionSeconds = 0 };
            Host = new HostSession(o, Hub);
            Host.StartAsync().GetAwaiter().GetResult();
        }

        public void Pump(GameSession s, double seconds)
        {
            double end = Now + seconds;
            while (Now < end)
            {
                Now += 0.05;
                Host.Pump(Now);
                s.Tick(Now);
            }
        }
    }

    [Fact]
    public async Task Connecting_reaches_the_lobby_and_raises_Changed()
    {
        var rig = new Rig();
        var s = new GameSession(rig.Hub.CreateClient(), "Ann");
        int changed = 0;
        s.Changed += () => changed++;
        await s.ConnectAsync();
        rig.Pump(s, 1);
        Assert.NotNull(s.Client.Lobby);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task Display_follows_events_through_the_pacer_and_reaches_the_replica_state()
    {
        var rig = new Rig();
        var s = new GameSession(rig.Hub.CreateClient(), "Ann");
        await s.ConnectAsync();
        rig.Pump(s, 0.5);
        s.Client.AddBot(BotLevel.Easy);
        s.Client.SetReady(true);
        rig.Pump(s, 0.5);
        s.Client.StartMatch();
        rig.Pump(s, 30);
        Assert.NotNull(s.Display);
        Assert.Equal(s.Client.State!.Version, s.Display!.Version);
    }

    [Fact]
    public async Task Resume_token_reclaims_the_same_seat_after_a_reconnect()
    {
        var rig = new Rig();
        var a = new GameSession(rig.Hub.CreateClient(), "Ann");
        await a.ConnectAsync();
        rig.Pump(a, 0.5);
        var seats = a.Client.Seats;
        string token = a.Client.ResumeToken;
        Assert.NotEmpty(token);
        await a.DisconnectAsync();
        rig.Pump(a, 0.5);

        var b = new GameSession(rig.Hub.CreateClient(), "Ann", resumeToken: token);
        await b.ConnectAsync();
        rig.Pump(b, 1);
        Assert.Equal(seats, b.Client.Seats);
    }

    [Fact]
    public void Update_required_is_null_until_a_version_reject_arrives()
    {
        var rig = new Rig();
        var s = new GameSession(rig.Hub.CreateClient(), "Ann");
        Assert.Null(s.UpdateRequired);
    }
}
