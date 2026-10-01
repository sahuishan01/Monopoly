using Game.Core.Board;
using Game.Core.Events;
using Game.Core.Replay;
using Game.Core.Rules;
using Game.Net;
using Game.Net.Transport;
using Game.Protocol;
using Godot;

namespace BoardEmpire.Net;

public enum SessionKind
{
    None,
    Local,
    LanHost,
    LanClient,
    NearbyHost,
    NearbyClient,
    Online,
}

public sealed class LocalSeat
{
    public string Name { get; set; } = "Player";
    public bool IsBot { get; set; }
    public BotLevel BotLevel { get; set; } = BotLevel.Medium;
}

/// <summary>
/// Owns the connection for whatever mode is being played. Solo and pass-and-play run a
/// <see cref="RoomHost"/> in-process over the loopback transport; LAN and Nearby add a real
/// transport next to it; joining a remote room only needs a <see cref="RoomClient"/>.
/// The rest of the client talks to <see cref="Client"/> and never asks which mode is active.
/// </summary>
public partial class GameSession : Node, IRoomListener
{
    private const string SaveDir = "user://saves";
    private const string SavePath = "user://saves/current.json";
    private const string ReplayDir = "user://replays";

    private LoopbackHub? _loopback;
    private LanHostTransport? _lan;
    private LanDiscovery? _migrationSearch;
    private bool _dirty;
    private double _lastSave;
    private double _clock;
    private bool _leaving;
    private bool _migrating;
    private double _migrationStarted;
    private double _nextReconnect;
    private int _reconnectAttempts;
    private bool _replaySaved;

    public SessionKind Kind { get; private set; }
    public HostSession? Host { get; private set; }
    public RoomClient? Client { get; private set; }
    public NearbyBridge Nearby { get; } = new();
    public OnlineApi Api { get; } = new();
    public string PlayerName { get; set; } = "Player";
    public int LanPort => _lan?.Port ?? 0;
    public bool IsAuthority => Host != null;
    public double Now => _clock;

    /// <summary>Raised whenever <see cref="Client"/> is replaced (new session, host migration).</summary>
    public event Action? ClientChanged;
    public event Action<string>? Notice;
    public event Action<string>? Lost;

    public override void _Ready()
    {
        // A player whose connection dropped mid-match is let back in without ceremony.
        Nearby.ConnectionInitiated += (endpointId, _, _, incoming) =>
        {
            if (incoming && Kind == SessionKind.NearbyHost && Host is { Room.InMatch: true }) Nearby.Accept(endpointId);
        };
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        Host?.Pump(_clock);
        Client?.Poll(_clock);
        if (_dirty && _clock - _lastSave > 2.0) WriteSave();
        if (_migrating) ContinueMigration();
        else if (Client != null && !_leaving && !Client.IsLinkUp && Kind == SessionKind.Online && Client.State != null) TryReconnect();
    }

    // ------------------------------------------------------------------ starting sessions

    private RoomHostOptions HostOptions(string roomName, GameRules rules, string boardId, bool sameDevice) => new()
    {
        RoomCode = NewCode(),
        RoomName = roomName,
        MaxSeats = ProtocolInfo.MaxSeats,
        Rules = rules,
        BoardId = boardId,
        BotDelaySeconds = 0.9,
        // Players sharing one screen cannot answer simultaneously, so no auction clock there.
        RapidAuctionSeconds = sameDevice ? 0 : 20,
        TradeAnswerSeconds = sameDevice ? 0 : 40,
        ReconnectSeconds = 60,
    };

    private static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        return new string(Enumerable.Range(0, 5).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
    }

    private void Flush(int rounds = 3)
    {
        for (int i = 0; i < rounds; i++)
        {
            Host?.Pump(_clock);
            Client?.Poll(_clock);
        }
    }

    private void AttachClient(RoomClient client)
    {
        Client = client;
        _replaySaved = false;
        client.Disconnected += reason => OnDisconnected(client, reason);
        client.EventsApplied += events =>
        {
            if (events.Any(e => e is GameEnded)) SaveReplay();
        };
        ClientChanged?.Invoke();
    }

