using BoardEmpire.Core;
using BoardEmpire.Net;
using BoardEmpire.View2D;
using Game.Core.Board;
using Game.Core.Rules;
using Godot;

namespace BoardEmpire.UI;

public partial class MainMenuScreen : Screen
{
    public override Screen Recreate() => new MainMenuScreen();

    protected override void Build()
    {
        // The idle board doubles as the hero image.
        var art = new Board2DView { MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0.5f) };
        art.Build(BoardLibrary.Get(BoardLibrary.BoardIds.Contains(Settings.LastBoard) ? Settings.LastBoard : BoardLibrary.DefaultBoardId), Settings);
        art.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        art.AnchorLeft = 0.44f;
        AddChild(art);

        var buttons = Ui.VBox(10);
        if (App.Session.HasSave)
        {
            buttons.AddChild(Ui.Button(Loc.T("Continue match"), async () =>
            {
                if (await App.Session.ResumeAsync()) App.Go(new BoardScreen());
                else App.Go(new MainMenuScreen(), clear: true);
            }, ButtonKind.Primary, 300));
        }
        buttons.AddChild(Ui.Button(Loc.T("Play"), () => App.Go(new PlayMenuScreen()), App.Session.HasSave ? ButtonKind.Secondary : ButtonKind.Primary, 300));
        buttons.AddChild(Ui.Button(Loc.T("Rules"), () => App.Go(new RulesScreen()), ButtonKind.Secondary, 300));
        buttons.AddChild(Ui.Button(Loc.T("Replays"), () => App.Go(new ReplayListScreen()), ButtonKind.Secondary, 300));
        buttons.AddChild(Ui.Button(Loc.T("Profile"), () => App.Go(new ProfileScreen()), ButtonKind.Secondary, 300));
        buttons.AddChild(Ui.Button(Loc.T("Settings"), () => App.Go(new SettingsScreen()), ButtonKind.Secondary, 300));
        if (OS.GetName() is not ("Android" or "iOS" or "Web"))
            buttons.AddChild(Ui.Button(Loc.T("Quit"), App.Quit, ButtonKind.Ghost, 300));

        buttons.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        var title = Ui.Label("BOARD\nEMPIRE", 64, Tokens.Accent, mono: true, bold: true);
        title.AddThemeConstantOverride("line_spacing", -14);
        var column = Ui.VBox(22,
            Ui.Caption("Property · Trade · City"),
            title,
            Ui.Wrapped("Buy the streets, strike the deals and watch your city rise.", 17, Tokens.Muted).MinSize(320, 0),
            buttons,
            Ui.Label($"{Settings.PlayerName} · v0.1", 12, Tokens.Muted, mono: true));
        column.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var margin = Ui.Margin(column, 64, 40, 40, 40);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide);
        margin.AnchorRight = 0.45f;
        AddChild(margin);
    }
}

public enum SetupMode
{
    Solo,
    PassAndPlay,
    HostLan,
    HostNearby,
    OnlinePrivate,
}

public partial class PlayMenuScreen : Screen
{
    public override Screen Recreate() => new PlayMenuScreen();

    private Control Card(string caption, string blurb, Color color, params Control[] buttons)
    {
        var box = Ui.VBox(12, Ui.Label(Loc.T(caption).ToUpperInvariant(), 20, color, mono: true, bold: true), Ui.Wrapped(blurb, 15, Tokens.Muted), Ui.Spacer(false));
        foreach (var b in buttons) box.AddChild(b);
        var panel = Ui.Panel(box, Tokens.Panel, 20, 18);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        return panel;
    }

    protected override void Build()
    {
        bool nearby = NearbyBridge.Available;
        var solo = Card("Solo", "You against one to seven bots, from easy-going to expert. Works offline.", Tokens.Good,
            Ui.Button("Play vs bots", () => App.Go(new SetupScreen(SetupMode.Solo)), ButtonKind.Primary));

        var nearbyHost = Ui.Button($"{Loc.T("Nearby")}: host", () => App.Go(new SetupScreen(SetupMode.HostNearby)));
        var nearbyJoin = Ui.Button($"{Loc.T("Nearby")}: join", () => App.Go(new NearbyScreen()));
        nearbyHost.Disabled = nearbyJoin.Disabled = !nearby;
        if (!nearby) nearbyHost.TooltipText = nearbyJoin.TooltipText = "Nearby play needs an Android device";
        var local = Card("Local", "Share one device, or connect phones over the same Wi-Fi or Bluetooth. No internet needed.", Tokens.Info,
            Ui.Button(Loc.T("Pass & Play"), () => App.Go(new SetupScreen(SetupMode.PassAndPlay)), ButtonKind.Primary),
            Ui.Button($"{Loc.T("Same Wi-Fi")}: host", () => App.Go(new SetupScreen(SetupMode.HostLan))),
            Ui.Button($"{Loc.T("Same Wi-Fi")}: join", () => App.Go(new LanBrowserScreen())),
            nearbyHost, nearbyJoin);

        var online = Card("Online", "Quick matches, ranked play and private rooms on the dedicated server.", Tokens.Accent,
            Ui.Button("Go online", () => App.Go(new OnlineScreen()), ButtonKind.Primary));

        var cards = Ui.HBox(16, solo, local, online);
        cards.SizeFlagsVertical = SizeFlags.ExpandFill;
        Page("Play", cards);
    }
}

