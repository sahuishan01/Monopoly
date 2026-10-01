using BoardEmpire.Core;
using BoardEmpire.Net;
using Game.Core.Board;
using Game.Core.Rules;
using Game.Net;
using Game.Net.Transport;
using Game.Protocol;
using Godot;

namespace BoardEmpire.UI;

/// <summary>Room lobby for every networked mode; the host edits settings, everyone readies up.</summary>
public partial class LobbyScreen : Screen
{
    private RoomClient? _client;
    private VBoxContainer _seats = null!;
    private VBoxContainer _info = null!;
    private HBoxContainer _buttons = null!;
    private bool _ready;
    private bool _leaving;

    public override Screen Recreate() => new LobbyScreen();

    protected override void Build()
    {
        _seats = Ui.VBox(8);
        _info = Ui.VBox(8);
        _buttons = Ui.HBox(10);
        var left = Ui.Panel(Ui.VBox(12, Ui.Caption("Players", Tokens.Accent), Ui.Scroll(_seats))).Expand(true, true);
        var right = Ui.VBox(14, Ui.Panel(_info), Ui.Spacer(false), _buttons).Expand(true, true);
        right.SizeFlagsStretchRatio = 0.9f;
        Page("Lobby", Ui.HBox(16, left, right));

        App.Session.ClientChanged += Bind;
        App.Session.Lost += OnLost;
        App.Session.Nearby.ConnectionInitiated += OnNearbyRequest;
        Bind();
    }

    public override void _ExitTree()
    {
        App.Session.ClientChanged -= Bind;
        App.Session.Lost -= OnLost;
        App.Session.Nearby.ConnectionInitiated -= OnNearbyRequest;
        Unbind();
    }

    /// <summary>Nearby host: show the pairing code so both players can confirm it matches.</summary>
    private void OnNearbyRequest(string endpointId, string name, string code, bool incoming)
    {
        if (!incoming || App.Session.Kind != SessionKind.NearbyHost) return;
        Modal? modal = null;
        var content = Ui.VBox(14,
            Ui.Caption($"{name} wants to join", Tokens.Accent),
            Ui.Label(code, 54, Tokens.Text, mono: true, bold: true),
            Ui.Label("Check that their screen shows the same code.", 15, Tokens.Muted),
            Ui.HBox(10,
                Ui.Button("Reject", () =>
                {
                    App.Session.Nearby.Reject(endpointId);
                    modal!.Close();
                }, ButtonKind.Danger).Expand(),
                Ui.Button("Codes match", () =>
                {
                    App.Session.Nearby.Accept(endpointId);
                    modal!.Close();
                }, ButtonKind.Primary).Expand()));
        modal = new Modal(content, null, false, 420);
        AddChild(modal);
    }

    private void Unbind()
    {
        if (_client == null) return;
        _client.LobbyChanged -= Refresh;
        _client.SnapshotLoaded -= OnStarted;
        _client.Rejected -= OnRejected;
        _client.CommandFailed -= OnFailed;
        _client.ChatReceived -= OnChat;
        _client = null;
    }

    private void Bind()
    {
        Unbind();
        _client = App.Session.Client;
        if (_client == null) return;
        _client.LobbyChanged += Refresh;
        _client.SnapshotLoaded += OnStarted;
        _client.Rejected += OnRejected;
        _client.CommandFailed += OnFailed;
        _client.ChatReceived += OnChat;
        if (_client.State is { IsOver: false }) OnStarted(_client.State);
        else Refresh();
    }

    private void OnStarted(Game.Core.State.GameState _)
    {
        if (App.Current == this) App.Go(new BoardScreen(), replace: true);
    }

    private void OnRejected(Reject reject)
    {
        App.Toast(reject.Code == RejectCode.VersionTooOld ? "Update required to join this game" : reject.Reason, error: true);
        _ = LeaveAsync();
    }

    private void OnFailed(string id, string reason) => App.Toast(reason, error: true);

    private void OnChat(int seat, int preset)
    {
        var lobby = _client?.Lobby;
        if (lobby == null || seat >= lobby.Seats.Count || !ChatPresets.IsValid(preset)) return;
        App.Toast($"{lobby.Seats[seat].Name}: {ChatPresets.Lines[preset]}");
    }

