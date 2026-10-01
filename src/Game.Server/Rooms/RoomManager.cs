using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Net;
using Game.Protocol;
using Game.Server.Auth;
using Game.Server.Data;

namespace Game.Server.Rooms;

public sealed class ServerOptions
{
    public string TokenSecret { get; set; } = "";
    public string Postgres { get; set; } = "";
    public string Redis { get; set; } = "";
    public string InstanceId { get; set; } = Environment.MachineName;
    public int MaxRooms { get; set; } = 500;
    public double BotDelaySeconds { get; set; } = 0.9;
    public int MatchmakingFillSeconds { get; set; } = 20;
    public int MatchmakingTargetSeats { get; set; } = 4;
    public int IdleRoomSeconds { get; set; } = 120;
    public int AbandonedMatchSeconds { get; set; } = 900;
}

public sealed class ServerRoom
{
    public string Code { get; init; } = "";
    public bool Public { get; init; }
    public bool Managed { get; init; }
    public RoomHost Host { get; init; } = null!;
    public WebSocketRoomTransport Transport { get; init; } = null!;
    public HostSession Session { get; init; } = null!;
    public double CreatedAt { get; init; }
    public double EmptySince { get; set; } = -1;
    public readonly object Gate = new();
}

/// <summary>
/// Owns every room on this instance: creation, matchmaking, the shared pump loop, persistence of
/// the event stream and recovery of unfinished matches after a restart.
/// </summary>
public sealed class RoomManager : BackgroundService, IRoomListener
{
    private readonly ConcurrentDictionary<string, ServerRoom> _rooms = new();
    private readonly Channel<Func<Task>> _writes = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly IStore _store;
    private readonly IPresence _presence;
    private readonly TokenService _tokens;
    private readonly ServerOptions _options;
    private readonly ILogger<RoomManager> _log;
    private readonly Random _random = new();
    private int _pendingWrites;

    public RoomManager(IStore store, IPresence presence, TokenService tokens, ServerOptions options, ILogger<RoomManager> log)
    {
        _store = store;
        _presence = presence;
        _tokens = tokens;
        _options = options;
        _log = log;
    }

    public double Now => _clock.Elapsed.TotalSeconds;
    public int RoomCount => _rooms.Count;
    public IEnumerable<ServerRoom> Rooms => _rooms.Values;
    public int PendingWrites => Volatile.Read(ref _pendingWrites);

    public ServerRoom? Find(string code) => _rooms.GetValueOrDefault(code.ToUpperInvariant());

