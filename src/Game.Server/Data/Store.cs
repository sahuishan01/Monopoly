using System.Collections.Concurrent;
using Game.Core.Events;

namespace Game.Server.Data;

public sealed class UserStats
{
    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int RankTotal { get; set; }
    public int PropertiesBought { get; set; }
    public int AuctionsWon { get; set; }
    public int TradesProposed { get; set; }
    public int TradesCompleted { get; set; }
    public long RentEarned { get; set; }
    public long RentPaid { get; set; }
    public int LargestPayment { get; set; }
    public int Bankruptcies { get; set; }
    public int BuildingsBuilt { get; set; }
    public int ObjectivesCompleted { get; set; }
    public string MostProfitableProperty { get; set; } = "";
    public int MostProfitableRent { get; set; }
    public int RankedGames { get; set; }
}

public sealed class UserRecord
{
    public string Id { get; set; } = "";
    /// <summary>Empty for guests.</summary>
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsGuest { get; set; }
    public int Rating { get; set; } = 1000;
    public int Avatar { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public UserStats Stats { get; set; } = new();
    public List<string> Unlocks { get; set; } = new();
}

public sealed class MatchPlayerRecord
{
    public string UserId { get; set; } = "";
    public int Seat { get; set; }
    public string Name { get; set; } = "";
    public bool IsBot { get; set; }
    public int Rank { get; set; }
    public int NetWorth { get; set; }
    public int RatingChange { get; set; }
}

public sealed class MatchRecord
{
    public string MatchId { get; set; } = "";
    public string RoomCode { get; set; } = "";
    public string BoardId { get; set; } = "";
    public string Preset { get; set; } = "";
    public bool Ranked { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public int Turns { get; set; }
    public List<MatchPlayerRecord> Players { get; set; } = new();
}

public sealed record ActiveMatch(MatchRecord Match, string SnapshotJson, List<GameEvent> Events);

public sealed record ReplayData(string SnapshotJson, List<GameEvent> Events);

/// <summary>Accounts, social graph, statistics and the event-sourced match log.</summary>
public interface IStore
{
    Task InitializeAsync();

    Task<bool> CreateUserAsync(UserRecord user);
    Task<UserRecord?> GetUserAsync(string id);
    Task<UserRecord?> GetUserByNameAsync(string username);
    Task UpdateUserAsync(UserRecord user);
    Task<List<UserRecord>> LeaderboardAsync(int top);

    Task AddFriendAsync(string userId, string friendId);
    Task RemoveFriendAsync(string userId, string friendId);
    Task<List<UserRecord>> FriendsAsync(string userId);
    Task AddRecentPlayersAsync(string userId, IEnumerable<string> others);
    Task<List<UserRecord>> RecentPlayersAsync(string userId);

    Task<bool> GrantAchievementAsync(string userId, string achievementId);
    Task<List<string>> AchievementsAsync(string userId);

    Task SaveMatchStartAsync(MatchRecord match);
    Task AppendEventsAsync(string matchId, IReadOnlyList<GameEvent> events);
    /// <summary>Replaces the latest authority snapshot (a RoomSave without its event list).</summary>
    Task SaveSnapshotAsync(string matchId, int version, string snapshotJson);
    /// <summary>Recovery: forget events that were written after the last durable snapshot.</summary>
    Task TruncateEventsAsync(string matchId, int afterVersion);
    Task CompleteMatchAsync(MatchRecord match);
    Task AbandonMatchAsync(string matchId);
    Task<List<ActiveMatch>> LoadActiveMatchesAsync();
    Task<ReplayData?> GetReplayAsync(string matchId);
    Task<List<MatchRecord>> RecentMatchesAsync(string userId, int limit);
}

public sealed class InMemoryStore : IStore
{
    private sealed class MatchData
    {
        public MatchRecord Record = new();
        public string Snapshot = "";
        public readonly List<GameEvent> Events = new();
        public bool Active = true;
    }

    private readonly ConcurrentDictionary<string, UserRecord> _users = new();
    private readonly ConcurrentDictionary<string, string> _usernames = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _friends = new();
    private readonly ConcurrentDictionary<string, List<string>> _recent = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _achievements = new();
    private readonly ConcurrentDictionary<string, MatchData> _matches = new();
    private readonly object _gate = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<bool> CreateUserAsync(UserRecord user)
    {
        if (user.Username.Length > 0 && !_usernames.TryAdd(user.Username, user.Id)) return Task.FromResult(false);
        _users[user.Id] = user;
        return Task.FromResult(true);
    }

    public Task<UserRecord?> GetUserAsync(string id) => Task.FromResult(_users.GetValueOrDefault(id));

    public Task<UserRecord?> GetUserByNameAsync(string username) =>
        Task.FromResult(_usernames.TryGetValue(username, out var id) ? _users.GetValueOrDefault(id) : null);

    public Task UpdateUserAsync(UserRecord user)
    {
        _users[user.Id] = user;
        return Task.CompletedTask;
    }

