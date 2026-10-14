using System.Diagnostics;
using Game.WebClient;
using Microsoft.AspNetCore.Components;

namespace Game.Web.Services;

public static class GameLoop
{
    public static async Task<GameSession> OpenAsync(Session session, NavigationManager nav, string code)
    {
        if (session.Game != null && session.GameCode == code) return session.Game;
        await session.CloseGameAsync();
        var uri = BrowserSocket.RoomUri(new Uri(nav.BaseUri), code, session.Auth?.Token ?? "");
        string resume = await session.LoadResumeAsync(code);
        var game = new GameSession(BrowserSocket.Create(uri), session.PlayerName, session.Auth?.Token ?? "", resume);
        var cts = new CancellationTokenSource();
        session.Game = game;
        session.GameCode = code;
        session.LoopCts = cts;
        try
        {
            await game.ConnectAsync();
        }
        catch
        {
            // Keep session.Game so callers can inspect UpdateRequired; a retry will replace it.
            cts.Cancel();
            session.GameCode = null;
            throw;
        }
        _ = RunAsync(game, session, code, cts.Token);
        return game;
    }

    private static async Task RunAsync(GameSession game, Session session, string code, CancellationToken ct)
    {
        try
        {
            var clock = Stopwatch.StartNew();
            string saved = "";
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!ReferenceEquals(session.Game, game)) return;
                game.Tick(clock.Elapsed.TotalSeconds);
                string token = game.Client.ResumeToken;
                if (token.Length > 0 && token != saved)
                {
                    saved = token;
                    try { await session.SaveResumeAsync(code, token); }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        Console.Error.WriteLine("resume save failed: " + e.Message);
                    }
                }
            }
        }
        catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            // Component or session torn down mid-tick; the loop simply ends.
        }
    }
}
