using BoardEmpire.Core;
using BoardEmpire.Net;
using BoardEmpire.UI;
using Game.Core.Rules;
using Godot;

namespace BoardEmpire;

/// <summary>
/// Developer command line (after "--"): open a screen, start a bot match and capture screenshots,
/// so the client can be exercised on machines without a display.
///   --screen menu|play|setup|settings|rules|profile|online|lan
///   --demo [preset]  --players N  --view 2d|3d  --speed normal|fast|minimal
///   --shot path.png  --after seconds   --quit-after seconds   --autoplay
/// </summary>
public static class DevAutomation
{
    public static bool Autoplay { get; private set; }

    public static void Run(App app)
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Length == 0) return;
        string Value(string key, string fallback)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : fallback;
        }
        bool Has(string key) => Array.IndexOf(args, key) >= 0;

        var settings = app.Settings;
        if (Has("--view")) settings.ViewMode = Value("--view", "3d") == "2d" ? ViewMode.Flat2D : ViewMode.City3D;
        if (Has("--speed")) settings.AnimationSpeed = Enum.Parse<AnimationSpeed>(Value("--speed", "normal"), true);
        if (Has("--contrast")) settings.HighContrast = true;
        if (Has("--patterns")) settings.ColorBlindPatterns = true;
        if (Has("--lang")) settings.Language = Value("--lang", "en");
        if (Has("--quality")) settings.Quality = Enum.Parse<Quality>(Value("--quality", "medium"), true);
        if (Has("--contrast") || Has("--lang"))
        {
            Loc.Language = settings.Language;
            app.RefreshTheme();
        }
        Autoplay = Has("--autoplay");

        switch (Value("--screen", ""))
        {
            case "play": app.Go(new PlayMenuScreen()); break;
            case "setup": app.Go(new SetupScreen(SetupMode.Solo)); break;
            case "passplay": app.Go(new SetupScreen(SetupMode.PassAndPlay)); break;
            case "settings": app.Go(new SettingsScreen()); break;
            case "rules": app.Go(new RulesScreen()); break;
            case "profile": app.Go(new ProfileScreen()); break;
            case "online": app.Go(new OnlineScreen()); break;
            case "lan": app.Go(new LanBrowserScreen()); break;
            case "replays": app.Go(new ReplayListScreen()); break;
        }

        if (Has("--demo")) _ = StartDemo(app, Value("--demo", "classic"), int.Parse(Value("--players", "4")), Has("--human"));
        if (Has("--host-lan")) _ = HostLan(app, Value("--host-lan", "classic"), int.Parse(Value("--wait-seats", "2")));
        if (Has("--join-lan")) _ = JoinLan(app, Value("--join-lan", "127.0.0.1"));
        if (Has("--online-quick")) _ = OnlineQuick(app, Value("--online-quick", "http://127.0.0.1:8091"), Value("--name", "Dev"));

        double shotAt = double.Parse(Value("--after", "2"), System.Globalization.CultureInfo.InvariantCulture);
        string shot = Value("--shot", "");
        double quitAt = double.Parse(Value("--quit-after", shot.Length > 0 ? (shotAt + 0.5).ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"),
            System.Globalization.CultureInfo.InvariantCulture);
        if (shot.Length > 0)
        {
            app.GetTree().CreateTimer(shotAt).Timeout += () =>
            {
                var image = app.GetViewport().GetTexture().GetImage();
                image.SavePng(shot);
                GD.Print($"screenshot saved: {shot} ({image.GetWidth()}x{image.GetHeight()})");
            };
        }
        if (quitAt > 0)
        {
            app.GetTree().CreateTimer(quitAt).Timeout += () =>
            {
                var state = app.Session.Client?.State;
                if (state != null)
                    GD.Print($"match state: turn {state.TurnNumber}, round {state.Round}, phase {state.Phase}, over {state.IsOver}, version {state.Version}, " +
                             $"hash {Game.Core.Serialization.StateHasher.Hash(state)[..12]}, resyncs {app.Session.Client!.ResyncCount}");
                app.GetTree().Quit();
            };
        }
    }

    private static async Task HostLan(App app, string preset, int waitSeats)
    {
        var rules = RulePresets.Get(RulePresets.Ids.Contains(preset) ? preset : "classic");
        if (!await app.Session.HostLanAsync("Dev room", rules, app.Settings.LastBoard, false)) return;
        app.Session.Host!.Room.Options.BotDelaySeconds = 0.05;
        app.Go(new LobbyScreen(), clear: true);
        GD.Print($"hosting on port {app.Session.LanPort}");
        bool started = false;
        var timer = new Godot.Timer { WaitTime = 0.5, Autostart = true };
        app.AddChild(timer);
        timer.Timeout += () =>
        {
            var client = app.Session.Client;
            if (started || client?.Lobby == null || client.Lobby.Seats.Count < waitSeats) return;
            if (client.Lobby.Seats.Any(s => !s.IsBot && !s.Ready && s.PeerId != client.Lobby.HostPeerId)) return;
            started = true;
            client.AddBot(BotLevel.Medium);
            client.StartMatch();
        };
    }

    private static async Task JoinLan(App app, string address)
    {
        int port = Game.Net.Transport.LanHostTransport.DefaultPort;
        int colon = address.LastIndexOf(':');
        if (colon > 0 && int.TryParse(address[(colon + 1)..], out int parsed))
        {
            port = parsed;
            address = address[..colon];
        }
        if (!await app.Session.JoinLanAsync(address, port))
        {
            GD.PushError("join failed");
            return;
        }
        app.Go(new LobbyScreen(), clear: true);
        var timer = new Godot.Timer { WaitTime = 0.5, Autostart = true };
        app.AddChild(timer);
        timer.Timeout += () =>
        {
            var client = app.Session.Client;
            if (client?.Lobby == null || client.State != null) return;
            if (client.Lobby.Seats.Where(s => client.Controls(s.Seat)).Any(s => !s.Ready)) client.SetReady(true);
        };
    }

    private static async Task OnlineQuick(App app, string url, string name)
    {
        var api = app.Session.Api;
        api.Configure(url, "");
        var guest = await api.Guest(name);
        if (!guest.Ok)
        {
            GD.PushError("guest sign-in failed: " + guest.Error);
            return;
        }
        api.Configure(url, guest.Text("token"));
        app.Session.PlayerName = name;
        var match = await api.Matchmaking(false, "blitz");
        if (!match.Ok || !await app.Session.JoinOnlineAsync(match.Text("code"), false))
        {
            GD.PushError("matchmaking failed: " + match.Error);
            return;
        }
        GD.Print($"joined online room {match.Text("code")}");
        app.Go(new LobbyScreen(), clear: true);
        app.Session.Client!.SetReady(true);
    }

    private static async Task StartDemo(App app, string preset, int players, bool human)
    {
        var rules = RulePresets.Get(RulePresets.Ids.Contains(preset) ? preset : "classic");
        var seats = new List<LocalSeat>();
        for (int i = 0; i < players; i++)
            seats.Add(new LocalSeat { Name = i == 0 && human ? "Ishan" : new[] { "Ada", "Rahul", "Priya", "Akash", "Mira", "Dev", "Zoya", "Kabir" }[i], IsBot = !(i == 0 && human), BotLevel = BotLevel.Hard });
        if (await app.Session.StartLocalAsync(seats, rules, app.Settings.LastBoard))
        {
            if (app.Session.Host != null && OS.GetCmdlineUserArgs().Contains("--turbo")) app.Session.Host.Room.Options.BotDelaySeconds = 0.05;
            app.Go(new BoardScreen(), clear: true);
        }
        else GD.PushError("demo match failed to start");
    }
}
