using Game.Core.AI;
using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Engine;
using Game.Core.Events;
using Game.Core.Rng;
using Game.Core.Rules;
using Game.Core.Serialization;
using Game.Core.State;
using Game.Protocol;

namespace Game.Net;

public sealed class RoomHostOptions
{
    public string RoomCode { get; set; } = "LOCAL";
    public string RoomName { get; set; } = "BoardEmpire";
    public int MaxSeats { get; set; } = 6;
    /// <summary>Pause between bot actions so humans can follow; 0 runs bots instantly.</summary>
    public double BotDelaySeconds { get; set; } = 0.8;
    public int ReconnectSeconds { get; set; } = 60;
    public int RapidAuctionSeconds { get; set; } = 15;
    public int TradeAnswerSeconds { get; set; } = 30;
    public double HeartbeatSeconds { get; set; } = 2;
    public bool ReplaceDisconnectedWithBot { get; set; } = true;
    public BotLevel ReplacementLevel { get; set; } = BotLevel.Medium;
    public bool AllowLocalPlayers { get; set; } = true;
    public bool Ranked { get; set; }
    /// <summary>Matchmaking rooms: no player is the host; the server adds bots and starts the match.</summary>
    public bool ServerManaged { get; set; }
    /// <summary>Fixed seed for tests; otherwise the seed combines every client's contribution.</summary>
    public ulong? FixedSeed { get; set; }
    /// <summary>Online play: maps an auth token to (userId, displayName); null rejects the peer.</summary>
    public Func<string, (string UserId, string Name)?>? Authenticate { get; set; }
    public GameRules Rules { get; set; } = new();
    public string BoardId { get; set; } = BoardLibrary.DefaultBoardId;
}

/// <summary>Persistence and bookkeeping hooks; every call happens on the room's thread.</summary>
public interface IRoomListener
{
    void MatchStarted(RoomHost room, MatchConfig config) { }

    void EventsCommitted(RoomHost room, IReadOnlyList<GameEvent> events) { }

    void MatchEnded(RoomHost room) { }

    void LobbyChanged(RoomHost room) { }
}

/// <summary>
/// The authority for one room: lobby, match, bots, timers and reconnection. It is transport
/// agnostic and single threaded; the same class runs on a phone hosting a local game and on the
/// dedicated server.
/// </summary>
public sealed class RoomHost
{
    private sealed class Peer
    {
        public string Id = "";
        public string Name = "";
        public PeerRole Role;
        public string ResumeToken = "";
        public string ClientSeed = "";
        public string UserId = "";
        public long Sequence;
        public readonly List<int> Seats = new();
    }

    private sealed class SeatState
    {
        public SeatInfo Info = new();
        public string ResumeToken = "";
        public string ClientSeed = "";
        public double ReconnectDeadline = double.MaxValue;
        public bool BotTakeover;
        public bool Forfeit;
    }

    private readonly RoomHostOptions _options;
    private readonly Action<string, byte[]> _send;
    private readonly Action<string>? _drop;
    private readonly Dictionary<string, Peer> _peers = new();
    private readonly List<SeatState> _seats = new();
    private readonly Dictionary<string, string?> _recentCommands = new();
    private readonly Queue<string> _recentOrder = new();
    private readonly Dictionary<int, (string Key, double Deadline)> _deadlines = new();
    private readonly Random _entropy = new();

    private BotDriver? _bots;
    private BoardDefinition? _customBoard;
    private string _stateHash = "";
    private double _now;
    private double _nextBotAt;
    private double _nextHeartbeat;
    private int _matchCounter;
    private int _revision;
    private string _hostPeer = "";
    private string _roomName;
    private string _boardId;
    private GameRules _rules;
    private bool _allowSeatClaims;

    public IRoomListener? Listener { get; set; }
    public GameEngine? Engine { get; private set; }
    public MatchConfig? Config { get; private set; }
    public GameState? InitialState { get; private set; }
    public List<GameEvent> EventLog { get; } = new();
    public string RoomCode => _options.RoomCode;
    public RoomHostOptions Options => _options;
    public bool InMatch => Engine != null && !Engine.State.IsOver;
    public int ConnectedHumans => _peers.Values.Count(p => p.Role == PeerRole.Player);
    public int PeerCount => _peers.Count;
    public int SeatCount => _seats.Count;
    public bool AllHumansReady => _seats.All(s => s.Info.IsBot || s.Info.Ready);
    public double Now => _now;
    public double LastActivity { get; private set; }

