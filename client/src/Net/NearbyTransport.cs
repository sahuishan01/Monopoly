using System.Buffers.Binary;
using System.Collections.Concurrent;
using Game.Net.Transport;
using Godot;

namespace BoardEmpire.Net;

public sealed record NearbyEndpoint(string Id, string Name);

/// <summary>
/// Bridge to the Android Nearby Connections plugin (Bluetooth / BLE / Wi-Fi without a router or
/// internet). One instance serves as the host transport, the client transport, or both in turn.
/// Payloads are chunked because Nearby limits BYTES payloads to roughly 32 KB.
/// </summary>
public sealed class NearbyBridge
{
    public const string ServiceId = "com.algosculptor.boardempire";
    private const string Singleton = "BoardEmpireNearby";
    private const int ChunkSize = 28_000;

    private readonly GodotObject? _plugin;
    private readonly ConcurrentDictionary<string, Dictionary<int, byte[][]>> _partial = new();
    private int _nextMessage;

    public static bool Available => Engine.HasSingleton(Singleton);

    public event Action<NearbyEndpoint>? EndpointFound;
    public event Action<string>? EndpointLost;
    /// <summary>(endpoint id, remote name, 4-digit code, incoming). Both players must confirm the same code.</summary>
    public event Action<string, string, string, bool>? ConnectionInitiated;
    public event Action<string, bool>? ConnectionResult;
    public event Action<string>? Disconnected;
    public event Action<string, byte[]>? PayloadReceived;
    public event Action<string>? Status;

    public NearbyBridge()
    {
        if (!Available) return;
        _plugin = Engine.GetSingleton(Singleton);
        _plugin.Connect("endpoint_found", Callable.From<string, string>((id, name) => EndpointFound?.Invoke(new NearbyEndpoint(id, name))));
        _plugin.Connect("endpoint_lost", Callable.From<string>(id => EndpointLost?.Invoke(id)));
        _plugin.Connect("connection_initiated", Callable.From<string, string, string, bool>((id, name, code, incoming) =>
            ConnectionInitiated?.Invoke(id, name, code, incoming)));
        _plugin.Connect("connection_result", Callable.From<string, bool>((id, ok) => ConnectionResult?.Invoke(id, ok)));
        _plugin.Connect("disconnected", Callable.From<string>(id =>
        {
            _partial.TryRemove(id, out _);
            Disconnected?.Invoke(id);
        }));
        _plugin.Connect("payload_received", Callable.From<string, byte[]>(OnChunk));
        _plugin.Connect("status", Callable.From<string>(message => Status?.Invoke(message)));
    }

    public void RequestPermissions() => _plugin?.Call("requestPermissions");

    public bool HasPermissions() => _plugin != null && _plugin.Call("hasPermissions").AsBool();

    public void StartAdvertising(string name) => _plugin?.Call("startAdvertising", name, ServiceId);

    public void StopAdvertising() => _plugin?.Call("stopAdvertising");

    public void StartDiscovery() => _plugin?.Call("startDiscovery", ServiceId);

    public void StopDiscovery() => _plugin?.Call("stopDiscovery");

    public void RequestConnection(string localName, string endpointId) => _plugin?.Call("requestConnection", localName, endpointId);

    public void Accept(string endpointId) => _plugin?.Call("acceptConnection", endpointId);

    public void Reject(string endpointId) => _plugin?.Call("rejectConnection", endpointId);

    public void Disconnect(string endpointId) => _plugin?.Call("disconnect", endpointId);

    public void StopAll() => _plugin?.Call("stopAll");

