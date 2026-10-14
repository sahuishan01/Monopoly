using System.Diagnostics;
using Game.WebClient;
using Microsoft.AspNetCore.Components;

namespace Game.Web.Services;

public static class GameLoop
{
    public static async Task<GameSession> OpenAsync(Session session, NavigationManager nav, string code)
    {
        if (session.Game != null && session.Game.Client.Lobby?.RoomCode == code) return session.Game;
        if (session.Game != null) await session.Game.DisconnectAsync();
        var uri = BrowserSocket.RoomUri(new Uri(nav.BaseUri), code, session.Auth?.Token ?? "");
        string resume = await session.LoadResumeAsync(code);
        var game = new GameSession(BrowserSocket.Create(uri), session.PlayerName, session.Auth?.Token ?? "", resume);
        session.Game = game;
        await game.ConnectAsync();
        _ = RunAsync(game, session, code);
        return game;
    }

    private static async Task RunAsync(GameSession game, Session session, string code)
    {
        try
        {
            var clock = Stopwatch.StartNew();
            string saved = "";
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            while (await timer.WaitForNextTickAsync())
            {
                if (!ReferenceEquals(session.Game, game)) return;
                game.Tick(clock.Elapsed.TotalSeconds);
                string token = game.Client.ResumeToken;
                if (token.Length > 0 && token != saved)
                {
                    saved = token;
                    await session.SaveResumeAsync(code, token);
                }
            }
        }
        catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            // Component or session torn down mid-tick; the loop simply ends.
        }
    }
}
