using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Game.Net.Transport;

/// <summary>What a host tells devices that are looking for a game on the same network.</summary>
public sealed class LanAdvertisement
{
    public string RoomCode { get; set; } = "";
    public string RoomName { get; set; } = "";
    public string HostName { get; set; } = "";
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public int Port { get; set; }
    public bool InMatch { get; set; }
    public int ProtocolVersion { get; set; }
    public string Preset { get; set; } = "";
    public string Board { get; set; } = "";
    /// <summary>Filled in by the receiver from the datagram's source address.</summary>
    public string Address { get; set; } = "";
}

internal static class LanFraming
{
    public const int MaxFrame = 4 * 1024 * 1024;

    public static async Task WriteAsync(NetworkStream stream, byte[] packet, SemaphoreSlim gate)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, packet.Length);
        await gate.WaitAsync();
        try
        {
            await stream.WriteAsync(header);
            await stream.WriteAsync(packet);
        }
        finally
        {
            gate.Release();
        }
    }

    public static async Task<byte[]?> ReadAsync(NetworkStream stream, CancellationToken cancel)
    {
        var header = new byte[4];
        if (!await Fill(stream, header, cancel)) return null;
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > MaxFrame) return null;
        var packet = new byte[length];
        return await Fill(stream, packet, cancel) ? packet : null;
    }

    private static async Task<bool> Fill(NetworkStream stream, byte[] buffer, CancellationToken cancel)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), cancel);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}

/// <summary>
/// Same-network host: length-prefixed TCP for the game and a UDP responder so that clients can
/// find the room without typing an address. Needs no internet connection.
/// </summary>
public sealed class LanHostTransport : IGameHostTransport
{
    public const int DefaultPort = 7777;
    public const int DiscoveryPort = 47777;
    public const string Query = "BEMP?1";
    public const string Answer = "BEMP!1";

    private sealed class Connection
    {
        public TcpClient Client = null!;
        public NetworkStream Stream = null!;
        public readonly SemaphoreSlim Gate = new(1, 1);
    }

    private readonly ConcurrentDictionary<string, Connection> _connections = new();
    private readonly Func<LanAdvertisement>? _describe;
    private readonly int _requestedPort;
    private readonly bool _discoverable;
    private CancellationTokenSource? _cts;
    private TcpListener? _listener;
    private UdpClient? _udp;
    private int _nextPeer;

    public int Port { get; private set; }

    public event Action<string, byte[]>? PacketReceived;
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;

    public LanHostTransport(int port = DefaultPort, Func<LanAdvertisement>? describe = null, bool discoverable = true)
    {
        _requestedPort = port;
        _describe = describe;
        _discoverable = discoverable;
    }

    public Task StartAsync(CancellationToken cancel = default)
    {
        _cts = new CancellationTokenSource();
        try
        {
            _listener = new TcpListener(IPAddress.Any, _requestedPort);
            _listener.Start();
        }
        catch (SocketException)
        {
            // Port taken (another room on this device): let the OS pick one; discovery reports it.
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
        }
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoop(_cts.Token);

        if (_discoverable && _describe != null)
        {
            try
            {
                _udp = new UdpClient { ExclusiveAddressUse = false, EnableBroadcast = true };
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _ = DiscoveryLoop(_cts.Token);
            }
            catch (SocketException)
            {
                _udp = null;
            }
        }
        return Task.CompletedTask;
    }

    private async Task AcceptLoop(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancel);
            }
            catch (Exception) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            client.NoDelay = true;
            string id = $"lan-{Interlocked.Increment(ref _nextPeer)}";
            var connection = new Connection { Client = client, Stream = client.GetStream() };
            _connections[id] = connection;
            PeerConnected?.Invoke(new PeerInfo(id, client.Client.RemoteEndPoint?.ToString() ?? ""));
            _ = ReadLoop(id, connection, cancel);
        }
    }

    private async Task ReadLoop(string id, Connection connection, CancellationToken cancel)
    {
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                var packet = await LanFraming.ReadAsync(connection.Stream, cancel);
                if (packet == null) break;
                PacketReceived?.Invoke(id, packet);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
        if (_connections.TryRemove(id, out _))
        {
            connection.Client.Dispose();
            PeerDisconnected?.Invoke(new PeerInfo(id));
        }
    }

    private async Task DiscoveryLoop(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                var request = await _udp!.ReceiveAsync(cancel);
                if (Encoding.UTF8.GetString(request.Buffer) != Query) continue;
                var ad = _describe!();
                ad.Port = Port;
                byte[] reply = Encoding.UTF8.GetBytes(Answer + JsonSerializer.Serialize(ad));
                await _udp.SendAsync(reply, reply.Length, request.RemoteEndPoint);
            }
            catch (Exception) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public async Task SendAsync(string peerId, byte[] packet)
    {
        if (!_connections.TryGetValue(peerId, out var c)) return;
        try
        {
            await LanFraming.WriteAsync(c.Stream, packet, c.Gate);
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            Disconnect(peerId);
        }
    }

    public void Disconnect(string peerId)
    {
        if (!_connections.TryRemove(peerId, out var c)) return;
        c.Client.Dispose();
        PeerDisconnected?.Invoke(new PeerInfo(peerId));
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _udp?.Dispose();
        _udp = null;
        foreach (string id in _connections.Keys.ToList())
            if (_connections.TryRemove(id, out var c)) c.Client.Dispose();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}

