using Game.Core.AI;
using Game.Core.Commands;
using Game.Core.Engine;
using Game.Core.Rng;
using Game.Core.Rules;
using Game.Core.Serialization;
using Game.Net;
using Game.Net.Transport;
using Game.Protocol;

namespace Net.Tests;

/// <summary>Plays a client's seats with a bot brain, like an attentive human would.</summary>
public sealed class Autopilot
{
    private readonly RoomClient _client;
    private readonly BotBrain _brain;
    private bool _awaiting;
    private double _sentAt;

    public bool Enabled { get; set; } = true;
    public int Sent { get; private set; }
    public List<string> Failures { get; } = new();

    public Autopilot(RoomClient client, ulong seed, BotLevel level = BotLevel.Medium)
    {
        _client = client;
        _brain = new BotBrain(level, new Pcg32(seed)) { Rollouts = 0 };
        client.EventsApplied += _ => _awaiting = false;
        client.SnapshotLoaded += _ => _awaiting = false;
        client.CommandFailed += (_, reason) =>
        {
            _awaiting = false;
            Failures.Add(reason);
        };
    }

    public void Step(double now)
    {
        var s = _client.State;
        if (!Enabled || s == null || s.IsOver || !_client.IsLinkUp) return;
        if (_awaiting && now - _sentAt < 20) return;
        foreach (int seat in TurnInfo.PendingActors(s))
        {
            if (!_client.Controls(seat)) continue;
            var command = _brain.Decide(s, seat) ?? (s.Trades.Any(t => t.From == seat) ? null : BotBrain.TimeoutAction(s, seat));
            if (command == null) continue;
            _client.SendCommand(command);
            _awaiting = true;
            _sentAt = now;
            Sent++;
            return;
        }
    }
}

public sealed class Harness : IAsyncDisposable
{
    public LoopbackHub Hub { get; private set; } = new();
    public HostSession Host { get; private set; }
    public List<RoomClient> Clients { get; } = new();
    public List<Autopilot> Pilots { get; } = new();
    public List<ChaosTransport> Chaos { get; } = new();
    public double Now { get; private set; }
    public RoomHostOptions Options { get; }

    public Harness(Action<RoomHostOptions>? configure = null)
    {
        Options = new RoomHostOptions { RoomCode = "TEST", BotDelaySeconds = 0, FixedSeed = 99, RapidAuctionSeconds = 0, TradeAnswerSeconds = 30 };
        configure?.Invoke(Options);
        Host = new HostSession(Options, Hub);
        Host.StartAsync().GetAwaiter().GetResult();
    }

    public RoomClient Join(string name, PeerRole role = PeerRole.Player, Action<ChaosTransport>? chaos = null, bool autopilot = true)
    {
        var transport = Hub.CreateClient();
        if (chaos != null)
        {
            var wrapped = new ChaosTransport(transport, Clients.Count + 7);
            chaos(wrapped);
            Chaos.Add(wrapped);
            transport = wrapped;
        }
        var client = new RoomClient(transport, name) { Role = role, ResendSeconds = 1.5 };
        client.ConnectAsync().GetAwaiter().GetResult();
        Clients.Add(client);
        if (autopilot && role == PeerRole.Player) Pilots.Add(new Autopilot(client, (ulong)Clients.Count * 31));
        Pump(0.3);
        return client;
    }

    public void Pump(double seconds = 0.1, double step = 0.05)
    {
        double end = Now + seconds;
        while (Now < end)
        {
            Now += step;
            foreach (var c in Chaos) c.Advance(Now);
            Host.Pump(Now);
            foreach (var c in Chaos) c.Advance(Now);
            foreach (var c in Clients) c.Poll(Now);
            foreach (var p in Pilots) p.Step(Now);
        }
    }

    public bool PumpUntil(Func<bool> done, double maxSeconds = 600, double step = 0.05)
    {
        double end = Now + maxSeconds;
        while (Now < end)
        {
            if (done()) return true;
            Pump(step, step);
        }
        return done();
    }

    public void StartMatch()
    {
        foreach (var c in Clients.Where(c => c.Role == PeerRole.Player)) c.SetReady(true);
        Pump(0.2);
        Clients[0].StartMatch();
        Pump(0.3);
    }

    public bool PlayToEnd(double maxSeconds = 3000) =>
        PumpUntil(() => Host.Room.Engine != null && Host.Room.Engine.State.IsOver, maxSeconds);

    public string HostHash => StateHasher.Hash(Host.Room.Engine!.State);

    /// <summary>Replaces the authority, as happens when a server restarts or a new host takes over.</summary>
    public void ReplaceHost(Action<RoomHost> prepare)
    {
        Hub.StopAsync().GetAwaiter().GetResult();
        Hub = new LoopbackHub();
        Host = new HostSession(Options, Hub);
        Host.StartAsync().GetAwaiter().GetResult();
        prepare(Host.Room);
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}
