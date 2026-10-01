using System.Collections.Concurrent;
using Game.Net.Transport;

namespace Game.Net;

/// <summary>
/// Glues a host transport to a <see cref="RoomHost"/>. Transport callbacks arrive on arbitrary
/// threads and are queued; <see cref="Pump"/> drains them on the owner's thread.
/// </summary>
public sealed class HostSession : IAsyncDisposable
{
    private enum Kind
    {
        Connected,
        Disconnected,
        Packet,
    }

    private readonly ConcurrentQueue<(Kind Kind, string Peer, byte[]? Data)> _queue = new();
    private readonly List<IGameHostTransport> _transports = new();

    public RoomHost Room { get; }

    public HostSession(RoomHostOptions options, params IGameHostTransport[] transports)
    {
        Room = new RoomHost(options, Send, Drop);
        foreach (var t in transports) Attach(t);
    }

    /// <summary>A room can listen on several transports at once (for example LAN and Nearby).</summary>
    public void Attach(IGameHostTransport transport)
    {
        int index = _transports.Count;
        _transports.Add(transport);
        string prefix = index + ":";
        transport.PeerConnected += p => _queue.Enqueue((Kind.Connected, prefix + p.PeerId, null));
        transport.PeerDisconnected += p => _queue.Enqueue((Kind.Disconnected, prefix + p.PeerId, null));
        transport.PacketReceived += (peer, data) => _queue.Enqueue((Kind.Packet, prefix + peer, data));
    }

    private (IGameHostTransport Transport, string Peer)? Resolve(string peerId)
    {
        int colon = peerId.IndexOf(':');
        if (colon <= 0 || !int.TryParse(peerId.AsSpan(0, colon), out int index) || index >= _transports.Count) return null;
        return (_transports[index], peerId[(colon + 1)..]);
    }

    private void Send(string peerId, byte[] packet)
    {
        if (Resolve(peerId) is { } target) _ = target.Transport.SendAsync(target.Peer, packet);
    }

    private void Drop(string peerId)
    {
        if (Resolve(peerId) is { } target) target.Transport.Disconnect(target.Peer);
    }

    public async Task StartAsync(CancellationToken cancel = default)
    {
        foreach (var t in _transports) await t.StartAsync(cancel);
    }

    public void Pump(double now)
    {
        while (_queue.TryDequeue(out var item))
        {
            switch (item.Kind)
            {
                case Kind.Connected:
                    Room.PeerConnected(item.Peer);
                    break;
                case Kind.Disconnected:
                    Room.PeerDisconnected(item.Peer);
                    break;
                case Kind.Packet:
                    Room.Receive(item.Peer, item.Data!);
                    break;
            }
        }
        Room.Tick(now);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var t in _transports) await t.DisposeAsync();
    }
}