    public RoomHost(RoomHostOptions options, Action<string, byte[]> send, Action<string>? dropPeer = null)
    {
        _options = options;
        _send = send;
        _drop = dropPeer;
        _roomName = options.RoomName;
        _boardId = options.BoardId;
        _rules = options.Rules.Clone();
    }

    // ------------------------------------------------------------------ lobby view

    public LobbyInfo Lobby => new()
    {
        RoomCode = _options.RoomCode,
        RoomName = _roomName,
        Revision = _revision,
        HostPeerId = _hostPeer,
        BoardId = _boardId,
        Rules = _rules,
        Seats = _seats.Select(s => s.Info.Clone()).ToList(),
        Spectators = _peers.Values.Count(p => p.Role == PeerRole.Spectator),
        InMatch = InMatch,
        Ranked = _options.Ranked,
    };

    private BoardDefinition Board => _customBoard ?? BoardLibrary.Get(_boardId);

    private void LobbyChanged()
    {
        _revision++;
        Broadcast(new LobbyState(Lobby));
        Listener?.LobbyChanged(this);
    }

    private void Reindex()
    {
        for (int i = 0; i < _seats.Count; i++) _seats[i].Info.Seat = i;
        foreach (var p in _peers.Values)
        {
            p.Seats.Clear();
            foreach (var s in _seats)
                if (!s.Info.IsBot && s.Info.PeerId == p.Id) p.Seats.Add(s.Info.Seat);
        }
    }

    private int NextFreeToken()
    {
        for (int t = 0; t < 16; t++)
            if (_seats.All(s => s.Info.Token != t)) return t;
        return 0;
    }

    // ------------------------------------------------------------------ transport entry points

    public void PeerConnected(string peerId) => LastActivity = _now;

    public void PeerDisconnected(string peerId)
    {
        if (!_peers.Remove(peerId, out var peer)) return;
        LastActivity = _now;
        if (peer.Role == PeerRole.Spectator)
        {
            LobbyChanged();
            return;
        }
        if (Engine == null || Engine.State.IsOver)
        {
            _seats.RemoveAll(s => !s.Info.IsBot && s.Info.PeerId == peerId);
            Reindex();
            if (_hostPeer == peerId) _hostPeer = _peers.Values.FirstOrDefault(p => p.Role == PeerRole.Player && p.Seats.Count > 0)?.Id ?? "";
            LobbyChanged();
            return;
        }
        foreach (var seat in _seats.Where(s => s.Info.PeerId == peerId && !s.Info.IsBot))
        {
            seat.Info.Connected = false;
            seat.Info.PeerId = "";
            seat.ReconnectDeadline = _now + _options.ReconnectSeconds;
            Broadcast(new PeerStatus(seat.Info.Seat, false, _options.ReconnectSeconds));
        }
        if (_hostPeer == peerId) _hostPeer = _peers.Values.FirstOrDefault(p => p.Role == PeerRole.Player && p.Seats.Count > 0)?.Id ?? "";
        LobbyChanged();
    }

    public void Receive(string peerId, byte[] packet)
    {
        NetMessage message;
        try
        {
            message = PacketCodec.Decode(packet, out _);
        }
        catch (ProtocolException)
        {
            return;
        }
        Receive(peerId, message);
    }

    public void Receive(string peerId, NetMessage message)
    {
        LastActivity = _now;
        if (message is Hello hello)
        {
            OnHello(peerId, hello);
            return;
        }
        if (!_peers.TryGetValue(peerId, out var peer)) return;
        switch (message)
        {
            case Ping ping:
                Send(peer, new Pong(ping.ClientTime));
                break;
            case SubmitCommand submit:
                OnCommand(peer, submit.Command);
                break;
            case ResyncRequest:
                SendSnapshot(peer);
                break;
            case SendChat chat:
                if (ChatPresets.IsValid(chat.PresetId) && peer.Seats.Count > 0)
                    Broadcast(new ChatMessage(peer.Seats[0], chat.PresetId));
                break;
            case ReportPrivateState report:
                OnPrivateState(peer, report);
                break;
            case LeaveRoom:
                _drop?.Invoke(peerId);
                PeerDisconnected(peerId);
                break;
            default:
                OnLobbyMessage(peer, message);
                break;
        }
    }

