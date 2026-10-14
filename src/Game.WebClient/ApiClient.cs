using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Game.WebClient;

public sealed record AuthResult(string UserId, string Token, string DisplayName, bool Guest);

public sealed record RoomSummary(string Code, string Name, int Players, int MaxPlayers, bool InMatch,
    string Preset, string Board, bool Ranked, int Spectators);

public sealed class ApiException : Exception
{
    public int Status { get; }
    public ApiException(string message, int status) : base(message) => Status = status;
}

/// <summary>REST client for the match server. Same-origin in the browser.</summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public string Token { get; set; } = "";

    public ApiClient(HttpClient http) => _http = http;

    public Task<AuthResult> GuestAsync(string name) =>
        Send<AuthResult>(HttpMethod.Post, "api/auth/guest", new { name });

    public Task<AuthResult> LoginAsync(string username, string password) =>
        Send<AuthResult>(HttpMethod.Post, "api/auth/login", new { username, password });

    public Task<AuthResult> RegisterAsync(string username, string password, string displayName) =>
        Send<AuthResult>(HttpMethod.Post, "api/auth/register", new { username, password, displayName });

    public async Task<IReadOnlyList<RoomSummary>> RoomsAsync() =>
        await Send<List<RoomSummary>>(HttpMethod.Get, "api/rooms", null) ?? new();

    public async Task<string> CreateRoomAsync(string name, string preset, string board, int maxPlayers, bool isPublic)
    {
        var r = await Send<CodeResult>(HttpMethod.Post, "api/rooms",
            new { name, preset, board, maxPlayers, @public = isPublic });
        return r.Code;
    }

    public async Task<string> QuickMatchAsync(string preset)
    {
        var r = await Send<CodeResult>(HttpMethod.Post, "api/matchmaking/quick", new { preset });
        return r.Code;
    }

    public async Task<bool> RoomExistsAsync(string code)
    {
        try
        {
            await Send<RoomSummary>(HttpMethod.Get, "api/rooms/" + Uri.EscapeDataString(code), null);
            return true;
        }
        catch (ApiException e) when (e.Status == 404)
        {
            return false;
        }
    }

    private sealed record CodeResult(string Code);

    private async Task<T> Send<T>(HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        if (Token.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (body != null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string message = response.ReasonPhrase ?? "Request failed";
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorBody>(Json);
                if (!string.IsNullOrEmpty(error?.Error)) message = error.Error;
            }
            catch (JsonException) { }
            throw new ApiException(message, (int)response.StatusCode);
        }
        var result = await response.Content.ReadFromJsonAsync<T>(Json);
        return result ?? throw new ApiException("Empty response", (int)HttpStatusCode.BadGateway);
    }

    private sealed record ErrorBody(string? Error);
}