    /// <summary>Solo or pass-and-play: builds the room and starts the match immediately.</summary>
    public async Task<bool> StartLocalAsync(List<LocalSeat> seats, GameRules rules, string boardId)
    {
        await LeaveAsync();
        var options = HostOptions("Local game", rules, boardId, sameDevice: true);
        _loopback = new LoopbackHub();
        Host = new HostSession(options, _loopback);
        Host.Room.Listener = this;
        await Host.StartAsync();
        Kind = SessionKind.Local;

        var humans = seats.Where(s => !s.IsBot).ToList();
        var client = new RoomClient(_loopback.CreateClient("local"), humans.Count > 0 ? humans[0].Name : PlayerName)
        {
            // An all-bot table is simply watched.
            Role = humans.Count > 0 ? PeerRole.Player : PeerRole.Spectator,
        };
        await client.ConnectAsync();
        AttachClient(client);
        Flush();
        foreach (var extra in humans.Skip(1)) client.AddLocalPlayer(extra.Name);
        Flush();
        foreach (var bot in seats.Where(s => s.IsBot)) Host.Room.AddBotSeat(bot.BotLevel);
        string? error = Host.Room.StartByAuthority();
        if (error != null) Notice?.Invoke(error);
        Flush();
        return client.State != null;
    }

    public async Task<bool> HostLanAsync(string roomName, GameRules rules, string boardId, bool nearby)
    {
        await LeaveAsync();
        var options = HostOptions(roomName, rules, boardId, sameDevice: false);
        _loopback = new LoopbackHub();
        Host = new HostSession(options, _loopback);
        Host.Room.Listener = this;
        if (nearby && NearbyBridge.Available)
        {
            Host.Attach(new NearbyHostTransport(Nearby, $"{roomName}|{options.RoomCode}"));
            Kind = SessionKind.NearbyHost;
        }
        else
        {
            _lan = new LanHostTransport(LanHostTransport.DefaultPort, Describe);
            Host.Attach(_lan);
            Kind = SessionKind.LanHost;
        }
        try
        {
            await Host.StartAsync();
        }
        catch (Exception e)
        {
            Notice?.Invoke("Could not open the room: " + e.Message);
            await LeaveAsync();
            return false;
        }
        var client = new RoomClient(_loopback.CreateClient("local"), PlayerName);
        await client.ConnectAsync();
        AttachClient(client);
        Flush();
        return true;
    }

    private LanAdvertisement Describe()
    {
        var room = Host?.Room;
        var lobby = room?.Lobby;
        return new LanAdvertisement
        {
            RoomCode = lobby?.RoomCode ?? "", RoomName = lobby?.RoomName ?? "", HostName = PlayerName,
            Players = lobby?.Seats.Count ?? 0, MaxPlayers = room?.Options.MaxSeats ?? 0, InMatch = lobby?.InMatch ?? false,
            ProtocolVersion = ProtocolInfo.Version, Preset = lobby?.Rules.PresetName ?? "", Board = lobby?.BoardId ?? "",
        };
    }

    public Task<bool> JoinLanAsync(string address, int port) =>
        JoinAsync(SessionKind.LanClient, new LanClientTransport(address, port), "", PeerRole.Player);

    public Task<bool> JoinNearbyAsync(string endpointId) =>
        JoinAsync(SessionKind.NearbyClient, new NearbyClientTransport(Nearby, endpointId, PlayerName), "", PeerRole.Player);

    public Task<bool> JoinOnlineAsync(string roomCode, bool spectate) =>
        JoinAsync(SessionKind.Online, new WebSocketClientTransport(Api.SocketUri(roomCode)), Api.Token,
            spectate ? PeerRole.Spectator : PeerRole.Player);

    private async Task<bool> JoinAsync(SessionKind kind, IGameTransport transport, string authToken, PeerRole role, string resumeToken = "")
    {
        if (!_migrating) await LeaveAsync();
        Kind = kind;
        var client = new RoomClient(transport, PlayerName) { AuthToken = authToken, Role = role, ResumeToken = resumeToken };
        try
        {
            await client.ConnectAsync();
        }
        catch (Exception e)
        {
            Notice?.Invoke("Could not connect: " + e.Message);
            Kind = SessionKind.None;
            return false;
        }
        AttachClient(client);
        return true;
    }

    public async Task LeaveAsync()
    {
        _leaving = true;
        _migrating = false;
        _migrationSearch?.Dispose();
        _migrationSearch = null;
        if (_dirty) WriteSave();
        if (Client != null)
        {
            try
            {
                Client.Send(new LeaveRoom());
                await Client.DisconnectAsync();
            }
            catch (Exception)
            {
            }
        }
        if (Host != null)
        {
            try
            {
                Host.Room.Close("The host left");
                await Host.DisposeAsync();
            }
            catch (Exception)
            {
            }
        }
        if (Kind is SessionKind.NearbyClient or SessionKind.NearbyHost) Nearby.StopAll();
        Client = null;
        Host = null;
        _loopback = null;
        _lan = null;
        Kind = SessionKind.None;
        _leaving = false;
        ClientChanged?.Invoke();
    }