    private void OnLost(string reason)
    {
        if (_leaving) return;
        App.Toast(reason, error: true);
        App.Home();
    }

    private async Task LeaveAsync()
    {
        _leaving = true;
        await App.Session.LeaveAsync();
        App.Home();
    }

    public override bool HandleBack()
    {
        _ = LeaveAsync();
        return true;
    }

    private void Refresh()
    {
        var lobby = _client?.Lobby;
        if (lobby == null || !IsInsideTree()) return;
        bool host = _client!.IsHost;

        foreach (var child in _seats.GetChildren()) child.QueueFree();
        foreach (var seat in lobby.Seats)
        {
            var swatch = new ColorRect { Color = Tokens.Player(seat.Seat), CustomMinimumSize = new Vector2(8, 40) };
            bool mine = _client.Controls(seat.Seat);
            var name = Ui.Label(seat.Name + (mine ? "  (you)" : ""), 17, Tokens.Text, bold: mine).Expand();
            var row = Ui.HBox(10, swatch, name);
            if (lobby.Rules.TeamsEnabled)
            {
                int index = seat.Seat, team = seat.Team;
                var teamButton = Ui.Button("TEAM " + (team + 1), () => _client.Send(new UpdateSeat(index, null, null, team == 0 ? 1 : 0, null, null)), ButtonKind.Ghost);
                teamButton.Disabled = !host && !mine;
                row.AddChild(teamButton);
            }
            if (seat.IsBot) row.AddChild(Ui.Chip(seat.BotLevel.ToString().ToUpperInvariant() + " BOT", Tokens.Info));
            else if (!seat.Connected) row.AddChild(Ui.Chip("OFFLINE", Tokens.Bad));
            else if (seat.PeerId == lobby.HostPeerId && lobby.HostPeerId.Length > 0) row.AddChild(Ui.Chip("HOST", Tokens.Accent));
            else row.AddChild(Ui.Chip(seat.Ready ? "READY" : "NOT READY", seat.Ready ? Tokens.Good : Tokens.Muted));
            if (host && !mine)
            {
                int index = seat.Seat;
                row.AddChild(Ui.Button("✕", () => _client.Kick(index), ButtonKind.Ghost));
            }
            _seats.AddChild(row);
        }
        if (lobby.Spectators > 0) _seats.AddChild(Ui.Label($"{lobby.Spectators} spectator(s)", 13, Tokens.Muted));

        foreach (var child in _info.GetChildren()) child.QueueFree();
        _info.AddChild(Ui.Label(lobby.RoomName, 24, Tokens.Text, bold: true));
        string where = App.Session.Kind switch
        {
            SessionKind.LanHost => $"Same Wi-Fi · port {App.Session.LanPort} · code {lobby.RoomCode}",
            SessionKind.LanClient => $"Same Wi-Fi · code {lobby.RoomCode}",
            SessionKind.NearbyHost or SessionKind.NearbyClient => $"Nearby · code {lobby.RoomCode}",
            SessionKind.Online => $"Online · join code {lobby.RoomCode}" + (lobby.Ranked ? " · ranked" : ""),
            _ => lobby.RoomCode,
        };
        _info.AddChild(Ui.Label(where, 14, Tokens.Accent, mono: true));
        var board = BoardLibrary.BoardIds.Contains(lobby.BoardId) ? BoardLibrary.Get(lobby.BoardId).Name : lobby.BoardId;
        _info.AddChild(Ui.Label($"{lobby.Seats.Count} / {ProtocolInfo.MaxSeats} players · {board}", 15, Tokens.Muted));

        if (host)
        {
            var presets = RulePresets.Ids.Select(RulePresets.Get).ToList();
            foreach (var saved in Settings.Presets) presets.Add(saved.Rules.Clone());
            int current = presets.FindIndex(p => p.PresetName == lobby.Rules.PresetName);
            _info.AddChild(Row("Rules", Ui.Options(presets.Select(p => p.PresetName), Math.Max(0, current), i => _client.UpdateSettings(presets[i].Clone(), null))));
            var boards = BoardLibrary.All.ToList();
            _info.AddChild(Row("Board", Ui.Options(boards.Select(b => b.Name), Math.Max(0, boards.FindIndex(b => b.BoardId == lobby.BoardId)),
                i => _client.UpdateSettings(null, boards[i].BoardId))));
        }
        else
        {
            _info.AddChild(Ui.Label($"Rules: {lobby.Rules.PresetName}", 16, Tokens.Text));
        }
        _info.AddChild(Ui.Wrapped(SetupScreen.Describe(lobby.Rules), 13, Tokens.Muted));

        foreach (var child in _buttons.GetChildren()) child.QueueFree();
        _buttons.AddChild(Ui.Button(Loc.T("Leave"), () => _ = LeaveAsync(), ButtonKind.Ghost));
        if (_client.Role == PeerRole.Spectator)
        {
            _buttons.AddChild(Ui.Label("Spectating — waiting for the match to start", 15, Tokens.Muted));
        }
        else if (host)
        {
            _buttons.AddChild(Ui.Button(Loc.T("Add bot"), () => _client.AddBot(BotLevel.Medium)).Expand());
            var start = Ui.Button(Loc.T("Start match"), () => _client.StartMatch(), ButtonKind.Primary).Expand();
            start.Disabled = lobby.Seats.Count < 2;
            _buttons.AddChild(start);
        }
        else
        {
            _ready = _client.Seats.Length > 0 && lobby.Seats.Where(s => _client.Controls(s.Seat)).All(s => s.Ready);
            _buttons.AddChild(Ui.Button(_ready ? "Not ready" : Loc.T("Ready"), () => _client.SetReady(!_ready), _ready ? ButtonKind.Secondary : ButtonKind.Primary).Expand());
        }
    }
}

