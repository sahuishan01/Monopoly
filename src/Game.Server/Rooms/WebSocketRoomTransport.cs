using System.Collections.Concurrent;
using System.Net.WebSockets;
using Game.Net.Transport;
using Game.Protocol;

namespace Game.Server.Rooms;

/// <summary>Adapts accepted WebSockets to the host transport a room expects.</summary>
public sealed class WebSocketRoomTransport : IGameHostTransport
{
    private sealed class Connection
    {
        public WebSocket Socket = null!;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly CancellationTokenSource Cancel = new();
    }

    private readonly ConcurrentDictionary<string, Connection> _connections = new();
    private int _next;

    public event Action<string, byte[]>? PacketReceived;
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;

    public int Count => _connections.Count;

    public Task StartAsync(CancellationToken cancel = default) => Task.CompletedTask;

    /// <summary>Runs the socket until it closes; the HTTP request stays open for that long.</summary>
    public async Task RunAsync(WebSocket socket, string description, CancellationToken requestAborted)
    {
        string id = $"ws-{Interlocked.Increment(ref _next)}";
        var connection = new Connection { Socket = socket };
        _connections[id] = connection;
        PeerConnected?.Invoke(new PeerInfo(id, description));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, connection.Cancel.Token);
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, linked.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (message.Length > PacketCodec.MaxPacketBytes) break;
                if (!result.EndOfMessage) continue;
                PacketReceived?.Invoke(id, message.ToArray());
                message.SetLength(0);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException)
        {
        }

        if (_connections.TryRemove(id, out _)) PeerDisconnected?.Invoke(new PeerInfo(id));
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", timeout.Token);
            }
            catch (Exception)
            {
            }
        }
    }

    public async Task SendAsync(string peerId, byte[] packet)
    {
        if (!_connections.TryGetValue(peerId, out var c)) return;
        await c.Gate.WaitAsync();
        try
        {
            if (c.Socket.State == WebSocketState.Open)
                await c.Socket.SendAsync(packet, WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            c.Cancel.Cancel();
        }
        finally
        {
            c.Gate.Release();
        }
    }

    public void Disconnect(string peerId)
    {
        if (_connections.TryGetValue(peerId, out var c)) c.Cancel.CancelAfter(TimeSpan.FromMilliseconds(250));
    }

    public Task StopAsync()
    {
        foreach (var c in _connections.Values) c.Cancel.Cancel();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
