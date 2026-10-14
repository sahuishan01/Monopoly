using System.Net.WebSockets;
using Game.Net.Transport;

namespace Game.WebClient;

/// <summary>Browser-safe WebSocket factory. The browser WebSocket rejects most ClientWebSocketOptions, so none are set.</summary>
public static class BrowserSocket
{
    public static WebSocketClientTransport Create(Uri uri) =>
        new(async cancel =>
        {
            // No ClientWebSocketOptions here: the browser WebSocket throws PlatformNotSupported for KeepAlive, headers, etc.
            var socket = new ClientWebSocket();
            await socket.ConnectAsync(uri, cancel);
            return socket;
        }, uri.Host);

    public static Uri RoomUri(Uri origin, string code, string token)
    {
        var b = new UriBuilder(origin)
        {
            Scheme = origin.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = "/ws/" + Uri.EscapeDataString(code),
            Query = token.Length == 0 ? "" : "token=" + Uri.EscapeDataString(token),
        };
        return b.Uri;
    }
}