    private string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        while (true)
        {
            var chars = new char[6];
            lock (_random)
                for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[_random.Next(alphabet.Length)];
            string code = new(chars);
            if (!_rooms.ContainsKey(code)) return code;
        }
    }

    public ServerRoom? Create(string name, GameRules rules, string boardId, int maxSeats, bool isPublic, bool managed, bool ranked, string? code = null)
    {
        if (_rooms.Count >= _options.MaxRooms) return null;
        code ??= NewCode();
        var options = new RoomHostOptions
        {
            RoomCode = code,
            RoomName = name,
            MaxSeats = Math.Clamp(maxSeats, 2, ProtocolInfo.MaxSeats),
            BotDelaySeconds = _options.BotDelaySeconds,
            Rules = rules,
            BoardId = boardId,
            Ranked = ranked,
            ServerManaged = managed,
            AllowLocalPlayers = false,
            Authenticate = token =>
            {
                var claims = _tokens.Validate(token);
                if (claims == null || (ranked && claims.Guest)) return null;
                return (claims.UserId, claims.Name);
            },
        };
        var transport = new WebSocketRoomTransport();
        var session = new HostSession(options, transport);
        session.Room.Listener = this;
        var room = new ServerRoom
        {
            Code = code, Public = isPublic, Managed = managed, Host = session.Room, Transport = transport,
            Session = session, CreatedAt = Now,
        };
        if (!_rooms.TryAdd(code, room)) return null;
        Enqueue(() => _presence.RegisterRoomAsync(code, _options.InstanceId));
        return room;
    }

    /// <summary>Quick or ranked matchmaking: join the fullest waiting room or open a new one.</summary>
    public ServerRoom? FindOrCreateMatch(string preset, bool ranked)
    {
        ServerRoom? best = null;
        int bestSeats = -1;
        foreach (var room in _rooms.Values)
        {
            if (!room.Managed) continue;
            lock (room.Gate)
            {
                var host = room.Host;
                if (host.Engine != null || host.Options.Ranked != ranked || host.Lobby.Rules.PresetId != preset) continue;
                int seats = host.SeatCount;
                if (seats >= host.Options.MaxSeats || seats <= bestSeats) continue;
                best = room;
                bestSeats = seats;
            }
        }
        return best ?? Create(ranked ? "Ranked match" : "Quick match", RulePresets.Get(preset), Core.Board.BoardLibrary.DefaultBoardId,
            _options.MatchmakingTargetSeats, isPublic: false, managed: true, ranked);
    }

    // ------------------------------------------------------------------ loops

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var writer = Task.Run(() => WriteLoop(stop), stop);
        try
        {
            while (!stop.IsCancellationRequested)
            {
                double now = Now;
                foreach (var room in _rooms.Values)
                {
                    try
                    {
                        lock (room.Gate)
                        {
                            room.Session.Pump(now);
                            Manage(room, now);
                        }
                    }
                    catch (Exception e)
                    {
                        _log.LogError(e, "Room {Code} failed and was closed", room.Code);
                        Remove(room, "Internal error");
                    }
                }
                await Task.Delay(40, stop);
            }
        }
        catch (OperationCanceledException)
        {
        }
        _writes.Writer.TryComplete();
        await writer;
    }

    private void Manage(ServerRoom room, double now)
    {
        var host = room.Host;
        if (room.Managed && host.Engine == null && host.SeatCount > 0)
        {
            bool full = host.SeatCount >= host.Options.MaxSeats;
            bool waitedLongEnough = now - room.CreatedAt >= _options.MatchmakingFillSeconds;
            if ((full || host.SeatCount >= 2) && host.AllHumansReady && (full || waitedLongEnough))
            {
                host.StartByAuthority();
            }
            else if (waitedLongEnough && !host.Options.Ranked)
            {
                while (host.SeatCount < Math.Min(3, host.Options.MaxSeats) && host.AddBotSeat(BotLevel.Hard)) { }
                host.StartByAuthority();
            }
        }

        if (host.PeerCount > 0)
        {
            room.EmptySince = -1;
            return;
        }
        if (room.EmptySince < 0) room.EmptySince = now;
        double limit = host.InMatch ? _options.AbandonedMatchSeconds : _options.IdleRoomSeconds;
        if (now - room.EmptySince < limit) return;
        if (host.InMatch && host.Config != null)
        {
            string matchId = host.Config.MatchId;
            Enqueue(() => _store.AbandonMatchAsync(matchId));
        }
        Remove(room, "Room closed");
    }

    private void Remove(ServerRoom room, string reason)
    {
        if (!_rooms.TryRemove(room.Code, out _)) return;
        try
        {
            room.Host.Close(reason);
        }
        catch (Exception)
        {
        }
        _ = room.Transport.StopAsync();
        Enqueue(() => _presence.UnregisterRoomAsync(room.Code));
    }

    private void Enqueue(Func<Task> write)
    {
        if (_writes.Writer.TryWrite(write)) Interlocked.Increment(ref _pendingWrites);
    }

    /// <summary>Database writes run in order on one background task so rooms never block on IO.</summary>
    private async Task WriteLoop(CancellationToken stop)
    {
        await foreach (var write in _writes.Reader.ReadAllAsync(CancellationToken.None))
        {
            try
            {
                await write();
            }
            catch (Exception e)
            {
                _log.LogError(e, "Persistence write failed");
            }
            finally
            {
                Interlocked.Decrement(ref _pendingWrites);
            }
        }
    }

    /// <summary>Waits until everything queued so far has been written (tests and shutdown).</summary>
    public async Task FlushAsync()
    {
        var done = new TaskCompletionSource();
        Enqueue(() =>
        {
            done.SetResult();
            return Task.CompletedTask;
        });
        await done.Task;
    }

    // ------------------------------------------------------------------ recovery

    public async Task RecoverAsync()
    {
        foreach (var active in await _store.LoadActiveMatchesAsync())
        {
            try
            {
                var save = RoomSave.FromJson(active.SnapshotJson);
                save.Events = active.Events;
                // Events newer than the snapshot cannot be re-derived without the RNG; drop them
                // and let play continue from the snapshot (clients resync automatically).
                save.Events.RemoveAll(e => e.Version > save.State.Version);
                await _store.TruncateEventsAsync(active.Match.MatchId, save.State.Version);
                var room = Create(save.Lobby.RoomName, save.Config.Rules, save.Board.BoardId, Math.Max(2, save.Lobby.Seats.Count),
                    isPublic: false, managed: false, save.Lobby.Ranked, active.Match.RoomCode);
                if (room == null) continue;
                lock (room.Gate) room.Host.Restore(save, allowSeatClaims: false);
                _log.LogInformation("Recovered match {Match} in room {Room} at version {Version}",
                    active.Match.MatchId, room.Code, save.State.Version);
            }
            catch (Exception e)
            {
                _log.LogError(e, "Could not recover match {Match}", active.Match.MatchId);
                await _store.AbandonMatchAsync(active.Match.MatchId);
            }
        }
    }

    // ------------------------------------------------------------------ IRoomListener

    public void MatchStarted(RoomHost room, MatchConfig config)
    {
        var lobby = room.Lobby;
        var record = new MatchRecord
        {
            MatchId = config.MatchId, RoomCode = room.RoomCode, BoardId = config.BoardId, Preset = config.Rules.PresetId,
            Ranked = room.Options.Ranked,
            Players = lobby.Seats.Select(s => new MatchPlayerRecord { Seat = s.Seat, UserId = s.UserId, Name = s.Name, IsBot = s.IsBot }).ToList(),
        };
        Enqueue(() => _store.SaveMatchStartAsync(record));
    }

    public void EventsCommitted(RoomHost room, IReadOnlyList<GameEvent> events)
    {
        if (room.Config == null || room.Engine == null) return;
        string matchId = room.Config.MatchId;
        var copy = events.ToList();
        var save = room.Save()!;
        save.Events = new List<GameEvent>();
        int version = save.State.Version;
        string json = save.ToJson();
        Enqueue(async () =>
        {
            await _store.AppendEventsAsync(matchId, copy);
            await _store.SaveSnapshotAsync(matchId, version, json);
        });
    }

    public void MatchEnded(RoomHost room)
    {
        if (room.Config == null || room.Engine == null) return;
        var state = room.Engine.State.Clone();
        state.Board = room.Engine.Board;
        var lobby = room.Lobby;
        string matchId = room.Config.MatchId;
        bool ranked = room.Options.Ranked;
        string preset = room.Config.Rules.PresetId;
        Enqueue(() => RecordResultsAsync(matchId, room.RoomCode, preset, ranked, state, lobby));
    }

    private async Task RecordResultsAsync(string matchId, string roomCode, string preset, bool ranked, Core.State.GameState state, LobbyInfo lobby)
    {
        var record = new MatchRecord
        {
            MatchId = matchId, RoomCode = roomCode, BoardId = state.BoardId, Preset = preset, Ranked = ranked,
            Turns = state.TurnNumber, EndedAt = DateTime.UtcNow,
        };
        var users = new Dictionary<int, UserRecord>();
        foreach (var standing in state.Standings)
        {
            var seat = lobby.Seats.FirstOrDefault(s => s.Seat == standing.Player);
            var player = state.Players[standing.Player];
            record.Players.Add(new MatchPlayerRecord
            {
                Seat = standing.Player, UserId = seat?.UserId ?? "", Name = player.Name,
                IsBot = seat == null || (seat.IsBot && seat.UserId.Length == 0), Rank = standing.Rank, NetWorth = standing.NetWorth,
            });
            if (seat is { UserId.Length: > 0 })
            {
                var user = await _store.GetUserAsync(seat.UserId);
                if (user != null) users[standing.Player] = user;
            }
        }

        if (ranked && users.Count >= 2)
        {
            var entries = record.Players.Where(p => users.ContainsKey(p.Seat)).ToList();
            int[] changes = Ratings.Changes(entries.Select(p => (users[p.Seat].Rating, p.Rank)).ToList());
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].RatingChange = changes[i];
                var user = users[entries[i].Seat];
                user.Rating = Math.Max(100, user.Rating + changes[i]);
                user.Stats.RankedGames++;
            }
        }

        foreach (var (seat, user) in users)
        {
            var player = state.Players[seat];
            var standing = state.Standings.First(s => s.Player == seat);
            bool won = state.Winners.Contains(seat);
            Ratings.Accumulate(user.Stats, state, player, standing.Rank, won);
            await _store.UpdateUserAsync(user);
            foreach (string id in Achievements.Earned(user, player, won, standing.NetWorth))
                await _store.GrantAchievementAsync(user.Id, id);
            await _store.AddRecentPlayersAsync(user.Id, users.Values.Where(u => u.Id != user.Id).Select(u => u.Id));
        }
        await _store.CompleteMatchAsync(record);
    }
}
