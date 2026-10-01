using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Game.Core.Serialization;

namespace Game.Protocol;

/// <summary>The wire envelope around every message.</summary>
public sealed class NetworkEnvelope
{
    public int ProtocolVersion { get; set; } = ProtocolInfo.Version;
    public string MatchId { get; set; } = "";
    public string MessageId { get; set; } = "";
    public long Sequence { get; set; }
    public string MessageType { get; set; } = "";
    public JsonElement Payload { get; set; }
}

public sealed class ProtocolException : Exception
{
    public ProtocolException(string message) : base(message) { }
}

/// <summary>
/// Encodes messages as UTF-8 JSON inside a versioned envelope. Large packets are deflated so
/// that snapshots also fit transports with small payload limits (Nearby Connections).
/// </summary>
public static class PacketCodec
{
    private const byte Raw = 0;
    private const byte Deflated = 1;
    private const int CompressAbove = 900;
    public const int MaxPacketBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(CoreJson.Options);

    private static readonly Dictionary<string, Type> Types = typeof(NetMessage).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(NetMessage).IsAssignableFrom(t))
        .ToDictionary(t => t.Name);

    public static byte[] Encode(NetMessage message, string matchId, long sequence)
    {
        var envelope = new NetworkEnvelope
        {
            MatchId = matchId,
            MessageId = Guid.NewGuid().ToString("N"),
            Sequence = sequence,
            MessageType = message.GetType().Name,
            Payload = JsonSerializer.SerializeToElement(message, message.GetType(), Options),
        };
        return Pack(JsonSerializer.SerializeToUtf8Bytes(envelope, Options));
    }

    public static NetworkEnvelope DecodeEnvelope(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 2) throw new ProtocolException("Packet too short");
        byte[] json = Unpack(packet);
        try
        {
            return JsonSerializer.Deserialize<NetworkEnvelope>(json, Options) ?? throw new ProtocolException("Empty envelope");
        }
        catch (JsonException e)
        {
            throw new ProtocolException("Malformed envelope: " + e.Message);
        }
    }

    public static NetMessage DecodeMessage(NetworkEnvelope envelope)
    {
        if (!Types.TryGetValue(envelope.MessageType, out var type))
            throw new ProtocolException($"Unknown message type '{envelope.MessageType}'");
        try
        {
            return (NetMessage)(envelope.Payload.Deserialize(type, Options) ?? throw new ProtocolException("Empty payload"));
        }
        catch (JsonException e)
        {
            throw new ProtocolException($"Malformed {envelope.MessageType}: {e.Message}");
        }
    }

    public static NetMessage Decode(ReadOnlySpan<byte> packet, out NetworkEnvelope envelope)
    {
        envelope = DecodeEnvelope(packet);
        return DecodeMessage(envelope);
    }

    private static byte[] Pack(byte[] json)
    {
        if (json.Length <= CompressAbove)
        {
            var raw = new byte[json.Length + 1];
            raw[0] = Raw;
            json.CopyTo(raw, 1);
            return raw;
        }
        using var ms = new MemoryStream(json.Length / 4 + 16);
        ms.WriteByte(Deflated);
        using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(json, 0, json.Length);
        return ms.ToArray();
    }

    private static byte[] Unpack(ReadOnlySpan<byte> packet)
    {
        switch (packet[0])
        {
            case Raw:
                return packet[1..].ToArray();
            case Deflated:
                using (var input = new MemoryStream(packet[1..].ToArray()))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        if (output.Length > MaxPacketBytes) throw new ProtocolException("Packet too large");
                    }
                    return output.ToArray();
                }
            default:
                throw new ProtocolException("Unknown packet framing");
        }
    }
}

/// <summary>Version negotiation shared by every authority.</summary>
public static class VersionCheck
{
    /// <summary>Null when the client may join, otherwise the rejection to send.</summary>
    public static Reject? Validate(int clientVersion)
    {
        if (clientVersion < ProtocolInfo.MinimumSupported)
            return new Reject(RejectCode.VersionTooOld, "Update required to play online", ProtocolInfo.MinimumSupported);
        if (clientVersion > ProtocolInfo.Version)
            return new Reject(RejectCode.VersionTooNew, "The host is running an older version", ProtocolInfo.MinimumSupported);
        return null;
    }
}