/// <summary>Rooms discovered on the local network, plus manual entry for stubborn routers.</summary>
public partial class LanBrowserScreen : Screen
{
    private readonly LanDiscovery _discovery = new();
    private VBoxContainer _list = null!;
    private double _sinceRefresh = 10;
    private string _address = "";
    private bool _joining;

    public override Screen Recreate() => new LanBrowserScreen();

    protected override void Build()
    {
        _list = Ui.VBox(8);
        var manual = Ui.HBox(8,
            Ui.Input("", "192.168.1.24 or 192.168.1.24:7777", t => _address = t, 40).Expand(),
            Ui.Button("Join", () => JoinManual(), ButtonKind.Primary));
        var body = Ui.VBox(14,
            Ui.Panel(Ui.VBox(10, Ui.Caption("Rooms on this network", Tokens.Accent), Ui.Scroll(_list).MinSize(0, 260))).Expand(true, true),
            Section("Join by address", manual));
        Page("Same Wi-Fi", body);
        try
        {
            _discovery.Start();
        }
        catch (Exception e)
        {
            App.Toast("Discovery is unavailable: " + e.Message, error: true);
        }
    }

    public override void _ExitTree() => _discovery.Dispose();

    public override void _Process(double delta)
    {
        _sinceRefresh += delta;
        if (_sinceRefresh < 1) return;
        _sinceRefresh = 0;
        foreach (var child in _list.GetChildren()) child.QueueFree();
        var rooms = _discovery.Rooms;
        if (rooms.Count == 0)
        {
            _list.AddChild(Ui.Label("Searching… make sure both devices are on the same Wi-Fi.", 15, Tokens.Muted));
            return;
        }
        foreach (var room in rooms)
        {
            var ad = room;
            bool compatible = ad.ProtocolVersion == ProtocolInfo.Version;
            var join = Ui.Button(ad.InMatch ? "In progress" : compatible ? "Join" : "Update needed", () => Join(ad.Address, ad.Port), ButtonKind.Primary);
            join.Disabled = ad.InMatch || !compatible;
            _list.AddChild(Ui.Panel(Ui.HBox(12,
                Ui.VBox(2, Ui.Label(ad.RoomName, 18, Tokens.Text, bold: true),
                    Ui.Label($"{ad.HostName} · {ad.Players}/{ad.MaxPlayers} · {ad.Preset} · {ad.Address}", 12, Tokens.Muted, mono: true)).Expand(),
                join), Tokens.PanelHi, 12, 12));
        }
    }

