using Game.Core.Rules;
using Game.Core.State;
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
        var seats = a.Client.Seats.ToArray();
        string token = a.Client.ResumeToken;
        Assert.NotEmpty(token);
        await a.DisconnectAsync();
        rig.Pump(a, 0.5);

        var b = new GameSession(rig.Hub.CreateClient(), "Ann", resumeToken: token);
        await b.ConnectAsync();
        rig.Pump(b, 1);
        Assert.Equal(seats, b.Client.Seats.ToArray());
        Assert.NotEmpty(b.Client.Seats);
    }

    [Fact]
    public async Task Resume_token_beats_a_tokenless_joiner_to_the_original_seat()
    {
        var rig = new Rig();
        var a = new GameSession(rig.Hub.CreateClient(), "Ann");
        await a.ConnectAsync();
        rig.Pump(a, 0.5);
        var c = new GameSession(rig.Hub.CreateClient(), "Cy");
        await c.ConnectAsync();
        rig.Pump(a, 0.3);
        rig.Pump(c, 0.3);
        a.Client.SetReady(true);
        c.Client.SetReady(true);
        rig.Pump(a, 0.3);
        rig.Pump(c, 0.3);
        a.Client.StartMatch();
        rig.Pump(a, 0.5);
        rig.Pump(c, 0.5);
        var seats = a.Client.Seats.ToArray();
        string token = a.Client.ResumeToken;
        Assert.NotEmpty(seats);
        await a.DisconnectAsync();
        rig.Pump(c, 0.5);

        var d = new GameSession(rig.Hub.CreateClient(), "Dee");
        await d.ConnectAsync();
        rig.Pump(d, 0.5);
        Assert.NotEqual(seats, d.Client.Seats.ToArray());

        var b = new GameSession(rig.Hub.CreateClient(), "Ann", resumeToken: token);
        await b.ConnectAsync();
        rig.Pump(b, 1);
        Assert.Equal(seats, b.Client.Seats.ToArray());
    }

    private sealed class FakeTransport : IGameTransport
    {
        public bool IsConnected { get; private set; }
        public event Action<byte[]>? PacketReceived;
        public event Action<PeerInfo>? PeerConnected;
        public event Action<PeerInfo>? PeerDisconnected;
        public Task ConnectAsync(CancellationToken cancel = default) { IsConnected = true; return Task.CompletedTask; }
        public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
        public Task SendAsync(byte[] packet) => Task.CompletedTask;
        public void Inject(NetMessage m) => PacketReceived?.Invoke(PacketCodec.Encode(m, "", 1));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(RejectCode.VersionTooOld, 0)]
    [InlineData(RejectCode.VersionTooNew, 0)]
    [InlineData(RejectCode.RoomFull, 9999)]
    public async Task Version_reject_sets_UpdateRequired_and_raises_Changed(RejectCode code, int min)
    {
        var t = new FakeTransport();
        var s = new GameSession(t, "Ann");
        int changed = 0;
        s.Changed += () => changed++;
        await s.ConnectAsync();
        t.Inject(new Reject(code, "please update", min));
        s.Tick(0.1);
        Assert.Equal("please update", s.UpdateRequired);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task Non_version_reject_leaves_UpdateRequired_null()
    {
        var t = new FakeTransport();
        var s = new GameSession(t, "Ann");
        await s.ConnectAsync();
        t.Inject(new Reject(RejectCode.RoomFull, "full", 0));
        s.Tick(0.1);
        Assert.Null(s.UpdateRequired);
    }

    [Fact]
    public async Task Snapshot_while_events_are_queued_replaces_display_and_drops_the_queue()
    {
        var rig = new Rig();
        var src = new GameSession(rig.Hub.CreateClient(), "Ann");
        await src.ConnectAsync();
        rig.Pump(src, 0.5);
        src.Client.AddBot(BotLevel.Easy);
        src.Client.SetReady(true);
        rig.Pump(src, 0.5);
        src.Client.StartMatch();
        rig.Pump(src, 1);
        src.Client.SendCommand(new Game.Core.Commands.RollDiceCommand(src.Client.Seats[0]));
        rig.Pump(src, 30);
        var log = src.Client.EventLog.ToList();
        Assert.True(log.Count >= 2);
        int n = Math.Min(log.Count, 4);
        var initial = src.Client.InitialState!.Clone();
        var final = src.Client.State!.Clone();
        Assert.True(final.Version > initial.Version);

        var t = new FakeTransport();
        var s = new GameSession(t, "Zed");
        await s.ConnectAsync();
        t.Inject(new Snapshot(initial, src.Client.Board, "", new int[0]));
        s.Tick(1.0);
        Assert.Equal(initial.Version, s.Display!.Version);
        t.Inject(new EventBatch(initial.Version + 1, log.Take(n).ToList(), "", ""));
        // Batch and snapshot arrive in the same poll: events are queued in the pacer when the snapshot lands.
        t.Inject(new Snapshot(final, src.Client.Board, "", new int[0]));
        s.Tick(1.1);
        Assert.Equal(final.Version, s.Display!.Version);
        for (double now = 2; now < 40; now += 0.1) s.Tick(now);
        Assert.Equal(final.Version, s.Display!.Version);
        Assert.Equal(final.Version, s.Client.State!.Version);
    }

    [Fact]
    public void Update_required_is_null_until_a_version_reject_arrives()
    {
        var rig = new Rig();
        var s = new GameSession(rig.Hub.CreateClient(), "Ann");
        Assert.Null(s.UpdateRequired);
    }
}
