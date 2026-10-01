using System.Collections.Concurrent;

namespace Game.Net.Transport;

public sealed record PeerInfo(string PeerId, string Description = "");

/// <summary>
/// Client side of a connection to an authority. Implementations exist for in-process play,
/// LAN, Nearby Connections and the internet server; game code never knows which one is active.
/// Events may be raised on any thread.
/// </summary>
public interface IGameTransport : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancel = default);

    Task DisconnectAsync();

    Task SendAsync(byte[] packet);

    event Action<byte[]> PacketReceived;
    event Action<PeerInfo> PeerConnected;
    event Action<PeerInfo> PeerDisconnected;
}

/// <summary>Authority side: accepts peers and exchanges packets with each of them.</summary>
public interface IGameHostTransport : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancel = default);

    Task StopAsync();

    Task SendAsync(string peerId, byte[] packet);

    void Disconnect(string peerId);

    event Action<string, byte[]> PacketReceived;
    event Action<PeerInfo> PeerConnected;
    event Action<PeerInfo> PeerDisconnected;
}

/// <summary>
/// In-memory transport. Used for solo and pass-and-play matches (the "offline transport") and
/// for tests; packets are handed over synchronously and in order.
/// </summary>
public sealed class LoopbackHub : IGameHostTransport
{
    private readonly ConcurrentDictionary<string, LoopbackClient> _clients = new();
    private int _next;

    public event Action<string, byte[]>? PacketReceived;
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;

    public bool Running { get; private set; }

    public Task StartAsync(CancellationToken cancel = default)
    {
        Running = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        Running = false;
        foreach (var c in _clients.Values.ToList()) c.DropFromHost();
        _clients.Clear();
        return Task.CompletedTask;
    }

    public IGameTransport CreateClient(string? peerId = null) =>
        new LoopbackClient(this, peerId ?? $"local-{Interlocked.Increment(ref _next)}");

    public Task SendAsync(string peerId, byte[] packet)
    {
        if (_clients.TryGetValue(peerId, out var c)) c.Deliver(packet);
        return Task.CompletedTask;
    }

    public void Disconnect(string peerId)
    {
        if (!_clients.TryRemove(peerId, out var c)) return;
        c.DropFromHost();
        PeerDisconnected?.Invoke(new PeerInfo(peerId));
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private sealed class LoopbackClient : IGameTransport
    {
        private readonly LoopbackHub _hub;
        private readonly string _id;

        public LoopbackClient(LoopbackHub hub, string id)
        {
            _hub = hub;
            _id = id;
        }

        public bool IsConnected { get; private set; }

        public event Action<byte[]>? PacketReceived;
        public event Action<PeerInfo>? PeerConnected;
        public event Action<PeerInfo>? PeerDisconnected;

        public Task ConnectAsync(CancellationToken cancel = default)
        {
            if (!_hub.Running) throw new InvalidOperationException("Host is not running");
            _hub._clients[_id] = this;
            IsConnected = true;
            _hub.PeerConnected?.Invoke(new PeerInfo(_id, "loopback"));
            PeerConnected?.Invoke(new PeerInfo("host", "loopback"));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            if (!IsConnected) return Task.CompletedTask;
            IsConnected = false;
            if (_hub._clients.TryRemove(_id, out _)) _hub.PeerDisconnected?.Invoke(new PeerInfo(_id));
            return Task.CompletedTask;
        }

        public Task SendAsync(byte[] packet)
        {
            if (IsConnected) _hub.PacketReceived?.Invoke(_id, packet);
            return Task.CompletedTask;
        }

        public void Deliver(byte[] packet)
        {
            if (IsConnected) PacketReceived?.Invoke(packet);
        }

        public void DropFromHost()
        {
            if (!IsConnected) return;
            IsConnected = false;
            PeerDisconnected?.Invoke(new PeerInfo("host"));
        }

        public ValueTask DisposeAsync() => new(DisconnectAsync());
    }
}

/// <summary>
/// Wraps a client transport and misbehaves on purpose: latency, loss, duplication and
/// reordering in both directions. Call <see cref="Advance"/> to move its clock.
/// </summary>
public sealed class ChaosTransport : IGameTransport
{
    private readonly IGameTransport _inner;
    private readonly Random _random;
    private readonly List<(double At, bool Outgoing, byte[] Packet)> _queue = new();
    private readonly object _gate = new();
    private double _now;

    public double LatencySeconds { get; set; }
    public double JitterSeconds { get; set; }
    public double LossRate { get; set; }
    public double DuplicateRate { get; set; }

    public ChaosTransport(IGameTransport inner, int seed)
    {
        _inner = inner;
        _random = new Random(seed);
        _inner.PacketReceived += p => Schedule(false, p);
        _inner.PeerConnected += p => PeerConnected?.Invoke(p);
        _inner.PeerDisconnected += p => PeerDisconnected?.Invoke(p);
    }

    public bool IsConnected => _inner.IsConnected;

    public event Action<byte[]>? PacketReceived;
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;

    public Task ConnectAsync(CancellationToken cancel = default) => _inner.ConnectAsync(cancel);

    public Task DisconnectAsync() => _inner.DisconnectAsync();

    public Task SendAsync(byte[] packet)
    {
        Schedule(true, packet);
        return Task.CompletedTask;
    }

    private void Schedule(bool outgoing, byte[] packet)
    {
        lock (_gate)
        {
            if (_random.NextDouble() < LossRate) return;
            int copies = _random.NextDouble() < DuplicateRate ? 2 : 1;
            for (int i = 0; i < copies; i++)
                _queue.Add((_now + LatencySeconds + _random.NextDouble() * JitterSeconds, outgoing, packet));
        }
    }

    /// <summary>Delivers everything that is due at the given time.</summary>
    public void Advance(double now)
    {
        List<(double At, bool Outgoing, byte[] Packet)> due;
        lock (_gate)
        {
            _now = now;
            due = _queue.Where(x => x.At <= now).OrderBy(x => x.At).ToList();
            _queue.RemoveAll(x => x.At <= now);
        }
        foreach (var (_, outgoing, packet) in due)
        {
            if (outgoing) _inner.SendAsync(packet);
            else PacketReceived?.Invoke(packet);
        }
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