    // ------------------------------------------------------------------ connection loss

    private void OnDisconnected(RoomClient client, string reason)
    {
        if (_leaving || client != Client) return;
        bool inMatch = client.State is { IsOver: false };
        if (inMatch && Kind is SessionKind.LanClient or SessionKind.NearbyClient && client.Lobby != null)
        {
            BeginMigration();
            return;
        }
        if (inMatch && Kind == SessionKind.Online)
        {
            _reconnectAttempts = 0;
            _nextReconnect = _clock + 1.5;
            Notice?.Invoke("Connection lost. Reconnecting…");
            return;
        }
        Lost?.Invoke(reason);
    }

    private async void TryReconnect()
    {
        if (_clock < _nextReconnect || Client == null) return;
        _nextReconnect = _clock + 3;
        if (++_reconnectAttempts > 25)
        {
            Lost?.Invoke("Could not reconnect to the server");
            _nextReconnect = double.MaxValue;
            return;
        }
        try
        {
            await Client.ConnectAsync();
            Notice?.Invoke("Reconnected");
            _reconnectAttempts = 0;
        }
        catch (Exception)
        {
        }
    }

    // ------------------------------------------------------------------ host migration (LAN / Nearby)

    private RoomClient? _orphan;

    private void BeginMigration()
    {
        _orphan = Client;
        _migrating = true;
        _migrationStarted = _clock;
        var lobby = _orphan!.Lobby!;
        if (HostMigration.ShouldBecomeHost(lobby, _orphan.Seats))
        {
            Notice?.Invoke("The host left. You are the new host.");
            _ = BecomeHostAsync();
        }
        else
        {
            Notice?.Invoke("The host left. Looking for the new host…");
            if (Kind == SessionKind.LanClient)
            {
                _migrationSearch = new LanDiscovery();
                _migrationSearch.Start();
            }
            else
            {
                Nearby.StartDiscovery();
                Nearby.EndpointFound += OnMigrationEndpoint;
            }
        }
    }

    private async Task BecomeHostAsync()
    {
        var old = _orphan!;
        var lobby = HostMigration.LobbyForNewHost(old.Lobby!);
        var save = RoomHost.SaveFromReplica(old.State!, old.Board!, lobby, old.EventLog, (ulong)Random.Shared.NextInt64());
        var options = HostOptions(lobby.RoomName, save.Config.Rules, save.Board.BoardId, sameDevice: false);
        options.RoomCode = lobby.RoomCode;
        _loopback = new LoopbackHub();
        Host = new HostSession(options, _loopback);
        Host.Room.Listener = this;
        if (Kind == SessionKind.NearbyClient)
        {
            Host.Attach(new NearbyHostTransport(Nearby, $"{lobby.RoomName}|{lobby.RoomCode}"));
            Kind = SessionKind.NearbyHost;
        }
        else
        {
            _lan = new LanHostTransport(LanHostTransport.DefaultPort, Describe);
            Host.Attach(_lan);
            Kind = SessionKind.LanHost;
        }
        Host.Room.Restore(save, allowSeatClaims: true);
        await Host.StartAsync();

        var client = new RoomClient(_loopback.CreateClient("local"), PlayerName) { ResumeToken = HostMigration.SeatClaim(old.Seats) };
        await client.ConnectAsync();
        _migrating = false;
        AttachClient(client);
        Flush();
        ReportPrivate(client, old);
        Flush();
        _orphan = null;
    }

    private static void ReportPrivate(RoomClient client, RoomClient old)
    {
        if (old.State == null) return;
        foreach (int seat in old.Seats)
        {
            var ids = old.State.Players[seat].Objectives.Where(o => !o.Completed).Select(o => o.Id).ToArray();
            if (ids.Length > 0) client.Send(new ReportPrivateState(seat, ids));
        }
    }

    private void OnMigrationEndpoint(NearbyEndpoint endpoint)
    {
        if (!_migrating || _orphan?.Lobby == null) return;
        if (!endpoint.Name.EndsWith("|" + _orphan.Lobby.RoomCode)) return;
        Nearby.EndpointFound -= OnMigrationEndpoint;
        _ = RejoinAsync(new NearbyClientTransport(Nearby, endpoint.Id, PlayerName), SessionKind.NearbyClient);
    }