public sealed class LanClientTransport : IGameTransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private int _connected;

    public bool IsConnected => _connected == 1;

    public event Action<byte[]>? PacketReceived;
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;

    public LanClientTransport(string host, int port = LanHostTransport.DefaultPort)
    {
        _host = host;
        _port = port;
    }

    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        _client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        await _client.ConnectAsync(_host, _port, timeout.Token);
        _stream = _client.GetStream();
        _cts = new CancellationTokenSource();
        _connected = 1;
        PeerConnected?.Invoke(new PeerInfo("host", $"{_host}:{_port}"));
        _ = ReadLoop(_cts.Token);
    }

    private async Task ReadLoop(CancellationToken cancel)
    {
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                var packet = await LanFraming.ReadAsync(_stream!, cancel);
                if (packet == null) break;
                PacketReceived?.Invoke(packet);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
        Drop();
    }

    private void Drop()
    {
        if (Interlocked.Exchange(ref _connected, 0) != 1) return;
        _client?.Dispose();
        PeerDisconnected?.Invoke(new PeerInfo("host"));
    }

    public async Task SendAsync(byte[] packet)
    {
        if (!IsConnected || _stream == null) return;
        try
        {
            await LanFraming.WriteAsync(_stream, packet, _gate);
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            Drop();
        }
    }

    public Task DisconnectAsync()
    {
        _cts?.Cancel();
        Drop();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());
}

/// <summary>Finds rooms on the local network by broadcasting a query every second.</summary>
public sealed class LanDiscovery : IDisposable
{
    private readonly ConcurrentDictionary<string, (LanAdvertisement Ad, DateTime Seen)> _rooms = new();
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    public IReadOnlyList<LanAdvertisement> Rooms => _rooms.Values
        .Where(r => DateTime.UtcNow - r.Seen < TimeSpan.FromSeconds(5))
        .Select(r => r.Ad)
        .OrderBy(a => a.RoomName)
        .ToList();

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        _ = SendLoop(_cts.Token);
        _ = ReceiveLoop(_cts.Token);
    }

    private static IEnumerable<IPAddress> BroadcastAddresses()
    {
        yield return IPAddress.Broadcast;
        yield return IPAddress.Loopback;
        IEnumerable<NetworkInterface> interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception)
        {
            yield break;
        }
        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            IPInterfaceProperties props;
            try
            {
                props = nic.GetIPProperties();
            }
            catch (Exception)
            {
                continue;
            }
            foreach (var unicast in props.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                byte[] ip = unicast.Address.GetAddressBytes();
                byte[] mask;
                try
                {
                    mask = unicast.IPv4Mask.GetAddressBytes();
                }
                catch (Exception)
                {
                    continue;
                }
                var broadcast = new byte[4];
                for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                yield return new IPAddress(broadcast);
            }
        }
    }

    private async Task SendLoop(CancellationToken cancel)
    {
        byte[] query = Encoding.UTF8.GetBytes(LanHostTransport.Query);
        while (!cancel.IsCancellationRequested)
        {
            foreach (var address in BroadcastAddresses().Distinct())
            {
                try
                {
                    await _udp!.SendAsync(query, query.Length, new IPEndPoint(address, LanHostTransport.DiscoveryPort));
                }
                catch (Exception e) when (e is SocketException or ObjectDisposedException)
                {
                }
            }
            try
            {
                await Task.Delay(1000, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReceiveLoop(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                var result = await _udp!.ReceiveAsync(cancel);
                string text = Encoding.UTF8.GetString(result.Buffer);
                if (!text.StartsWith(LanHostTransport.Answer)) continue;
                var ad = JsonSerializer.Deserialize<LanAdvertisement>(text[LanHostTransport.Answer.Length..]);
                if (ad == null) continue;
                ad.Address = result.RemoteEndPoint.Address.ToString();
                _rooms[$"{ad.Address}:{ad.Port}"] = (ad, DateTime.UtcNow);
            }
            catch (Exception) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is SocketException or JsonException)
            {
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udp?.Dispose();
        _udp = null;
        _rooms.Clear();
    }

    public void Dispose() => Stop();
}
