using System.Collections.Concurrent;
using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.Serialization;
using Game.Core.State;
using Game.Net.Transport;
using Game.Protocol;

namespace Game.Net;

public enum ClientStatus
{
    Disconnected,
    Connecting,
    Lobby,
    InMatch,
}

/// <summary>
/// A player's (or spectator's) end of a room. Keeps a replica of the match by applying the
/// authority's events, verifies it against the authority's state hash and heals itself when
/// packets are lost, duplicated or reordered. Not thread safe: call <see cref="Poll"/> and every
/// other member from one thread; transport callbacks are queued internally.
/// </summary>
public sealed class RoomClient
{
    private sealed class PendingCommand
    {
        public GameCommand Command = null!;
        public double SentAt;
        public int Attempts;
    }

    private readonly IGameTransport _transport;
    private readonly ConcurrentQueue<byte[]> _inbox = new();
    private readonly ConcurrentQueue<bool> _linkEvents = new();
    private readonly Dictionary<string, PendingCommand> _pending = new();
    private readonly SortedDictionary<int, EventBatch> _stash = new();
    private readonly HashSet<string> _seenMessages = new();
    private readonly Queue<string> _seenOrder = new();
    private readonly string _clientSeed = Guid.NewGuid().ToString("N");
    private long _sequence;
    private double _now;
    private double _gapSince = -1;
    private double _lastResync = -100;
    private double _lastHeard;

    public string PlayerName { get; set; }
    public string AuthToken { get; set; } = "";
    public PeerRole Role { get; set; } = PeerRole.Player;
    public int PreferredToken { get; set; }
    public double ResendSeconds { get; set; } = 2.5;

    public ClientStatus Status { get; private set; } = ClientStatus.Disconnected;
    public string PeerId { get; private set; } = "";
    public string ResumeToken { get; set; } = "";
    public int[] Seats { get; private set; } = Array.Empty<int>();
    public LobbyInfo? Lobby { get; private set; }
    public GameState? State { get; private set; }
    public BoardDefinition? Board { get; private set; }
    /// <summary>Every event applied since the last snapshot; used for local replays and host migration.</summary>
    public List<GameEvent> EventLog { get; } = new();
    public GameState? InitialState { get; private set; }
    public int TimerSeat { get; private set; } = -1;
    public int TimerSeconds { get; private set; }
    public int ResyncCount { get; private set; }
    public double RoundTripSeconds { get; private set; }
    public bool IsHost => Lobby != null && Lobby.HostPeerId == PeerId;
    public bool IsLinkUp => _transport.IsConnected;
    public double SecondsSinceHeard => _now - _lastHeard;

    public event Action? LobbyChanged;
    public event Action<GameState>? SnapshotLoaded;
    public event Action<IReadOnlyList<GameEvent>>? EventsApplied;
    public event Action<string, string>? CommandFailed;
    public event Action<Reject>? Rejected;
    public event Action<int, int>? ChatReceived;
    public event Action<PeerStatus>? PeerStatusChanged;
    public event Action<string>? Disconnected;
    public event Action? Connected;
    /// <summary>The host started a rematch: the previous match is gone and the lobby is open again.</summary>
    public event Action? ReturnedToLobby;

    public RoomClient(IGameTransport transport, string playerName)
    {
        _transport = transport;
        PlayerName = playerName;
        transport.PacketReceived += p => _inbox.Enqueue(p);
        transport.PeerConnected += _ => _linkEvents.Enqueue(true);
        transport.PeerDisconnected += _ => _linkEvents.Enqueue(false);
    }