    private void ContinueMigration()
    {
        if (_orphan?.Lobby == null) return;
        if (_clock - _migrationStarted > 30)
        {
            _migrating = false;
            _migrationSearch?.Dispose();
            _migrationSearch = null;
            Lost?.Invoke("The match could not be continued");
            return;
        }
        if (_migrationSearch == null) return;
        var room = _migrationSearch.Rooms.FirstOrDefault(r => r.RoomCode == _orphan.Lobby.RoomCode);
        if (room == null) return;
        _migrationSearch.Dispose();
        _migrationSearch = null;
        _ = RejoinAsync(new LanClientTransport(room.Address, room.Port), SessionKind.LanClient);
    }

    private async Task RejoinAsync(IGameTransport transport, SessionKind kind)
    {
        var old = _orphan!;
        bool ok = await JoinAsync(kind, transport, "", PeerRole.Player, HostMigration.SeatClaim(old.Seats));
        _migrating = false;
        if (!ok)
        {
            Lost?.Invoke("The match could not be continued");
            return;
        }
        ReportPrivate(Client!, old);
        _orphan = null;
        Notice?.Invoke("Reconnected to the new host");
    }

    // ------------------------------------------------------------------ save / resume / replays

    public bool HasSave => Godot.FileAccess.FileExists(SavePath);

    void IRoomListener.EventsCommitted(RoomHost room, IReadOnlyList<GameEvent> events) => _dirty = true;

    void IRoomListener.MatchEnded(RoomHost room)
    {
        _dirty = false;
        DeleteSave();
    }

    private void WriteSave()
    {
        _dirty = false;
        _lastSave = _clock;
        var save = Host?.Room.Save();
        if (save == null || save.State.IsOver) return;
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(SaveDir);
            using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
            file?.StoreString(save.ToJson());
        }
        catch (Exception e)
        {
            GD.PushWarning("Autosave failed: " + e.Message);
        }
    }

    public void DeleteSave()
    {
        if (HasSave) DirAccess.RemoveAbsolute(SavePath);
    }

    /// <summary>Continue the last unfinished match hosted on this device.</summary>
    public async Task<bool> ResumeAsync()
    {
        RoomSave save;
        try
        {
            using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
            save = RoomSave.FromJson(file.GetAsText());
        }
        catch (Exception e)
        {
            Notice?.Invoke("The saved match could not be read");
            GD.PushWarning(e.Message);
            DeleteSave();
            return false;
        }
        await LeaveAsync();
        var options = HostOptions(save.Lobby.RoomName, save.Config.Rules, save.Board.BoardId, sameDevice: true);
        options.RoomCode = save.Lobby.RoomCode.Length > 0 ? save.Lobby.RoomCode : options.RoomCode;
        _loopback = new LoopbackHub();
        Host = new HostSession(options, _loopback);
        Host.Room.Listener = this;
        Host.Room.Restore(save, allowSeatClaims: true);
        await Host.StartAsync();
        Kind = SessionKind.Local;
        var client = new RoomClient(_loopback.CreateClient("local"), PlayerName) { ResumeToken = "seat:all" };
        await client.ConnectAsync();
        AttachClient(client);
        Flush();
        return client.State != null;
    }

    private void SaveReplay()
    {
        if (_replaySaved || Client?.State == null || Client.InitialState == null || Client.Board == null) return;
        _replaySaved = true;
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(ReplayDir);
            var replay = new ReplayFile { Board = Client.Board, Initial = Client.InitialState, Events = Client.EventLog.ToList() };
            string name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Client.State.MatchId}.json";
            using var file = Godot.FileAccess.Open($"{ReplayDir}/{name}", Godot.FileAccess.ModeFlags.Write);
            file?.StoreString(replay.ToJson());
            // Keep the twenty most recent replays.
            var files = ListReplays();
            foreach (string old in files.Skip(20)) DirAccess.RemoveAbsolute($"{ReplayDir}/{old}");
        }
        catch (Exception e)
        {
            GD.PushWarning("Replay could not be saved: " + e.Message);
        }
    }

    public static List<string> ListReplays()
    {
        var list = new List<string>();
        using var dir = DirAccess.Open(ReplayDir);
        if (dir == null) return list;
        foreach (string file in dir.GetFiles())
            if (file.EndsWith(".json")) list.Add(file);
        list.Sort(StringComparer.Ordinal);
        list.Reverse();
        return list;
    }

    public static ReplayFile? LoadReplay(string name)
    {
        try
        {
            using var file = Godot.FileAccess.Open($"{ReplayDir}/{name}", Godot.FileAccess.ModeFlags.Read);
            return ReplayFile.FromJson(file.GetAsText());
        }
        catch (Exception)
        {
            return null;
        }
    }
}