/// <summary>Seats, ruleset and board for a match hosted on this device (or a private online room).</summary>
public partial class SetupScreen : Screen
{
    private readonly SetupMode _mode;
    private readonly List<LocalSeat> _seats = new();
    private string _preset;
    private string _board;
    private string _roomName;
    private VBoxContainer _seatList = null!;
    private Label _summary = null!;

    public SetupScreen(SetupMode mode)
    {
        _mode = mode;
        var settings = App.I.Settings;
        _preset = settings.LastPreset;
        _board = BoardLibrary.BoardIds.Contains(settings.LastBoard) ? settings.LastBoard : BoardLibrary.DefaultBoardId;
        _roomName = settings.PlayerName + "'s game";
        _seats.Add(new LocalSeat { Name = settings.PlayerName });
        if (mode == SetupMode.Solo)
            for (int i = 0; i < 3; i++) _seats.Add(new LocalSeat { IsBot = true, BotLevel = BotLevel.Medium });
        else if (mode == SetupMode.PassAndPlay)
            _seats.Add(new LocalSeat { Name = "Player 2" });
    }

    public override Screen Recreate() => new SetupScreen(_mode);

    private List<(string Id, string Name, GameRules Rules)> Presets()
    {
        var list = RulePresets.Ids.Select(id => (id, RulePresets.Get(id).PresetName, RulePresets.Get(id))).ToList();
        foreach (var saved in Settings.Presets) list.Add(("custom:" + saved.Name, "★ " + saved.Name, saved.Rules.Clone()));
        return list;
    }

    private GameRules Rules()
    {
        var presets = Presets();
        var match = presets.FirstOrDefault(p => p.Id == _preset);
        return (match.Rules ?? presets[0].Rules).Clone();
    }

    public static string Describe(GameRules r)
    {
        var parts = new List<string> { $"cash {r.StartingMoney}", $"salary {r.Salary}", r.AuctionsEnabled ? $"{r.AuctionMode.ToString().ToLowerInvariant()} auctions" : "no auctions" };
        if (r.MaximumRounds > 0) parts.Add($"{r.MaximumRounds} rounds");
        if (r.TurnTimeSeconds > 0) parts.Add($"{r.TurnTimeSeconds}s turns");
        if (r.StartingProperties > 0) parts.Add($"{r.StartingProperties} starting properties");
        if (r.SecretObjectivesEnabled) parts.Add("secret objectives");
        if (r.MarketEventsEnabled) parts.Add("market & city events");
        if (r.PublicProjectsEnabled) parts.Add("public projects");
        if (r.ContractsEnabled) parts.Add("contracts");
        if (r.PropertySharesEnabled) parts.Add("property shares");
        if (r.AdvancedDevelopment) parts.Add("development types");
        if (r.AbilitiesEnabled) parts.Add("abilities");
        if (r.TeamsEnabled) parts.Add("teams");
        if (r.FreeParkingJackpot) parts.Add("jackpot");
        return string.Join(" · ", parts);
    }