    // ------------------------------------------------------------------ joining

    private void OnHello(string peerId, Hello hello)
    {
        var reject = VersionCheck.Validate(hello.ProtocolVersion);
        if (reject != null)
        {
            Refuse(peerId, reject);
            return;
        }
        if (_peers.ContainsKey(peerId)) return;

        string userId = "";
        string name = Sanitize(hello.PlayerName);
        if (_options.Authenticate != null)
        {
            var auth = _options.Authenticate(hello.AuthToken);
            if (auth == null)
            {
                Refuse(peerId, new Reject(RejectCode.BadToken, "Sign in again", ProtocolInfo.MinimumSupported));
                return;
            }
            userId = auth.Value.UserId;
            name = Sanitize(auth.Value.Name);
        }

        // One connection per account: a second device takes over from the first.
        if (userId.Length > 0)
        {
            var old = _peers.Values.FirstOrDefault(p => p.UserId == userId);
            if (old != null)
            {
                _drop?.Invoke(old.Id);
                PeerDisconnected(old.Id);
            }
        }

        var peer = new Peer
        {
            Id = peerId, Name = name, Role = hello.Role, ClientSeed = hello.ClientSeed, UserId = userId,
            ResumeToken = Guid.NewGuid().ToString("N"),
        };

        if (hello.Role == PeerRole.Spectator)
        {
            _peers[peerId] = peer;
            Send(peer, new Welcome(ProtocolInfo.Version, peerId, peer.ResumeToken, Array.Empty<int>(), PeerRole.Spectator));
            LobbyChanged();
            if (Engine != null) SendSnapshot(peer);
            return;
        }

        // A returning player reclaims the seats that are waiting for them.
        var claimed = _allowSeatClaims ? ParseSeatClaim(hello.ResumeToken) : null;
        var reclaim = _seats.Where(s => (!s.Info.Connected && !s.Info.IsBot) || s.BotTakeover)
            .Where(s => (hello.ResumeToken.Length > 0 && s.ResumeToken == hello.ResumeToken) ||
                        (userId.Length > 0 && s.Info.UserId == userId) ||
                        (claimed != null && (claimed.Count == 0 || claimed.Contains(s.Info.Seat))))
            .ToList();
        if (reclaim.Count > 0)
        {
            _peers[peerId] = peer;
            peer.ResumeToken = hello.ResumeToken.StartsWith("seat:") ? peer.ResumeToken : hello.ResumeToken;
            foreach (var seat in reclaim)
            {
                seat.Info.PeerId = peerId;
                seat.Info.Connected = true;
                seat.ResumeToken = peer.ResumeToken;
                seat.ReconnectDeadline = double.MaxValue;
                if (seat.BotTakeover)
                {
                    seat.BotTakeover = false;
                    seat.Info.IsBot = false;
                    _bots?.Remove(seat.Info.Seat);
                    if (Engine != null) Commit(Engine.SetBotControl(seat.Info.Seat, false), "");
                }
                Broadcast(new PeerStatus(seat.Info.Seat, true, 0));
            }
            Reindex();
            if (_hostPeer.Length == 0 && !_options.ServerManaged) _hostPeer = peerId;
            Send(peer, new Welcome(ProtocolInfo.Version, peerId, peer.ResumeToken, peer.Seats.ToArray(), PeerRole.Player));
            LobbyChanged();
            if (Engine != null) SendCatchUp(peer, hello.LastVersion);
            return;
        }

        if (Engine != null)
        {
            Refuse(peerId, new Reject(RejectCode.MatchInProgress, "The match has already started", ProtocolInfo.MinimumSupported));
            return;
        }
        if (_seats.Count >= _options.MaxSeats)
        {
            Refuse(peerId, new Reject(RejectCode.RoomFull, "The room is full", ProtocolInfo.MinimumSupported));
            return;
        }

        _peers[peerId] = peer;
        if (_hostPeer.Length == 0 && !_options.ServerManaged) _hostPeer = peerId;
        _seats.Add(new SeatState
        {
            Info = new SeatInfo
            {
                Name = UniqueName(name), PeerId = peerId, Token = _seats.Any(s => s.Info.Token == hello.Token) ? NextFreeToken() : hello.Token,
                UserId = userId, Team = _seats.Count % 2,
            },
            ResumeToken = peer.ResumeToken,
            ClientSeed = hello.ClientSeed,
        });
        Reindex();
        Send(peer, new Welcome(ProtocolInfo.Version, peerId, peer.ResumeToken, peer.Seats.ToArray(), PeerRole.Player));
        LobbyChanged();
    }