    public bool Controls(int seat) => Array.IndexOf(Seats, seat) >= 0;

    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        Status = ClientStatus.Connecting;
        await _transport.ConnectAsync(cancel);
        SendHello();
    }

    private void SendHello() => Send(new Hello(
        ProtocolInfo.Version, PlayerName, Role, AuthToken, ResumeToken, _clientSeed, State?.Version ?? 0, PreferredToken));

    public Task DisconnectAsync()
    {
        Status = ClientStatus.Disconnected;
        return _transport.DisconnectAsync();
    }

    public void Send(NetMessage message)
    {
        if (!_transport.IsConnected) return;
        _ = _transport.SendAsync(PacketCodec.Encode(message, Lobby?.RoomCode ?? "", ++_sequence));
    }

    /// <summary>Sends a command stamped with the replica's version; it is retried until answered.</summary>
    public GameCommand SendCommand(GameCommand command)
    {
        command = command with { MatchId = State?.MatchId ?? "", ExpectedVersion = State?.Version ?? -1 };
        _pending[command.CommandId] = new PendingCommand { Command = command, SentAt = _now, Attempts = 1 };
        Send(new SubmitCommand(command));
        return command;
    }

    // Lobby shortcuts ---------------------------------------------------------------------

    public void SetReady(bool ready) => Send(new SetReady(ready));

    public void UpdateSettings(GameRules? rules, string? boardId, string? roomName = null) =>
        Send(new UpdateSettings(rules, boardId, roomName));

    public void AddBot(BotLevel level) => Send(new AddBot(level));

    public void AddLocalPlayer(string name) => Send(new AddLocalPlayer(name));

    public void Kick(int seat) => Send(new KickPlayer(seat));

    public void StartMatch() => Send(new StartMatch());

    public void Chat(int presetId) => Send(new SendChat(presetId));

    // Pump --------------------------------------------------------------------------------

    public void Poll(double now)
    {
        _now = now;
        while (_linkEvents.TryDequeue(out bool up))
        {
            if (up)
            {
                Connected?.Invoke();
            }
            else
            {
                Status = ClientStatus.Disconnected;
                Disconnected?.Invoke("Connection lost");
            }
        }
        while (_inbox.TryDequeue(out var packet))
        {
            NetMessage message;
            NetworkEnvelope envelope;
            try
            {
                message = PacketCodec.Decode(packet, out envelope);
            }
            catch (ProtocolException)
            {
                continue;
            }
            if (!_seenMessages.Add(envelope.MessageId)) continue;
            _seenOrder.Enqueue(envelope.MessageId);
            while (_seenOrder.Count > 256) _seenMessages.Remove(_seenOrder.Dequeue());
            _lastHeard = now;
            Handle(message);
        }

        if (State != null && _stash.Count > 0 && _gapSince >= 0 && now - _gapSince > 1.0) RequestResync();

        foreach (var p in _pending.Values.ToList())
        {
            if (now - p.SentAt < ResendSeconds) continue;
            if (p.Attempts >= 6)
            {
                _pending.Remove(p.Command.CommandId);
                CommandFailed?.Invoke(p.Command.CommandId, "No answer from the host");
                continue;
            }
            p.Attempts++;
            p.SentAt = now;
            Send(new SubmitCommand(p.Command));
        }
    }

    private void RequestResync()
    {
        if (_now - _lastResync < 1.0) return;
        _lastResync = _now;
        ResyncCount++;
        _stash.Clear();
        _gapSince = -1;
        Send(new ResyncRequest(State?.Version ?? 0));
    }

    private void Handle(NetMessage message)
    {
        switch (message)
        {
            case Welcome w:
                PeerId = w.PeerId;
                if (!ResumeToken.StartsWith("seat:") || w.ResumeToken.Length > 0) ResumeToken = w.ResumeToken;
                Seats = w.Seats;
                if (Status != ClientStatus.InMatch) Status = ClientStatus.Lobby;
                break;

            case Reject r:
                Status = ClientStatus.Disconnected;
                Rejected?.Invoke(r);
                break;

            case LobbyState l:
                if (Lobby != null && l.Lobby.Revision < Lobby.Revision) break;
                Lobby = l.Lobby;
                Seats = l.Lobby.Seats.Where(s => s.PeerId == PeerId && PeerId.Length > 0).Select(s => s.Seat).ToArray();
                if (State == null) Status = ClientStatus.Lobby;
                LobbyChanged?.Invoke();
                break;

            case Snapshot snap:
                if (snap.Board != null) Board = snap.Board;
                Board ??= BoardLibrary.Get(snap.State.BoardId);
                snap.State.Board = Board;
                bool newMatch = State == null || State.MatchId != snap.State.MatchId;
                State = snap.State;
                Seats = snap.Seats;
                if (newMatch || InitialState == null)
                {
                    InitialState = snap.State.Clone();
                    EventLog.Clear();
                    _pending.Clear();
                }
                else
                {
                    // Mid-match resync: the local log has a hole, so restart it from here.
                    InitialState = snap.State.Clone();
                    EventLog.Clear();
                }
                _stash.Clear();
                _gapSince = -1;
                Status = ClientStatus.InMatch;
                SnapshotLoaded?.Invoke(State);
                break;

            case EventBatch batch:
                OnBatch(batch);
                break;

            case CommandRejected rejected:
                _pending.Remove(rejected.CommandId);
                CommandFailed?.Invoke(rejected.CommandId, rejected.Reason);
                break;

            case Heartbeat hb:
                TimerSeat = hb.TimerSeat;
                TimerSeconds = hb.TimerSeconds;
                if (State != null && hb.StateHash.Length > 0)
                {
                    if (hb.StateVersion > State.Version)
                    {
                        if (_gapSince < 0) _gapSince = _now;
                        if (_now - _gapSince > 1.0) RequestResync();
                    }
                    else if (hb.StateVersion == State.Version && hb.StateHash != StateHasher.Hash(State))
                    {
                        RequestResync();
                    }
                    else if (hb.StateVersion == State.Version)
                    {
                        _gapSince = -1;
                    }
                }
                else if (State != null && hb.StateHash.Length == 0 && Lobby is { InMatch: false })
                {
                    LeaveMatch();
                    ReturnedToLobby?.Invoke();
                }
                break;

            case PeerStatus status:
                PeerStatusChanged?.Invoke(status);
                break;

            case ChatMessage chat:
                ChatReceived?.Invoke(chat.Seat, chat.PresetId);
                break;

            case Pong pong:
                RoundTripSeconds = Math.Max(0, _now - pong.ClientTime / 1000.0);
                break;

            case RoomClosed closed:
                Status = ClientStatus.Disconnected;
                Disconnected?.Invoke(closed.Reason);
                break;
        }
    }

    /// <summary>Drops the replica after a rematch returned the room to the lobby.</summary>
    public void LeaveMatch()
    {
        State = null;
        InitialState = null;
        EventLog.Clear();
        _pending.Clear();
        _stash.Clear();
        Status = ClientStatus.Lobby;
    }

    private void OnBatch(EventBatch batch)
    {
        if (State == null || batch.Events.Count == 0) return;
        if (batch.CommandId.Length > 0) _pending.Remove(batch.CommandId);
        int last = batch.Events[^1].Version;
        if (last <= State.Version) return;
        if (batch.FromVersion > State.Version + 1)
        {
            _stash[batch.FromVersion] = batch;
            if (_gapSince < 0) _gapSince = _now;
            return;
        }
        Apply(batch);
        while (_stash.Count > 0 && State != null)
        {
            var next = _stash.First();
            if (next.Key > State.Version + 1) break;
            _stash.Remove(next.Key);
            if (next.Value.Events[^1].Version > State.Version) Apply(next.Value);
        }
        _gapSince = _stash.Count > 0 ? (_gapSince < 0 ? _now : _gapSince) : -1;
    }

    private void Apply(EventBatch batch)
    {
        var applied = new List<GameEvent>(batch.Events.Count);
        foreach (var e in batch.Events)
        {
            if (e.Version <= State!.Version) continue;
            e.Apply(State);
            State.Version = e.Version;
            EventLog.Add(e);
            applied.Add(e);
        }
        if (applied.Count == 0) return;
        EventsApplied?.Invoke(applied);
        // Only the newest batch carries a hash that matches the replica's version.
        if (State!.Version == batch.Events[^1].Version && batch.StateHash.Length > 0 && _stash.Count == 0 &&
            StateHasher.Hash(State) != batch.StateHash)
            RequestResync();
    }

    public void SendPing() => Send(new Ping((long)(_now * 1000)));
}