    private void JoinManual()
    {
        string text = _address.Trim();
        if (text.Length == 0) return;
        int port = LanHostTransport.DefaultPort;
        int colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out int parsed))
        {
            port = parsed;
            text = text[..colon];
        }
        Join(text, port);
    }

    private async void Join(string address, int port)
    {
        if (_joining) return;
        _joining = true;
        bool ok = await App.Session.JoinLanAsync(address, port);
        _joining = false;
        if (ok && IsInsideTree()) App.Go(new LobbyScreen(), replace: true);
    }
}

/// <summary>Bluetooth / Wi-Fi Direct discovery through Nearby Connections (Android).</summary>
public partial class NearbyScreen : Screen
{
    private readonly Dictionary<string, NearbyEndpoint> _found = new();
    private VBoxContainer _list = null!;
    private Label _status = null!;
    private Modal? _codeDialog;

    public override Screen Recreate() => new NearbyScreen();

    protected override void Build()
    {
        _list = Ui.VBox(8);
        _status = Ui.Label("Searching for nearby games…", 15, Tokens.Muted);
        Page("Nearby", Ui.VBox(14, _status, Ui.Panel(Ui.Scroll(_list)).Expand(true, true)));
        var nearby = App.Session.Nearby;
        nearby.EndpointFound += OnFound;
        nearby.EndpointLost += OnLostEndpoint;
        nearby.ConnectionInitiated += OnInitiated;
        nearby.Status += OnStatus;
        if (!NearbyBridge.Available)
        {
            _status.Text = "Nearby play is only available on Android.";
            return;
        }
        if (!nearby.HasPermissions()) nearby.RequestPermissions();
        nearby.StartDiscovery();
    }

    public override void _ExitTree()
    {
        var nearby = App.Session.Nearby;
        nearby.EndpointFound -= OnFound;
        nearby.EndpointLost -= OnLostEndpoint;
        nearby.ConnectionInitiated -= OnInitiated;
        nearby.Status -= OnStatus;
        nearby.StopDiscovery();
    }

    private void OnStatus(string message) => _status.Text = message;

    private void OnFound(NearbyEndpoint endpoint)
    {
        _found[endpoint.Id] = endpoint;
        Redraw();
    }

    private void OnLostEndpoint(string id)
    {
        _found.Remove(id);
        Redraw();
    }

    private void Redraw()
    {
        foreach (var child in _list.GetChildren()) child.QueueFree();
        foreach (var endpoint in _found.Values)
        {
            var e = endpoint;
            string name = e.Name.Contains('|') ? e.Name[..e.Name.LastIndexOf('|')] : e.Name;
            _list.AddChild(Ui.Panel(Ui.HBox(12, Ui.Label(name, 18, Tokens.Text, bold: true).Expand(),
                Ui.Button("Join", () => Join(e.Id), ButtonKind.Primary)), Tokens.PanelHi, 12, 12));
        }
    }

    private async void Join(string endpointId)
    {
        _status.Text = "Requesting connection…";
        if (await App.Session.JoinNearbyAsync(endpointId) && IsInsideTree())
            App.Go(new LobbyScreen(), replace: true);
    }

    /// <summary>Both devices show the same short code; the players confirm it matches.</summary>
    private void OnInitiated(string endpointId, string name, string code, bool incoming)
    {
        _codeDialog?.Close();
        var content = Ui.VBox(14,
            Ui.Caption("Confirm the code on both devices", Tokens.Accent),
            Ui.Label(code, 54, Tokens.Text, mono: true, bold: true),
            Ui.Label(name.Contains('|') ? name[..name.LastIndexOf('|')] : name, 16, Tokens.Muted),
            Ui.HBox(10,
                Ui.Button("Reject", () =>
                {
                    App.Session.Nearby.Reject(endpointId);
                    _codeDialog?.Close();
                }, ButtonKind.Danger).Expand(),
                Ui.Button("Codes match", () =>
                {
                    App.Session.Nearby.Accept(endpointId);
                    _codeDialog?.Close();
                }, ButtonKind.Primary).Expand()));
        _codeDialog = new Modal(content, null, false, 420);
        AddChild(_codeDialog);
    }
}