    private void Refuse(string peerId, Reject reject)
    {
        _send(peerId, PacketCodec.Encode(reject, _options.RoomCode, 0));
        _drop?.Invoke(peerId);
    }

    /// <summary>"seat:all" claims every waiting seat (empty set); "seat:1,3" claims those seats.</summary>
    private static HashSet<int>? ParseSeatClaim(string token)
    {
        if (!token.StartsWith("seat:")) return null;
        var set = new HashSet<int>();
        string rest = token[5..];
        if (rest == "all") return set;
        foreach (string part in rest.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part, out int seat)) set.Add(seat);
        return set.Count > 0 ? set : null;
    }

    private static string Sanitize(string name)
    {
        name = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0) name = "Player";
        return name.Length > 16 ? name[..16] : name;
    }

    private string UniqueName(string name)
    {
        if (_seats.All(s => s.Info.Name != name)) return name;
        for (int i = 2; i < 20; i++)
            if (_seats.All(s => s.Info.Name != $"{name} {i}")) return $"{name} {i}";
        return name;
    }

    // ------------------------------------------------------------------ lobby

    private void OnLobbyMessage(Peer peer, NetMessage message)
    {
        bool isHost = peer.Id == _hostPeer;
        bool matchOver = Engine != null && Engine.State.IsOver;
        if (message is RequestRematch)
        {
            if (isHost && matchOver) ReturnToLobby();
            return;
        }
        if (Engine != null) return;

        switch (message)
        {
            case SetReady ready:
                foreach (int own in peer.Seats) _seats[own].Info.Ready = ready.Ready;
                LobbyChanged();
                break;

            case UpdateSettings settings when isHost:
                if (settings.Rules != null) _rules = settings.Rules.Clone();
                if (settings.BoardId != null && BoardLibrary.BoardIds.Contains(settings.BoardId))
                {
                    _boardId = settings.BoardId;
                    _customBoard = null;
                }
                if (!string.IsNullOrWhiteSpace(settings.RoomName)) _roomName = Sanitize(settings.RoomName);
                foreach (var s in _seats.Where(s => !s.Info.IsBot && s.Info.PeerId != _hostPeer)) s.Info.Ready = false;
                LobbyChanged();
                break;

            case AddBot bot when isHost:
                AddBotSeat(bot.Level);
                break;

            case AddLocalPlayer local when _options.AllowLocalPlayers && _seats.Count < _options.MaxSeats:
                _seats.Add(new SeatState
                {
                    Info = new SeatInfo
                    {
                        Name = UniqueName(Sanitize(local.Name)), PeerId = peer.Id, Token = NextFreeToken(),
                        Team = _seats.Count % 2, UserId = "",
                    },
                    ResumeToken = peer.ResumeToken,
                });
                Reindex();
                LobbyChanged();
                break;

            case KickPlayer kick when kick.Seat >= 0 && kick.Seat < _seats.Count:
                var target = _seats[kick.Seat];
                bool ownExtra = target.Info.PeerId == peer.Id && peer.Seats.Count > 1;
                if (!isHost && !ownExtra) break;
                string kickedPeer = target.Info.PeerId;
                _seats.RemoveAt(kick.Seat);
                Reindex();
                if (kickedPeer.Length > 0 && kickedPeer != peer.Id && _peers.TryGetValue(kickedPeer, out var kicked) && kicked.Seats.Count == 0)
                {
                    Send(kicked, new Reject(RejectCode.Kicked, "You were removed from the room", ProtocolInfo.MinimumSupported));
                    _peers.Remove(kickedPeer);
                    _drop?.Invoke(kickedPeer);
                }
                LobbyChanged();
                break;

            case UpdateSeat update when update.Seat >= 0 && update.Seat < _seats.Count:
                var seat = _seats[update.Seat];
                bool mine = seat.Info.PeerId == peer.Id;
                if (!mine && !(isHost && seat.Info.IsBot) && !(isHost && update.Team != null)) break;
                if (update.Name != null && (mine || seat.Info.IsBot)) seat.Info.Name = UniqueName(Sanitize(update.Name));
                if (update.Token is { } token && _seats.All(s => s == seat || s.Info.Token != token)) seat.Info.Token = token;
                if (update.Team is { } team) seat.Info.Team = Math.Clamp(team, 0, 3);
                if (update.Ability is { } ability) seat.Info.Ability = ability;
                if (update.BotLevel is { } level && seat.Info.IsBot && isHost) seat.Info.BotLevel = level;
                LobbyChanged();
                break;

            case StartMatch when isHost:
                TryStartMatch(peer);
                break;
        }
    }

    /// <summary>Host only, lobby only: play on a board that is not part of the built-in library.</summary>
    public void SetCustomBoard(BoardDefinition board)
    {
        board.Validate();
        _customBoard = board;
        _boardId = board.BoardId;
        LobbyChanged();
    }

    private void TryStartMatch(Peer host)
    {
        string? error = StartMatchCore(requireReady: true);
        if (error != null) Send(host, new CommandRejected("", error));
    }

    /// <summary>Server-managed rooms: add a bot seat while in the lobby.</summary>
    public bool AddBotSeat(BotLevel level)
    {
        if (Engine != null || _seats.Count >= _options.MaxSeats) return false;
        string[] names = { "Ada", "Babbage", "Curie", "Darwin", "Euler", "Faraday", "Gauss", "Hopper" };
        _seats.Add(new SeatState
        {
            Info = new SeatInfo
            {
                Name = UniqueName(names[_seats.Count % names.Length]), IsBot = true, BotLevel = level,
                Ready = true, Token = NextFreeToken(), Team = _seats.Count % 2,
            },
        });
        Reindex();
        LobbyChanged();
        return true;
    }

    /// <summary>Server-managed rooms: start without a host peer. Returns the reason on failure.</summary>
    public string? StartByAuthority(bool requireReady = false) => Engine != null ? "Already started" : StartMatchCore(requireReady);

    private string? StartMatchCore(bool requireReady)
    {
        if (_seats.Count < 2) return "At least two players are needed";
        if (requireReady && _seats.Any(s => !s.Info.IsBot && !s.Info.Ready && s.Info.PeerId != _hostPeer)) return "Not everyone is ready";
        if (_rules.TeamsEnabled && _seats.Select(s => s.Info.Team).Distinct().Count() < 2) return "Team games need two teams";

        string matchId = $"{_options.RoomCode}-{++_matchCounter}";
        var seeds = _seats.Select(s => s.ClientSeed).Append(_entropy.NextInt64().ToString());
        var config = new MatchConfig
        {
            MatchId = matchId,
            Seed = _options.FixedSeed ?? FairSeed.Combine(seeds, matchId),
            BoardId = _boardId,
            Rules = _rules.Clone(),
        };
        foreach (var s in _seats)
        {
            config.Players.Add(new PlayerSetup
            {
                Name = s.Info.Name, Token = s.Info.Token, IsBot = s.Info.IsBot, BotLevel = s.Info.BotLevel,
                Team = s.Info.Team, Ability = s.Info.Ability,
            });
        }

        Config = config;
        Engine = GameEngine.Create(config, Board);
        InitialState = Engine.State.RedactedFor(-1);
        EventLog.Clear();
        _recentCommands.Clear();
        _recentOrder.Clear();
        _deadlines.Clear();
        _bots = new BotDriver(config.Seed ^ 0x9E3779B97F4A7C15UL);
        foreach (var s in _seats.Where(s => s.Info.IsBot)) _bots.SetBot(s.Info.Seat, s.Info.BotLevel);

        var start = Engine.Start();
        EventLog.AddRange(start);
        _stateHash = StateHasher.Hash(Engine.State);
        Listener?.MatchStarted(this, config);
        Listener?.EventsCommitted(this, start);
        LobbyChanged();
        foreach (var p in _peers.Values) SendSnapshot(p);
        _nextBotAt = _now + _options.BotDelaySeconds;
        RefreshDeadlines();
        return null;
    }

    private void ReturnToLobby()
    {
        Engine = null;
        Config = null;
        _bots = null;
        _deadlines.Clear();
        _seats.RemoveAll(s => s.BotTakeover || (!s.Info.IsBot && !s.Info.Connected));
        foreach (var s in _seats) s.Info.Ready = s.Info.IsBot;
        Reindex();
        LobbyChanged();
    }

    // ------------------------------------------------------------------ match

    private void OnCommand(Peer peer, GameCommand command)
    {
        if (Engine == null)
        {
            Send(peer, new CommandRejected(command.CommandId, "No match in progress"));
            return;
        }
        if (_recentCommands.TryGetValue(command.CommandId, out var previous))
        {
            // Duplicate delivery: accepted commands are already in the event stream.
            if (previous != null) Send(peer, new CommandRejected(command.CommandId, previous));
            return;
        }
        string? error = null;
        if (!peer.Seats.Contains(command.PlayerId)) error = "That seat is not yours";
        else if (_seats[command.PlayerId].BotTakeover) error = "A bot is playing this seat";

        CommandResult? result = null;
        if (error == null)
        {
            result = Engine.Execute(command);
            if (!result.Ok) error = result.Error;
        }
        Remember(command.CommandId, error);
        if (error != null)
        {
            Send(peer, new CommandRejected(command.CommandId, error));
            return;
        }
        Commit(result!.Events, command.CommandId);
    }

    private void Remember(string commandId, string? error)
    {
        _recentCommands[commandId] = error;
        _recentOrder.Enqueue(commandId);
        while (_recentOrder.Count > 512) _recentCommands.Remove(_recentOrder.Dequeue());
    }

    private void Commit(IReadOnlyList<GameEvent> events, string commandId)
    {
        if (events.Count == 0 || Engine == null) return;
        EventLog.AddRange(events);
        _stateHash = StateHasher.Hash(Engine.State);
        int from = events[0].Version;
        foreach (var p in _peers.Values)
        {
            var visible = events.Select(e => e.RedactFor(p.Seats)).ToList();
            Send(p, new EventBatch(from, visible, _stateHash, commandId));
        }
        Listener?.EventsCommitted(this, events);
        RefreshDeadlines();
        if (Engine.State.IsOver)
        {
            _deadlines.Clear();
            Listener?.MatchEnded(this);
            LobbyChanged();
        }
    }

    private void SendSnapshot(Peer peer)
    {
        if (Engine == null) return;
        var view = Engine.State.RedactedFor(peer.Seats);
        Send(peer, new Snapshot(view, Engine.Board, _stateHash, peer.Seats.ToArray()));
    }

    /// <summary>Reconnect: replay only the missed events when the client still has its replica.</summary>
    private void SendCatchUp(Peer peer, int lastVersion)
    {
        if (Engine == null) return;
        int first = EventLog.Count > 0 ? EventLog[0].Version : int.MaxValue;
        if (lastVersion <= 0 || lastVersion < first - 1 || lastVersion > Engine.State.Version)
        {
            SendSnapshot(peer);
            return;
        }
        var missed = EventLog.Where(e => e.Version > lastVersion).Select(e => e.RedactFor(peer.Seats)).ToList();
        if (missed.Count == 0)
        {
            Send(peer, new Heartbeat(Engine.State.Version, _stateHash, _revision, -1, 0));
            return;
        }
        Send(peer, new EventBatch(missed[0].Version, missed, _stateHash, ""));
    }

    private void OnPrivateState(Peer peer, ReportPrivateState report)
    {
        if (!_allowSeatClaims || Engine == null || !peer.Seats.Contains(report.Seat)) return;
        var objectives = Engine.State.Players[report.Seat].Objectives;
        int next = 0;
        foreach (var o in objectives)
        {
            if (o.Completed || o.Id >= 0) continue;
            while (next < report.ObjectiveIds.Length && objectives.Any(x => x.Id == report.ObjectiveIds[next])) next++;
            if (next >= report.ObjectiveIds.Length) break;
            int id = report.ObjectiveIds[next++];
            if (id >= 0 && id < ObjectiveCatalog.All.Length) o.Id = id;
        }
    }

    // ------------------------------------------------------------------ clock

    public void Tick(double now)
    {
        _now = now;
        if (Engine != null && !Engine.State.IsOver)
        {
            ExpireReconnects();
            RunBots();
            RunTimeouts();
        }
        if (now >= _nextHeartbeat)
        {
            _nextHeartbeat = now + _options.HeartbeatSeconds;
            var (seat, seconds) = NextDeadline();
            int version = Engine?.State.Version ?? 0;
            Broadcast(new Heartbeat(version, Engine != null ? _stateHash : "", _revision, seat, seconds));
        }
    }

    private void ExpireReconnects()
    {
        foreach (var seat in _seats)
        {
            if (seat.Forfeit)
            {
                Forfeit(seat);
                continue;
            }
            if (seat.Info.Connected || seat.Info.IsBot || _now < seat.ReconnectDeadline) continue;
            seat.ReconnectDeadline = double.MaxValue;
            if (!_options.ReplaceDisconnectedWithBot)
            {
                seat.Forfeit = true;
                Forfeit(seat);
                continue;
            }
            seat.BotTakeover = true;
            seat.Info.IsBot = true;
            seat.Info.BotLevel = _options.ReplacementLevel;
            _bots!.SetBot(seat.Info.Seat, _options.ReplacementLevel);
            Commit(Engine!.SetBotControl(seat.Info.Seat, true), "");
            LobbyChanged();
        }
    }

    /// <summary>An absent player without a replacement resigns as soon as the rules allow it.</summary>
    private void Forfeit(SeatState seat)
    {
        if (Engine == null || Engine.State.IsOver) return;
        int id = seat.Info.Seat;
        if (Engine.State.Players[id].Bankrupt)
        {
            seat.Forfeit = false;
            return;
        }
        var result = Engine.Execute(new DeclareBankruptcyCommand(id));
        if (!result.Ok)
        {
            if (!TurnInfo.PendingActors(Engine.State).Contains(id)) return;
            var fallback = BotBrain.TimeoutAction(Engine.State, id);
            if (fallback == null) return;
            result = Engine.Execute(fallback);
            if (!result.Ok) return;
        }
        Commit(result.Events, "");
    }

    private void RunBots()
    {
        if (_bots == null) return;
        int budget = _options.BotDelaySeconds <= 0 ? 5000 : 1;
        while (budget-- > 0 && Engine != null && !Engine.State.IsOver && _now >= _nextBotAt)
        {
            var command = _bots.Next(Engine.State);
            if (command == null) return;
            var result = Engine.Execute(command);
            if (!result.Ok)
            {
                var fallback = BotBrain.TimeoutAction(Engine.State, command.PlayerId);
                if (fallback == null) return;
                result = Engine.Execute(fallback);
                if (!result.Ok) return;
            }
            bool quick = Engine.State.Phase == TurnPhase.Auction;
            _nextBotAt = _now + (quick ? _options.BotDelaySeconds / 2 : _options.BotDelaySeconds);
            Commit(result.Events, "");
        }
    }

    private bool IsHumanSeat(int seat) => seat >= 0 && seat < _seats.Count && !_seats[seat].Info.IsBot;

    private void RefreshDeadlines()
    {
        if (Engine == null || Engine.State.IsOver)
        {
            _deadlines.Clear();
            return;
        }
        var s = Engine.State;
        var live = new HashSet<int>();
        foreach (int seat in TurnInfo.PendingActors(s))
        {
            if (!IsHumanSeat(seat)) continue;
            bool onlyTrade = s.Phase != TurnPhase.Auction && TurnInfo.BlockingActor(s) != seat;
            int seconds = s.Phase == TurnPhase.Auction && s.Auction?.Mode == AuctionMode.Rapid
                ? _options.RapidAuctionSeconds
                : onlyTrade
                    ? _options.TradeAnswerSeconds
                    : s.Rules.TurnTimeSeconds;
            if (seconds <= 0) continue;
            live.Add(seat);
            string key = $"{s.TurnNumber}:{s.Phase}:{onlyTrade}:{(s.Phase == TurnPhase.Auction ? s.Auction!.Tile : -1)}";
            if (!_deadlines.TryGetValue(seat, out var current) || current.Key != key)
                _deadlines[seat] = (key, _now + seconds);
        }
        foreach (int seat in _deadlines.Keys.Where(k => !live.Contains(k)).ToList()) _deadlines.Remove(seat);
    }

    private (int Seat, int Seconds) NextDeadline()
    {
        if (_deadlines.Count == 0) return (-1, 0);
        var first = _deadlines.OrderBy(d => d.Value.Deadline).First();
        return (first.Key, Math.Max(0, (int)Math.Ceiling(first.Value.Deadline - _now)));
    }

    private void RunTimeouts()
    {
        foreach (var (seat, entry) in _deadlines.ToList())
        {
            if (_now < entry.Deadline || Engine == null || Engine.State.IsOver) continue;
            _deadlines.Remove(seat);
            var command = BotBrain.TimeoutAction(Engine.State, seat);
            if (command == null) continue;
            var result = Engine.Execute(command);
            if (result.Ok) Commit(result.Events, "");
        }
    }

    // ------------------------------------------------------------------ sending

    private void Send(Peer peer, NetMessage message) =>
        _send(peer.Id, PacketCodec.Encode(message, _options.RoomCode, ++peer.Sequence));

    private void Broadcast(NetMessage message)
    {
        foreach (var p in _peers.Values) Send(p, message);
    }

    public void Close(string reason)
    {
        Broadcast(new RoomClosed(reason));
        foreach (string id in _peers.Keys.ToList()) _drop?.Invoke(id);
        _peers.Clear();
    }

    // ------------------------------------------------------------------ persistence / migration

    /// <summary>Authority-only snapshot (includes the RNG) for save files and server recovery.</summary>
    public RoomSave? Save() => Engine == null || Config == null
        ? null
        : new RoomSave
        {
            Config = Config, Board = Engine.Board, State = Engine.State.Clone(), Initial = InitialState!,
            Events = EventLog.ToList(), Lobby = Lobby,
        };

    /// <summary>Restores a saved match. Human seats wait for their players to reconnect.</summary>
    public void Restore(RoomSave save, bool allowSeatClaims)
    {
        Config = save.Config;
        _rules = save.Config.Rules;
        _boardId = save.Board.BoardId;
        _customBoard = BoardLibrary.BoardIds.Contains(save.Board.BoardId) ? null : save.Board;
        save.State.Board = save.Board;
        Engine = GameEngine.Resume(save.State.Clone(), save.Board);
        InitialState = save.Initial;
        EventLog.Clear();
        EventLog.AddRange(save.Events);
        _stateHash = StateHasher.Hash(Engine.State);
        _bots = new BotDriver(save.Config.Seed ^ 0x9E3779B97F4A7C15UL);
        _seats.Clear();
        foreach (var info in save.Lobby.Seats)
        {
            var seat = new SeatState { Info = info.Clone() };
            if (seat.Info.IsBot)
            {
                _bots.SetBot(seat.Info.Seat, seat.Info.BotLevel);
            }
            else
            {
                seat.Info.PeerId = "";
                seat.Info.Connected = false;
                seat.ReconnectDeadline = _now + _options.ReconnectSeconds;
            }
            _seats.Add(seat);
        }
        _allowSeatClaims = allowSeatClaims;
        _roomName = save.Lobby.RoomName;
        _revision = save.Lobby.Revision + 1;
        RefreshDeadlines();
    }

    /// <summary>
    /// Host migration: a former client becomes the authority using its replica. Unknown private
    /// data is dropped (sealed bids are re-collected, objectives are re-reported by their owners)
    /// and the dice stream is reseeded.
    /// </summary>
    public static RoomSave SaveFromReplica(GameState replica, BoardDefinition board, LobbyInfo lobby, IEnumerable<GameEvent> log, ulong newSeed)
    {
        var state = replica.Clone();
        state.Board = board;
        state.Rng = Pcg32.Seed(newSeed);
        state.Auction?.SealedBids.RemoveAll(b => b.Amount < 0);
        var config = new MatchConfig { MatchId = state.MatchId, Seed = newSeed, BoardId = board.BoardId, Rules = state.Rules };
        foreach (var p in state.Players)
            config.Players.Add(new PlayerSetup { Name = p.Name, Token = p.Token, IsBot = p.IsBot, Team = p.Team, Ability = p.Ability });
        return new RoomSave
        {
            Config = config, Board = board, State = state, Initial = state.RedactedFor(-1), Events = new List<GameEvent>(), Lobby = lobby,
        };
    }
}

/// <summary>Everything an authority needs to continue a match after a restart.</summary>
public sealed class RoomSave
{
    public MatchConfig Config { get; set; } = new();
    public BoardDefinition Board { get; set; } = new();
    /// <summary>Full authority state including the RNG.</summary>
    public GameState State { get; set; } = new();
    public GameState Initial { get; set; } = new();
    public List<GameEvent> Events { get; set; } = new();
    public LobbyInfo Lobby { get; set; } = new();

    public string ToJson() => CoreJson.Serialize(this);

    public static RoomSave FromJson(string json)
    {
        var save = CoreJson.Deserialize<RoomSave>(json);
        save.State.Board = save.Board;
        save.Initial.Board = save.Board;
        return save;
    }
}