    protected override void Build()
    {
        var presets = Presets();
        int presetIndex = Math.Max(0, presets.FindIndex(p => p.Id == _preset));
        _preset = presets[presetIndex].Id;
        var boards = BoardLibrary.All.ToList();
        _summary = Ui.Wrapped("", 14, Tokens.Muted);

        var match = Section("Match",
            Row("Rules", Ui.Options(presets.Select(p => p.Name), presetIndex, i =>
            {
                _preset = presets[i].Id;
                Update();
            })),
            _summary,
            Row("Board", Ui.Options(boards.Select(b => $"{b.Name} ({b.Count} tiles)"), Math.Max(0, boards.FindIndex(b => b.BoardId == _board)), i => _board = boards[i].BoardId)));
        if (_mode is SetupMode.HostLan or SetupMode.HostNearby or SetupMode.OnlinePrivate)
            match.GetChild<VBoxContainer>(0).AddChild(Row("Room name", Ui.Input(_roomName, "Room name", t => _roomName = t)));

        _seatList = Ui.VBox(8);
        var seatsBox = Ui.VBox(10, Ui.Caption("Players", Tokens.Accent), Ui.Scroll(_seatList).MinSize(0, 220));
        var add = Ui.HBox(8);
        if (_mode == SetupMode.PassAndPlay)
            add.AddChild(Ui.Button(Loc.T("Add player"), () => AddSeat(new LocalSeat { Name = $"Player {_seats.Count(x => !x.IsBot) + 1}" })).Expand());
        if (_mode is SetupMode.Solo or SetupMode.PassAndPlay)
            add.AddChild(Ui.Button(Loc.T("Add bot"), () => AddSeat(new LocalSeat { IsBot = true })).Expand());
        seatsBox.AddChild(add);
        var seats = Ui.Panel(seatsBox);

        string go = _mode switch
        {
            SetupMode.HostLan or SetupMode.HostNearby => "Open room",
            SetupMode.OnlinePrivate => "Create room",
            _ => Loc.T("Start match"),
        };
        var start = Ui.Button(go, Start, ButtonKind.Primary);
        var left = Ui.VBox(14, match, Ui.Spacer(false), start).Expand();
        left.SizeFlagsStretchRatio = 1.1f;
        Control body = _mode is SetupMode.Solo or SetupMode.PassAndPlay
            ? Ui.HBox(16, left, seats.Expand())
            : left;
        Page(_mode switch
        {
            SetupMode.Solo => "Solo",
            SetupMode.PassAndPlay => "Pass & Play",
            SetupMode.HostLan => "Same Wi-Fi",
            SetupMode.HostNearby => "Nearby",
            _ => "Private Room",
        }, body);
        Update();
        RebuildSeats();
    }

    private void Update() => _summary.Text = Describe(Rules());

    private void AddSeat(LocalSeat seat)
    {
        if (_seats.Count >= 8) return;
        _seats.Add(seat);
        RebuildSeats();
    }

    private void RebuildSeats()
    {
        foreach (var child in _seatList.GetChildren()) child.QueueFree();
        for (int i = 0; i < _seats.Count; i++)
        {
            var seat = _seats[i];
            int index = i;
            var swatch = new ColorRect { Color = Tokens.Player(i), CustomMinimumSize = new Vector2(8, 40) };
            Control editor = seat.IsBot
                ? Ui.Options(new[] { "Easy bot", "Medium bot", "Hard bot", "Expert bot" }, (int)seat.BotLevel, level => seat.BotLevel = (BotLevel)level).Expand()
                : Ui.Input(seat.Name, "Name", t => seat.Name = t, 16).Expand();
            var row = Ui.HBox(8, swatch, editor);
            if (i > 0) row.AddChild(Ui.Button("✕", () =>
            {
                _seats.RemoveAt(index);
                RebuildSeats();
            }, ButtonKind.Ghost));
            _seatList.AddChild(row);
        }
    }

    private async void Start()
    {
        var rules = Rules();
        Settings.LastPreset = _preset;
        Settings.LastBoard = _board;
        Settings.Save();
        switch (_mode)
        {
            case SetupMode.Solo:
            case SetupMode.PassAndPlay:
                if (_seats.Count < 2)
                {
                    App.Toast("Add at least one more player", error: true);
                    return;
                }
                if (rules.TeamsEnabled && _seats.Count < 2) return;
                if (await App.Session.StartLocalAsync(_seats, rules, _board)) App.Go(new BoardScreen(), replace: true);
                else App.Toast("The match could not be started", error: true);
                break;
            case SetupMode.HostLan:
            case SetupMode.HostNearby:
                if (await App.Session.HostLanAsync(_roomName, rules, _board, _mode == SetupMode.HostNearby)) App.Go(new LobbyScreen(), replace: true);
                break;
            case SetupMode.OnlinePrivate:
                var created = await App.Session.Api.CreateRoom(_roomName, RulePresets.Ids.Contains(_preset) ? _preset : "classic", _board, 6, false, rules);
                if (!created.Ok)
                {
                    App.Toast(created.Error, error: true);
                    return;
                }
                if (await App.Session.JoinOnlineAsync(created.Text("code"), false)) App.Go(new LobbyScreen(), replace: true);
                break;
        }
    }
}
