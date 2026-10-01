using System.Collections.Concurrent;
using StackExchange.Redis;

namespace Game.Server.Rooms;

/// <summary>Who is online, which instance owns which room, and pending invites.</summary>
public interface IPresence
{
    Task SetOnlineAsync(string userId, string roomCode);
    Task SetOfflineAsync(string userId);
    Task<Dictionary<string, string>> GetAsync(IEnumerable<string> userIds);
    Task RegisterRoomAsync(string roomCode, string instanceId);
    Task UnregisterRoomAsync(string roomCode);
    Task<string?> RoomInstanceAsync(string roomCode);
    Task InviteAsync(string fromUserId, string fromName, string toUserId, string roomCode);
    Task<List<Invite>> InvitesAsync(string userId);
}

public sealed record Invite(string FromUserId, string FromName, string RoomCode, long SentAt);

public sealed class MemoryPresence : IPresence
{
    private readonly ConcurrentDictionary<string, string> _online = new();
    private readonly ConcurrentDictionary<string, string> _rooms = new();
    private readonly ConcurrentDictionary<string, List<Invite>> _invites = new();

    public Task SetOnlineAsync(string userId, string roomCode)
    {
        _online[userId] = roomCode;
        return Task.CompletedTask;
    }

    public Task SetOfflineAsync(string userId)
    {
        _online.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    public Task<Dictionary<string, string>> GetAsync(IEnumerable<string> userIds) => Task.FromResult(
        userIds.Where(_online.ContainsKey).ToDictionary(id => id, id => _online.GetValueOrDefault(id, "")));

    public Task RegisterRoomAsync(string roomCode, string instanceId)
    {
        _rooms[roomCode] = instanceId;
        return Task.CompletedTask;
    }

    public Task UnregisterRoomAsync(string roomCode)
    {
        _rooms.TryRemove(roomCode, out _);
        return Task.CompletedTask;
    }

    public Task<string?> RoomInstanceAsync(string roomCode) => Task.FromResult(_rooms.GetValueOrDefault(roomCode));

    public Task InviteAsync(string fromUserId, string fromName, string toUserId, string roomCode)
    {
        var list = _invites.GetOrAdd(toUserId, _ => new List<Invite>());
        lock (list)
        {
            list.RemoveAll(i => i.FromUserId == fromUserId);
            list.Add(new Invite(fromUserId, fromName, roomCode, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        }
        return Task.CompletedTask;
    }

    public Task<List<Invite>> InvitesAsync(string userId)
    {
        if (!_invites.TryGetValue(userId, out var list)) return Task.FromResult(new List<Invite>());
        long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600;
        lock (list)
        {
            list.RemoveAll(i => i.SentAt < cutoff);
            return Task.FromResult(list.ToList());
        }
    }
}

/// <summary>Redis-backed presence so that several server instances share one view.</summary>
public sealed class RedisPresence : IPresence, IDisposable
{
    private static readonly TimeSpan OnlineTtl = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RoomTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan InviteTtl = TimeSpan.FromMinutes(10);

    private readonly ConnectionMultiplexer _redis;
    private IDatabase Db => _redis.GetDatabase();

    public RedisPresence(string configuration) => _redis = ConnectionMultiplexer.Connect(configuration);

    public Task SetOnlineAsync(string userId, string roomCode) => Db.StringSetAsync("be:online:" + userId, roomCode, OnlineTtl);

    public Task SetOfflineAsync(string userId) => Db.KeyDeleteAsync("be:online:" + userId);

    public async Task<Dictionary<string, string>> GetAsync(IEnumerable<string> userIds)
    {
        var ids = userIds.ToArray();
        var result = new Dictionary<string, string>();
        if (ids.Length == 0) return result;
        var values = await Db.StringGetAsync(ids.Select(i => (RedisKey)("be:online:" + i)).ToArray());
        for (int i = 0; i < ids.Length; i++)
            if (values[i].HasValue) result[ids[i]] = values[i].ToString();
        return result;
    }

    public Task RegisterRoomAsync(string roomCode, string instanceId) => Db.StringSetAsync("be:room:" + roomCode, instanceId, RoomTtl);

    public Task UnregisterRoomAsync(string roomCode) => Db.KeyDeleteAsync("be:room:" + roomCode);

    public async Task<string?> RoomInstanceAsync(string roomCode)
    {
        var value = await Db.StringGetAsync("be:room:" + roomCode);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task InviteAsync(string fromUserId, string fromName, string toUserId, string roomCode)
    {
        string key = "be:invites:" + toUserId;
        string entry = $"{roomCode}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}|{fromName}";
        await Db.HashSetAsync(key, fromUserId, entry);
        await Db.KeyExpireAsync(key, InviteTtl);
    }

    public async Task<List<Invite>> InvitesAsync(string userId)
    {
        var list = new List<Invite>();
        foreach (var e in await Db.HashGetAllAsync("be:invites:" + userId))
        {
            string[] parts = e.Value.ToString().Split('|', 3);
            if (parts.Length == 3 && long.TryParse(parts[1], out long sent))
                list.Add(new Invite(e.Name.ToString(), parts[2], parts[0], sent));
        }
        return list;
    }

    public void Dispose() => _redis.Dispose();
}
