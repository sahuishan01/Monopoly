using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BoardEmpire.Net;

public sealed record ApiResult(bool Ok, JsonElement Body, string Error)
{
    public string Text(string property) =>
        Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
}

/// <summary>REST client for the BoardEmpire server (accounts, rooms, matchmaking, social).</summary>
public sealed class OnlineApi
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public string BaseUrl { get; private set; } = "";
    public string Token { get; private set; } = "";

    public void Configure(string baseUrl, string token)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        Token = token;
        _http.DefaultRequestHeaders.Authorization = token.Length > 0 ? new AuthenticationHeaderValue("Bearer", token) : null;
    }

    public Uri SocketUri(string roomCode)
    {
        string ws = BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "wss://" + BaseUrl[8..]
            : "ws://" + BaseUrl.Replace("http://", "", StringComparison.OrdinalIgnoreCase);
        return new Uri($"{ws}/ws/{roomCode}?token={Uri.EscapeDataString(Token)}");
    }

    private async Task<ApiResult> Send(HttpMethod method, string path, object? body = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, BaseUrl + path);
            if (body != null) request.Content = JsonContent.Create(body);
            using var response = await _http.SendAsync(request);
            string text = await response.Content.ReadAsStringAsync();
            JsonElement json = default;
            if (text.Length > 0)
            {
                try
                {
                    json = JsonDocument.Parse(text).RootElement.Clone();
                }
                catch (JsonException)
                {
                }
            }
            if (response.IsSuccessStatusCode) return new ApiResult(true, json, "");
            string error = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var e)
                ? e.GetString() ?? ""
                : $"Server error {(int)response.StatusCode}";
            return new ApiResult(false, json, error);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return new ApiResult(false, default, "Cannot reach the server");
        }
    }

    public Task<ApiResult> Version() => Send(HttpMethod.Get, "/api/version");
    public Task<ApiResult> Guest(string name) => Send(HttpMethod.Post, "/api/auth/guest", new { name });
    public Task<ApiResult> Register(string username, string password, string displayName) =>
        Send(HttpMethod.Post, "/api/auth/register", new { username, password, displayName });
    public Task<ApiResult> Login(string username, string password) => Send(HttpMethod.Post, "/api/auth/login", new { username, password });
    public Task<ApiResult> Me() => Send(HttpMethod.Get, "/api/me");
    public Task<ApiResult> UpdateProfile(string displayName, int avatar) => Send(HttpMethod.Put, "/api/me", new { displayName, avatar });
    public Task<ApiResult> CreateRoom(string name, string preset, string board, int maxPlayers, bool isPublic, object? rules = null) =>
        Send(HttpMethod.Post, "/api/rooms", new { name, preset, board, maxPlayers, @public = isPublic, rules });
    public Task<ApiResult> Room(string code) => Send(HttpMethod.Get, "/api/rooms/" + Uri.EscapeDataString(code));
    public Task<ApiResult> PublicRooms() => Send(HttpMethod.Get, "/api/rooms");
    public Task<ApiResult> Matchmaking(bool ranked, string preset) =>
        Send(HttpMethod.Post, "/api/matchmaking/" + (ranked ? "ranked" : "quick"), new { preset });
    public Task<ApiResult> Friends() => Send(HttpMethod.Get, "/api/friends");
    public Task<ApiResult> AddFriend(string username) => Send(HttpMethod.Post, "/api/friends", new { username });
    public Task<ApiResult> AddFriendById(string userId) => Send(HttpMethod.Post, "/api/friends", new { userId });
    public Task<ApiResult> RemoveFriend(string userId) => Send(HttpMethod.Delete, "/api/friends/" + Uri.EscapeDataString(userId));
    public Task<ApiResult> RecentPlayers() => Send(HttpMethod.Get, "/api/recent-players");
    public Task<ApiResult> Invite(string userId, string roomCode) => Send(HttpMethod.Post, "/api/invites", new { userId, roomCode });
    public Task<ApiResult> Leaderboard() => Send(HttpMethod.Get, "/api/leaderboard");
    public Task<ApiResult> RecentMatches() => Send(HttpMethod.Get, "/api/matches/recent");

    public async Task<string?> Replay(string matchId)
    {
        try
        {
            return await _http.GetStringAsync($"{BaseUrl}/api/matches/{Uri.EscapeDataString(matchId)}/replay");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }
}