    public void Send(string endpointId, byte[] packet)
    {
        if (_plugin == null) return;
        int message = Interlocked.Increment(ref _nextMessage);
        int count = Math.Max(1, (packet.Length + ChunkSize - 1) / ChunkSize);
        for (int i = 0; i < count; i++)
        {
            int offset = i * ChunkSize;
            int length = Math.Min(ChunkSize, packet.Length - offset);
            var chunk = new byte[8 + length];
            BinaryPrimitives.WriteInt32LittleEndian(chunk, message);
            BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(4), (ushort)i);
            BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(6), (ushort)count);
            Array.Copy(packet, offset, chunk, 8, length);
            _plugin.Call("sendBytes", endpointId, chunk);
        }
    }

    private void OnChunk(string endpointId, byte[] chunk)
    {
        if (chunk.Length < 8) return;
        int message = BinaryPrimitives.ReadInt32LittleEndian(chunk);
        int index = BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(4));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(6));
        var data = chunk.AsSpan(8).ToArray();
        if (count <= 1)
        {
            PayloadReceived?.Invoke(endpointId, data);
            return;
        }
        var messages = _partial.GetOrAdd(endpointId, _ => new Dictionary<int, byte[][]>());
        lock (messages)
        {
            if (!messages.TryGetValue(message, out var parts))
            {
                parts = new byte[count][];
                messages[message] = parts;
            }
            if (index >= parts.Length) return;
            parts[index] = data;
            if (parts.Any(p => p == null)) return;
            messages.Remove(message);
            PayloadReceived?.Invoke(endpointId, parts.SelectMany(p => p).ToArray());
        }
    }
}

public sealed class NearbyHostTransport : IGameHostTransport
{
    private readonly NearbyBridge _bridge;
    private readonly string _name;

    public event Action<string, byte[]>? PacketReceived;
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;

    public NearbyHostTransport(NearbyBridge bridge, string advertisedName)
    {
        _bridge = bridge;
        _name = advertisedName;
        bridge.ConnectionResult += (id, ok) =>
        {
            if (ok) PeerConnected?.Invoke(new PeerInfo(id, "nearby"));
        };
        bridge.Disconnected += id => PeerDisconnected?.Invoke(new PeerInfo(id));
        bridge.PayloadReceived += (id, data) => PacketReceived?.Invoke(id, data);
    }

    public Task StartAsync(CancellationToken cancel = default)
    {
        _bridge.StartAdvertising(_name);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _bridge.StopAll();
        return Task.CompletedTask;
    }

    public Task SendAsync(string peerId, byte[] packet)
    {
        _bridge.Send(peerId, packet);
        return Task.CompletedTask;
    }

    public void Disconnect(string peerId) => _bridge.Disconnect(peerId);

    public ValueTask DisposeAsync() => new(StopAsync());
}

public sealed class NearbyClientTransport : IGameTransport
{
    private readonly NearbyBridge _bridge;
    private readonly string _endpointId;
    private readonly string _localName;
    private TaskCompletionSource<bool>? _connecting;

    public bool IsConnected { get; private set; }

    public event Action<byte[]>? PacketReceived;
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;

    public NearbyClientTransport(NearbyBridge bridge, string endpointId, string localName)
    {
        _bridge = bridge;
        _endpointId = endpointId;
        _localName = localName;
        bridge.ConnectionResult += (id, ok) =>
        {
            if (id != _endpointId) return;
            IsConnected = ok;
            _connecting?.TrySetResult(ok);
            if (ok) PeerConnected?.Invoke(new PeerInfo(id, "nearby"));
        };
        bridge.Disconnected += id =>
        {
            if (id != _endpointId || !IsConnected) return;
            IsConnected = false;
            PeerDisconnected?.Invoke(new PeerInfo(id));
        };
        bridge.PayloadReceived += (id, data) =>
        {
            if (id == _endpointId) PacketReceived?.Invoke(data);
        };
    }

    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        _connecting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bridge.RequestConnection(_localName, _endpointId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        await using var registration = timeout.Token.Register(() => _connecting.TrySetResult(false));
        if (!await _connecting.Task) throw new IOException("Nearby connection was not accepted");
    }

    public Task DisconnectAsync()
    {
        if (IsConnected)
        {
            IsConnected = false;
            _bridge.Disconnect(_endpointId);
        }
        return Task.CompletedTask;
    }

    public Task SendAsync(byte[] packet)
    {
        if (IsConnected) _bridge.Send(_endpointId, packet);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
