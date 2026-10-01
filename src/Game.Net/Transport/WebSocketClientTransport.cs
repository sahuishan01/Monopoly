using System.Net.WebSockets;

namespace Game.Net.Transport;

/// <summary>Connection to the dedicated match server over a binary WebSocket.</summary>
public sealed class WebSocketClientTransport : IGameTransport
{
    private readonly Func<CancellationToken, Task<WebSocket>> _connect;
    private readonly string _description;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WebSocket? _socket;
    private CancellationTokenSource? _cts;
    private int _connected;

    public bool IsConnected => _connected == 1;

    public event Action<byte[]>? PacketReceived;
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;

    public WebSocketClientTransport(Uri uri)
    {
        _description = uri.Host;
        _connect = async cancel =>
        {
            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            await socket.ConnectAsync(uri, cancel);
            return socket;
        };
    }

    /// <summary>Custom connector, for example an in-process test server.</summary>
    public WebSocketClientTransport(Func<CancellationToken, Task<WebSocket>> connect, string description = "server")
    {
        _connect = connect;
        _description = description;
    }

    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        _socket = await _connect(timeout.Token);
        _cts = new CancellationTokenSource();
        _connected = 1;
        PeerConnected?.Invoke(new PeerInfo("server", _description));
        _ = ReadLoop(_cts.Token);
    }

    private async Task ReadLoop(CancellationToken cancel)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (!cancel.IsCancellationRequested && _socket!.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, cancel);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                PacketReceived?.Invoke(message.ToArray());
                message.SetLength(0);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException)
        {
        }
        Drop();
    }

    private void Drop()
    {
        if (Interlocked.Exchange(ref _connected, 0) != 1) return;
        _socket?.Dispose();
        PeerDisconnected?.Invoke(new PeerInfo("server"));
    }

    public async Task SendAsync(byte[] packet)
    {
        if (!IsConnected || _socket == null) return;
        await _gate.WaitAsync();
        try
        {
            await _socket.SendAsync(packet, WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            Drop();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (_socket is { State: WebSocketState.Open })
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token);
            }
            catch (Exception)
            {
            }
        }
        Drop();
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
