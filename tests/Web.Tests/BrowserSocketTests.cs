using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class BrowserSocketTests
{
    [Fact]
    public void Create_returns_a_transport_without_connecting()
    {
        var t = BrowserSocket.Create(new Uri("wss://example.test/ws/ABCD?token=x"));
        Assert.False(t.IsConnected);
    }

    [Fact]
    public void Url_builder_converts_http_origin_to_ws_and_escapes_the_token()
    {
        var uri = BrowserSocket.RoomUri(new Uri("https://monopoly.algosculptor.com/"), "AB CD", "t k+n");
        Assert.Equal("wss", uri.Scheme);
        Assert.Equal("/ws/AB%20CD", uri.AbsolutePath);
        Assert.Contains("token=t%20k%2Bn", uri.Query);
    }
}
