using Game.WebClient;
using Microsoft.JSInterop;

namespace Game.Web.Services;

/// <summary>Per-tab identity and the active GameSession, persisted to sessionStorage so a refresh resumes the seat.</summary>
public sealed class Session
{
    private readonly IJSRuntime _js;
    public ApiClient Api { get; }
    public AuthResult? Auth { get; private set; }
    public string PlayerName { get; set; } = "";
    public GameSession? Game { get; set; }
    public string? GameCode { get; set; }
    public CancellationTokenSource? LoopCts { get; set; }

    /// <summary>Stops the tick loop and disconnects the active game, if any.</summary>
    public async Task CloseGameAsync()
    {
        var game = Game;
        LoopCts?.Cancel();
        LoopCts = null;
        GameCode = null;
        Game = null;
        if (game != null)
        {
            try { await game.DisconnectAsync(); } catch (Exception) { }
        }
    }

    public Session(ApiClient api, IJSRuntime js)
    {
        Api = api;
        _js = js;
    }

    public async Task<AuthResult> EnsureGuestAsync(string name)
    {
        if (Auth != null) return Auth;
        PlayerName = name;
        Auth = await Api.GuestAsync(name);
        Api.Token = Auth.Token;
        return Auth;
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        Auth = await Api.LoginAsync(username, password);
        Api.Token = Auth.Token;
        PlayerName = Auth.DisplayName;
        return Auth;
    }

    public async Task<AuthResult> RegisterAsync(string username, string password, string displayName)
    {
        Auth = await Api.RegisterAsync(username, password, displayName);
        Api.Token = Auth.Token;
        PlayerName = Auth.DisplayName;
        return Auth;
    }

    public async Task SaveResumeAsync(string code, string token)
    {
        try { await _js.InvokeVoidAsync("sessionStorage.setItem", "be.resume." + code, token); }
        catch (JSException) { }
    }

    public async Task<string> LoadResumeAsync(string code)
    {
        try { return await _js.InvokeAsync<string?>("sessionStorage.getItem", "be.resume." + code) ?? ""; }
        catch (JSException) { return ""; }
    }
}
