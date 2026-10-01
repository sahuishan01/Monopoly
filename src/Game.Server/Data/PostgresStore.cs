using System.Text.Json;
using Game.Core.Events;
using Game.Core.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Game.Server.Data;

/// <summary>
/// PostgreSQL persistence. Matches are event sourced: one row per event plus the latest
/// authority snapshot, which is all that is needed for recovery, reconnection and replays.
/// </summary>
public sealed class PostgresStore : IStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _db;

    public PostgresStore(string connectionString) => _db = NpgsqlDataSource.Create(connectionString);

    public async Task InitializeAsync()
    {
        const string schema = """
            CREATE TABLE IF NOT EXISTS user_profiles (
                id TEXT PRIMARY KEY,
                username TEXT,
                display_name TEXT NOT NULL,
                password_hash TEXT NOT NULL DEFAULT '',
                is_guest BOOLEAN NOT NULL,
                rating INT NOT NULL DEFAULT 1000,
                avatar INT NOT NULL DEFAULT 0,
                ranked_games INT NOT NULL DEFAULT 0,
                unlocks JSONB NOT NULL DEFAULT '[]',
                created_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS user_profiles_username ON user_profiles (lower(username)) WHERE username <> '';
            CREATE TABLE IF NOT EXISTS match_statistics (
                user_id TEXT PRIMARY KEY REFERENCES user_profiles(id) ON DELETE CASCADE,
                stats JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS friends (
                user_id TEXT NOT NULL REFERENCES user_profiles(id) ON DELETE CASCADE,
                friend_id TEXT NOT NULL REFERENCES user_profiles(id) ON DELETE CASCADE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, friend_id)
            );
            CREATE TABLE IF NOT EXISTS recent_players (
                user_id TEXT NOT NULL REFERENCES user_profiles(id) ON DELETE CASCADE,
                other_id TEXT NOT NULL REFERENCES user_profiles(id) ON DELETE CASCADE,
                played_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, other_id)
            );
            CREATE TABLE IF NOT EXISTS achievements (
                user_id TEXT NOT NULL REFERENCES user_profiles(id) ON DELETE CASCADE,
                achievement_id TEXT NOT NULL,
                earned_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, achievement_id)
            );
            CREATE TABLE IF NOT EXISTS matches (
                id TEXT PRIMARY KEY,
                room_code TEXT NOT NULL,
                board_id TEXT NOT NULL,
                preset TEXT NOT NULL,
                ranked BOOLEAN NOT NULL,
                status TEXT NOT NULL DEFAULT 'active',
                turns INT NOT NULL DEFAULT 0,
                started_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                ended_at TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS matches_status ON matches (status);
            CREATE TABLE IF NOT EXISTS match_players (
                match_id TEXT NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
                seat INT NOT NULL,
                user_id TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL,
                is_bot BOOLEAN NOT NULL,
                rank INT NOT NULL DEFAULT 0,
                net_worth INT NOT NULL DEFAULT 0,
                rating_change INT NOT NULL DEFAULT 0,
                PRIMARY KEY (match_id, seat)
            );
            CREATE INDEX IF NOT EXISTS match_players_user ON match_players (user_id);
            CREATE TABLE IF NOT EXISTS match_snapshots (
                match_id TEXT PRIMARY KEY REFERENCES matches(id) ON DELETE CASCADE,
                version INT NOT NULL,
                snapshot JSONB NOT NULL,
                saved_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS match_events (
                match_id TEXT NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
                version INT NOT NULL,
                type TEXT NOT NULL,
                payload JSONB NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (match_id, version)
            );
            """;
        await using var cmd = _db.CreateCommand(schema);
        await cmd.ExecuteNonQueryAsync();
    }

    private static NpgsqlParameter Json(string name, string json) => new(name, NpgsqlDbType.Jsonb) { Value = json };

    // ------------------------------------------------------------------ users

    public async Task<bool> CreateUserAsync(UserRecord u)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO user_profiles (id, username, display_name, password_hash, is_guest, rating, avatar, unlocks, created_at) " +
                "VALUES (@id, @username, @name, @hash, @guest, @rating, @avatar, @unlocks, @created)", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", u.Id);
                cmd.Parameters.AddWithValue("username", u.Username);
                cmd.Parameters.AddWithValue("name", u.DisplayName);
                cmd.Parameters.AddWithValue("hash", u.PasswordHash);
                cmd.Parameters.AddWithValue("guest", u.IsGuest);
                cmd.Parameters.AddWithValue("rating", u.Rating);
                cmd.Parameters.AddWithValue("avatar", u.Avatar);
                cmd.Parameters.Add(Json("unlocks", JsonSerializer.Serialize(u.Unlocks)));
                cmd.Parameters.AddWithValue("created", u.CreatedAt);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = new NpgsqlCommand("INSERT INTO match_statistics (user_id, stats) VALUES (@id, @stats)", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", u.Id);
                cmd.Parameters.Add(Json("stats", JsonSerializer.Serialize(u.Stats)));
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return true;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    private const string UserSelect =
        "SELECT u.id, u.username, u.display_name, u.password_hash, u.is_guest, u.rating, u.avatar, u.unlocks, u.created_at, s.stats " +
        "FROM user_profiles u LEFT JOIN match_statistics s ON s.user_id = u.id ";

    private static UserRecord ReadUser(NpgsqlDataReader r) => new()
    {
        Id = r.GetString(0),
        Username = r.GetString(1),
        DisplayName = r.GetString(2),
        PasswordHash = r.GetString(3),
        IsGuest = r.GetBoolean(4),
        Rating = r.GetInt32(5),
        Avatar = r.GetInt32(6),
        Unlocks = JsonSerializer.Deserialize<List<string>>(r.GetString(7)) ?? new List<string>(),
        CreatedAt = r.GetDateTime(8),
        Stats = r.IsDBNull(9) ? new UserStats() : JsonSerializer.Deserialize<UserStats>(r.GetString(9)) ?? new UserStats(),
    };

    private async Task<List<UserRecord>> QueryUsers(string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = _db.CreateCommand(sql);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<UserRecord>();
        while (await reader.ReadAsync()) list.Add(ReadUser(reader));
        return list;
    }

    public async Task<UserRecord?> GetUserAsync(string id) =>
        (await QueryUsers(UserSelect + "WHERE u.id = @id", ("id", id))).FirstOrDefault();

    public async Task<UserRecord?> GetUserByNameAsync(string username) =>
        (await QueryUsers(UserSelect + "WHERE lower(u.username) = lower(@n) AND u.username <> ''", ("n", username))).FirstOrDefault();

    public async Task UpdateUserAsync(UserRecord u)
    {
        await using var batch = _db.CreateBatch();
        var profile = new NpgsqlBatchCommand(
            "UPDATE user_profiles SET display_name = @name, rating = @rating, avatar = @avatar, unlocks = @unlocks, ranked_games = @ranked WHERE id = @id");
        profile.Parameters.AddWithValue("name", u.DisplayName);
        profile.Parameters.AddWithValue("rating", u.Rating);
        profile.Parameters.AddWithValue("avatar", u.Avatar);
        profile.Parameters.Add(Json("unlocks", JsonSerializer.Serialize(u.Unlocks)));
        profile.Parameters.AddWithValue("ranked", u.Stats.RankedGames);
        profile.Parameters.AddWithValue("id", u.Id);
        batch.BatchCommands.Add(profile);
        var stats = new NpgsqlBatchCommand(
            "INSERT INTO match_statistics (user_id, stats) VALUES (@id, @stats) ON CONFLICT (user_id) DO UPDATE SET stats = EXCLUDED.stats");
        stats.Parameters.AddWithValue("id", u.Id);
        stats.Parameters.Add(Json("stats", JsonSerializer.Serialize(u.Stats)));
        batch.BatchCommands.Add(stats);
        await batch.ExecuteNonQueryAsync();
    }

    public Task<List<UserRecord>> LeaderboardAsync(int top) => QueryUsers(
        UserSelect + "WHERE NOT u.is_guest AND u.ranked_games > 0 ORDER BY u.rating DESC, u.display_name LIMIT @top", ("top", top));

    // ------------------------------------------------------------------ social

    private async Task Exec(string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = _db.CreateCommand(sql);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    public Task AddFriendAsync(string userId, string friendId) => Exec(
        "INSERT INTO friends (user_id, friend_id) VALUES (@a, @b), (@b, @a) ON CONFLICT DO NOTHING", ("a", userId), ("b", friendId));

    public Task RemoveFriendAsync(string userId, string friendId) => Exec(
        "DELETE FROM friends WHERE (user_id = @a AND friend_id = @b) OR (user_id = @b AND friend_id = @a)", ("a", userId), ("b", friendId));

    public Task<List<UserRecord>> FriendsAsync(string userId) => QueryUsers(
        UserSelect + "JOIN friends f ON f.friend_id = u.id WHERE f.user_id = @id ORDER BY u.display_name", ("id", userId));

    public async Task AddRecentPlayersAsync(string userId, IEnumerable<string> others)
    {
        foreach (string other in others)
            await Exec("INSERT INTO recent_players (user_id, other_id) VALUES (@a, @b) ON CONFLICT (user_id, other_id) DO UPDATE SET played_at = now()",
                ("a", userId), ("b", other));
    }

    public Task<List<UserRecord>> RecentPlayersAsync(string userId) => QueryUsers(
        UserSelect + "JOIN recent_players r ON r.other_id = u.id WHERE r.user_id = @id ORDER BY r.played_at DESC LIMIT 20", ("id", userId));

    public async Task<bool> GrantAchievementAsync(string userId, string achievementId)
    {
        await using var cmd = _db.CreateCommand(
            "INSERT INTO achievements (user_id, achievement_id) VALUES (@u, @a) ON CONFLICT DO NOTHING");
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("a", achievementId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<string>> AchievementsAsync(string userId)
    {
        await using var cmd = _db.CreateCommand("SELECT achievement_id FROM achievements WHERE user_id = @u ORDER BY achievement_id");
        cmd.Parameters.AddWithValue("u", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<string>();
        while (await reader.ReadAsync()) list.Add(reader.GetString(0));
        return list;
    }

    // ------------------------------------------------------------------ matches

    public async Task SaveMatchStartAsync(MatchRecord m)
    {
        await using var batch = _db.CreateBatch();
        var match = new NpgsqlBatchCommand(
            "INSERT INTO matches (id, room_code, board_id, preset, ranked, started_at) VALUES (@id, @room, @board, @preset, @ranked, @started) ON CONFLICT (id) DO NOTHING");
        match.Parameters.AddWithValue("id", m.MatchId);
        match.Parameters.AddWithValue("room", m.RoomCode);
        match.Parameters.AddWithValue("board", m.BoardId);
        match.Parameters.AddWithValue("preset", m.Preset);
        match.Parameters.AddWithValue("ranked", m.Ranked);
        match.Parameters.AddWithValue("started", m.StartedAt);
        batch.BatchCommands.Add(match);
        foreach (var p in m.Players)
        {
            var player = new NpgsqlBatchCommand(
                "INSERT INTO match_players (match_id, seat, user_id, name, is_bot) VALUES (@m, @seat, @user, @name, @bot) ON CONFLICT DO NOTHING");
            player.Parameters.AddWithValue("m", m.MatchId);
            player.Parameters.AddWithValue("seat", p.Seat);
            player.Parameters.AddWithValue("user", p.UserId);
            player.Parameters.AddWithValue("name", p.Name);
            player.Parameters.AddWithValue("bot", p.IsBot);
            batch.BatchCommands.Add(player);
        }
        await batch.ExecuteNonQueryAsync();
    }

    public async Task AppendEventsAsync(string matchId, IReadOnlyList<GameEvent> events)
    {
        if (events.Count == 0) return;
        await using var batch = _db.CreateBatch();
        foreach (var e in events)
        {
            var cmd = new NpgsqlBatchCommand(
                "INSERT INTO match_events (match_id, version, type, payload) VALUES (@m, @v, @t, @p) ON CONFLICT DO NOTHING");
            cmd.Parameters.AddWithValue("m", matchId);
            cmd.Parameters.AddWithValue("v", e.Version);
            cmd.Parameters.AddWithValue("t", e.GetType().Name);
            cmd.Parameters.Add(Json("p", CoreJson.Serialize(e)));
            batch.BatchCommands.Add(cmd);
        }
        await batch.ExecuteNonQueryAsync();
    }

    public async Task SaveSnapshotAsync(string matchId, int version, string snapshotJson)
    {
        await using var cmd = _db.CreateCommand(
            "INSERT INTO match_snapshots (match_id, version, snapshot) VALUES (@m, @v, @s) " +
            "ON CONFLICT (match_id) DO UPDATE SET version = EXCLUDED.version, snapshot = EXCLUDED.snapshot, saved_at = now()");
        cmd.Parameters.AddWithValue("m", matchId);
        cmd.Parameters.AddWithValue("v", version);
        cmd.Parameters.Add(Json("s", snapshotJson));
        await cmd.ExecuteNonQueryAsync();
    }

    public Task TruncateEventsAsync(string matchId, int afterVersion) =>
        Exec("DELETE FROM match_events WHERE match_id = @m AND version > @v", ("m", matchId), ("v", afterVersion));

    public async Task CompleteMatchAsync(MatchRecord m)
    {
        await using var batch = _db.CreateBatch();
        var match = new NpgsqlBatchCommand("UPDATE matches SET status = 'complete', ended_at = now(), turns = @turns WHERE id = @id");
        match.Parameters.AddWithValue("turns", m.Turns);
        match.Parameters.AddWithValue("id", m.MatchId);
        batch.BatchCommands.Add(match);
        foreach (var p in m.Players)
        {
            var player = new NpgsqlBatchCommand(
                "UPDATE match_players SET rank = @rank, net_worth = @worth, rating_change = @delta WHERE match_id = @m AND seat = @seat");
            player.Parameters.AddWithValue("rank", p.Rank);
            player.Parameters.AddWithValue("worth", p.NetWorth);
            player.Parameters.AddWithValue("delta", p.RatingChange);
            player.Parameters.AddWithValue("m", m.MatchId);
            player.Parameters.AddWithValue("seat", p.Seat);
            batch.BatchCommands.Add(player);
        }
        await batch.ExecuteNonQueryAsync();
    }

    public Task AbandonMatchAsync(string matchId) =>
        Exec("UPDATE matches SET status = 'abandoned', ended_at = now() WHERE id = @id AND status = 'active'", ("id", matchId));

    private async Task<List<MatchRecord>> QueryMatches(string where, params (string Name, object Value)[] args)
    {
        var list = new List<MatchRecord>();
        await using (var cmd = _db.CreateCommand(
            "SELECT id, room_code, board_id, preset, ranked, started_at, ended_at, turns FROM matches m " + where))
        {
            foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new MatchRecord
                {
                    MatchId = reader.GetString(0), RoomCode = reader.GetString(1), BoardId = reader.GetString(2),
                    Preset = reader.GetString(3), Ranked = reader.GetBoolean(4), StartedAt = reader.GetDateTime(5),
                    EndedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6), Turns = reader.GetInt32(7),
                });
            }
        }
        foreach (var m in list)
        {
            await using var cmd = _db.CreateCommand(
                "SELECT seat, user_id, name, is_bot, rank, net_worth, rating_change FROM match_players WHERE match_id = @m ORDER BY seat");
            cmd.Parameters.AddWithValue("m", m.MatchId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                m.Players.Add(new MatchPlayerRecord
                {
                    Seat = reader.GetInt32(0), UserId = reader.GetString(1), Name = reader.GetString(2), IsBot = reader.GetBoolean(3),
                    Rank = reader.GetInt32(4), NetWorth = reader.GetInt32(5), RatingChange = reader.GetInt32(6),
                });
            }
        }
        return list;
    }

    private async Task<List<GameEvent>> LoadEvents(string matchId)
    {
        await using var cmd = _db.CreateCommand("SELECT payload FROM match_events WHERE match_id = @m ORDER BY version");
        cmd.Parameters.AddWithValue("m", matchId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var events = new List<GameEvent>();
        while (await reader.ReadAsync()) events.Add(CoreJson.Deserialize<GameEvent>(Normalize(reader.GetString(0))));
        return events;
    }

    /// <summary>jsonb does not preserve key order; the polymorphic reader needs "$t" first.</summary>
    private static string Normalize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return json;
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            WriteOrdered(doc.RootElement, writer);
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteOrdered(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                if (element.TryGetProperty("$t", out var type))
                {
                    writer.WritePropertyName("$t");
                    type.WriteTo(writer);
                }
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.NameEquals("$t")) continue;
                    writer.WritePropertyName(prop.Name);
                    WriteOrdered(prop.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteOrdered(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private async Task<string?> LoadSnapshot(string matchId)
    {
        await using var cmd = _db.CreateCommand("SELECT snapshot FROM match_snapshots WHERE match_id = @m");
        cmd.Parameters.AddWithValue("m", matchId);
        return await cmd.ExecuteScalarAsync() is string s ? Normalize(s) : null;
    }

    public async Task<List<ActiveMatch>> LoadActiveMatchesAsync()
    {
        var result = new List<ActiveMatch>();
        foreach (var m in await QueryMatches("WHERE m.status = 'active' ORDER BY m.started_at"))
        {
            string? snapshot = await LoadSnapshot(m.MatchId);
            if (snapshot == null) continue;
            result.Add(new ActiveMatch(m, snapshot, await LoadEvents(m.MatchId)));
        }
        return result;
    }

    public async Task<ReplayData?> GetReplayAsync(string matchId)
    {
        string? snapshot = await LoadSnapshot(matchId);
        return snapshot == null ? null : new ReplayData(snapshot, await LoadEvents(matchId));
    }

    public Task<List<MatchRecord>> RecentMatchesAsync(string userId, int limit) => QueryMatches(
        "WHERE m.status = 'complete' AND EXISTS (SELECT 1 FROM match_players p WHERE p.match_id = m.id AND p.user_id = @u) " +
        "ORDER BY m.started_at DESC LIMIT @limit", ("u", userId), ("limit", limit));

    public ValueTask DisposeAsync() => _db.DisposeAsync();
}