    public Task<List<UserRecord>> LeaderboardAsync(int top) => Task.FromResult(_users.Values
        .Where(u => !u.IsGuest && u.Stats.RankedGames > 0)
        .OrderByDescending(u => u.Rating).ThenBy(u => u.DisplayName).Take(top).ToList());

    public Task AddFriendAsync(string userId, string friendId)
    {
        lock (_gate)
        {
            _friends.GetOrAdd(userId, _ => new HashSet<string>()).Add(friendId);
            _friends.GetOrAdd(friendId, _ => new HashSet<string>()).Add(userId);
        }
        return Task.CompletedTask;
    }

    public Task RemoveFriendAsync(string userId, string friendId)
    {
        lock (_gate)
        {
            if (_friends.TryGetValue(userId, out var a)) a.Remove(friendId);
            if (_friends.TryGetValue(friendId, out var b)) b.Remove(userId);
        }
        return Task.CompletedTask;
    }

    public Task<List<UserRecord>> FriendsAsync(string userId)
    {
        lock (_gate)
        {
            var ids = _friends.TryGetValue(userId, out var set) ? set.ToList() : new List<string>();
            return Task.FromResult(ids.Select(i => _users.GetValueOrDefault(i)).Where(u => u != null).Select(u => u!).ToList());
        }
    }

    public Task AddRecentPlayersAsync(string userId, IEnumerable<string> others)
    {
        lock (_gate)
        {
            var list = _recent.GetOrAdd(userId, _ => new List<string>());
            foreach (string o in others)
            {
                list.Remove(o);
                list.Insert(0, o);
            }
            if (list.Count > 20) list.RemoveRange(20, list.Count - 20);
        }
        return Task.CompletedTask;
    }

    public Task<List<UserRecord>> RecentPlayersAsync(string userId)
    {
        lock (_gate)
        {
            var ids = _recent.TryGetValue(userId, out var list) ? list.ToList() : new List<string>();
            return Task.FromResult(ids.Select(i => _users.GetValueOrDefault(i)).Where(u => u != null).Select(u => u!).ToList());
        }
    }

    public Task<bool> GrantAchievementAsync(string userId, string achievementId)
    {
        lock (_gate) return Task.FromResult(_achievements.GetOrAdd(userId, _ => new HashSet<string>()).Add(achievementId));
    }

    public Task<List<string>> AchievementsAsync(string userId)
    {
        lock (_gate)
            return Task.FromResult(_achievements.TryGetValue(userId, out var set) ? set.OrderBy(x => x).ToList() : new List<string>());
    }

    public Task SaveMatchStartAsync(MatchRecord match)
    {
        _matches[match.MatchId] = new MatchData { Record = match };
        return Task.CompletedTask;
    }

    public Task AppendEventsAsync(string matchId, IReadOnlyList<GameEvent> events)
    {
        if (_matches.TryGetValue(matchId, out var m))
            lock (m) m.Events.AddRange(events);
        return Task.CompletedTask;
    }

    public Task SaveSnapshotAsync(string matchId, int version, string snapshotJson)
    {
        if (_matches.TryGetValue(matchId, out var m)) m.Snapshot = snapshotJson;
        return Task.CompletedTask;
    }

    public Task TruncateEventsAsync(string matchId, int afterVersion)
    {
        if (_matches.TryGetValue(matchId, out var m))
            lock (m) m.Events.RemoveAll(e => e.Version > afterVersion);
        return Task.CompletedTask;
    }

    public Task CompleteMatchAsync(MatchRecord match)
    {
        if (_matches.TryGetValue(match.MatchId, out var m))
        {
            m.Record = match;
            m.Active = false;
        }
        return Task.CompletedTask;
    }

    public Task AbandonMatchAsync(string matchId)
    {
        if (_matches.TryGetValue(matchId, out var m)) m.Active = false;
        return Task.CompletedTask;
    }

    public Task<List<ActiveMatch>> LoadActiveMatchesAsync() => Task.FromResult(_matches.Values
        .Where(m => m.Active && m.Snapshot.Length > 0)
        .Select(m =>
        {
            lock (m) return new ActiveMatch(m.Record, m.Snapshot, m.Events.ToList());
        }).ToList());

    public Task<ReplayData?> GetReplayAsync(string matchId)
    {
        if (!_matches.TryGetValue(matchId, out var m) || m.Snapshot.Length == 0) return Task.FromResult<ReplayData?>(null);
        lock (m) return Task.FromResult<ReplayData?>(new ReplayData(m.Snapshot, m.Events.ToList()));
    }

    public Task<List<MatchRecord>> RecentMatchesAsync(string userId, int limit) => Task.FromResult(_matches.Values
        .Where(m => !m.Active && m.Record.Players.Any(p => p.UserId == userId))
        .OrderByDescending(m => m.Record.StartedAt).Take(limit).Select(m => m.Record).ToList());
}
