using System.Net;
using System.Text;
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public sealed class FakeHandler : HttpMessageHandler
{
    public HttpRequestMessage? Last;
    public string? LastBody;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string Body = "{}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        Last = request;
        LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancel);
        return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
    }
}

public class ApiClientTests
{
    private static (ApiClient api, FakeHandler h) Make()
    {
        var h = new FakeHandler();
        return (new ApiClient(new HttpClient(h) { BaseAddress = new Uri("https://example.test/") }), h);
    }

    [Fact]
    public async Task Guest_posts_name_and_returns_token()
    {
        var (api, h) = Make();
        h.Body = "{\"userId\":\"u1\",\"token\":\"tok\",\"displayName\":\"Ann\",\"guest\":true}";
        var r = await api.GuestAsync("Ann");
        Assert.Equal("/api/auth/guest", h.Last!.RequestUri!.AbsolutePath);
        Assert.Contains("\"name\":\"Ann\"", h.LastBody);
        Assert.Equal("tok", r.Token);
        Assert.True(r.Guest);
    }

    [Fact]
    public async Task Authenticated_calls_send_bearer_token()
    {
        var (api, h) = Make();
        api.Token = "tok";
        h.Body = "[]";
        await api.RoomsAsync();
        Assert.Equal("Bearer", h.Last!.Headers.Authorization!.Scheme);
        Assert.Equal("tok", h.Last.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Error_body_becomes_ApiException_with_server_message()
    {
        var (api, h) = Make();
        h.Status = HttpStatusCode.Unauthorized;
        h.Body = "{\"error\":\"Wrong username or password\"}";
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync("a", "b"));
        Assert.Equal(401, ex.Status);
        Assert.Equal("Wrong username or password", ex.Message);
    }

    [Fact]
    public async Task RoomExists_is_false_on_404()
    {
        var (api, h) = Make();
        h.Status = HttpStatusCode.NotFound;
        h.Body = "{\"error\":\"Room not found\"}";
        Assert.False(await api.RoomExistsAsync("ABCD"));
    }

    [Fact]
    public async Task Rooms_parse_camel_case_summaries()
    {
        var (api, h) = Make();
        h.Body = "[{\"code\":\"ABCD\",\"name\":\"Ann's game\",\"players\":2,\"maxPlayers\":6,\"inMatch\":false,\"preset\":\"classic\",\"board\":\"neo_city\",\"ranked\":false,\"spectators\":0}]";
        var rooms = await api.RoomsAsync();
        Assert.Single(rooms);
        Assert.Equal("ABCD", rooms[0].Code);
        Assert.Equal(6, rooms[0].MaxPlayers);
    }
}
