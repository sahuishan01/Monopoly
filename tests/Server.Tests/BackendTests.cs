using System.Net.Http.Json;
using System.Text.Json;
using Game.Core.AI;
using Game.Core.Engine;
using Game.Core.Events;
using Game.Core.Replay;
using Game.Core.Rules;
using Game.Core.Serialization;
using Game.Net;
using Game.Server.Data;
using Game.Server.Rooms;

namespace Server.Tests;

/// <summary>
/// Tests against real PostgreSQL and Redis. They run only when BOARDEMPIRE_TEST_POSTGRES /
/// BOARDEMPIRE_TEST_REDIS point at disposable instances.
/// </summary>
[Collection("server")]
public class BackendTests
{
    private static string? Pg => Environment.GetEnvironmentVariable("BOARDEMPIRE_TEST_POSTGRES");
    private static string? Redis => Environment.GetEnvironmentVariable("BOARDEMPIRE_TEST_REDIS");

    private static UserRecord NewUser(string name, bool guest = false) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Username = guest ? "" : name + Guid.NewGuid().ToString("N")[..6],
        DisplayName = name, IsGuest = guest, PasswordHash = "x",
    };

    [SkippableFact]
    public async Task Postgres_stores_users_social_graph_and_achievements()
    {
        Skip.If(Pg == null, "BOARDEMPIRE_TEST_POSTGRES not set");
        await using var store = new PostgresStore(Pg!);
        await store.InitializeAsync();
        await store.InitializeAsync();

        var a = NewUser("Asha");
        var b = NewUser("Bala");
        var guest = NewUser("Guest", guest: true);
        var guest2 = NewUser("Guest", guest: true);
        Assert.True(await store.CreateUserAsync(a));
        Assert.True(await store.CreateUserAsync(b));
        Assert.True(await store.CreateUserAsync(guest));
        Assert.True(await store.CreateUserAsync(guest2));
        var clash = NewUser("Clash");
        clash.Username = a.Username.ToUpperInvariant();
        Assert.False(await store.CreateUserAsync(clash));

        var loaded = await store.GetUserByNameAsync(a.Username.ToUpperInvariant());
        Assert.Equal(a.Id, loaded!.Id);
        Assert.Null(await store.GetUserByNameAsync(""));

        a.Rating = 1250;
        a.Stats.GamesPlayed = 3;
        a.Stats.RankedGames = 2;
        a.Stats.RentEarned = 123456789012;
        a.Unlocks.Add("gold_token");
        await store.UpdateUserAsync(a);
        loaded = await store.GetUserAsync(a.Id);
        Assert.Equal(1250, loaded!.Rating);
        Assert.Equal(123456789012, loaded.Stats.RentEarned);
        Assert.Equal(new[] { "gold_token" }, loaded.Unlocks);
        Assert.Contains(await store.LeaderboardAsync(500), u => u.Id == a.Id);
        Assert.DoesNotContain(await store.LeaderboardAsync(500), u => u.Id == b.Id);

        await store.AddFriendAsync(a.Id, b.Id);
        await store.AddFriendAsync(a.Id, b.Id);
        Assert.Equal(a.Id, (await store.FriendsAsync(b.Id)).Single().Id);
        await store.RemoveFriendAsync(b.Id, a.Id);
        Assert.Empty(await store.FriendsAsync(a.Id));

        await store.AddRecentPlayersAsync(a.Id, new[] { b.Id, guest.Id });
        Assert.Equal(2, (await store.RecentPlayersAsync(a.Id)).Count);

        Assert.True(await store.GrantAchievementAsync(a.Id, "first_win"));
        Assert.False(await store.GrantAchievementAsync(a.Id, "first_win"));
        Assert.Equal(new[] { "first_win" }, await store.AchievementsAsync(a.Id));
    }

    [SkippableFact]
    public async Task Postgres_event_log_reproduces_the_match()
    {
        Skip.If(Pg == null, "BOARDEMPIRE_TEST_POSTGRES not set");
        await using var store = new PostgresStore(Pg!);
        await store.InitializeAsync();
        var user = NewUser("Replay");
        await store.CreateUserAsync(user);

        string matchId = "pg-" + Guid.NewGuid().ToString("N")[..8];
        var cfg = new MatchConfig { MatchId = matchId, Seed = 11, Rules = RulePresets.Get("tycoon") };
        cfg.Rules.MaximumRounds = 20;
        cfg.Rules.SecretObjectivesEnabled = true;
        for (int i = 0; i < 4; i++) cfg.Players.Add(new PlayerSetup { Name = "B" + i, IsBot = true });
        var engine = GameEngine.Create(cfg);
        var initial = engine.State.RedactedFor(-1);
        var driver = new BotDriver(11);
        for (int i = 0; i < 4; i++) driver.SetBot(i, BotLevel.Medium);

        var record = new MatchRecord
        {
            MatchId = matchId, RoomCode = "PGROOM", BoardId = cfg.BoardId, Preset = "tycoon",
            Players = { new MatchPlayerRecord { Seat = 0, UserId = user.Id, Name = "B0" }, new MatchPlayerRecord { Seat = 1, Name = "B1", IsBot = true } },
        };
        await store.SaveMatchStartAsync(record);
        var all = new List<GameEvent>();
        async Task Persist(IReadOnlyList<GameEvent> events)
        {
            all.AddRange(events);
            await store.AppendEventsAsync(matchId, events);
        }

        await Persist(engine.Start());
        var pending = new List<GameEvent>();
        BotDriver.RunToEnd(engine, driver, 200_000, (_, ev) => pending.AddRange(ev));
        for (int i = 0; i < pending.Count; i += 200) await Persist(pending.Skip(i).Take(200).ToList());

        var save = new RoomSave { Config = cfg, Board = engine.Board, State = engine.State, Initial = initial };
        await store.SaveSnapshotAsync(matchId, engine.State.Version, save.ToJson());
        await store.SaveSnapshotAsync(matchId, engine.State.Version, save.ToJson());

        var active = (await store.LoadActiveMatchesAsync()).Single(m => m.Match.MatchId == matchId);
        Assert.Equal(all.Count, active.Events.Count);
        var restored = RoomSave.FromJson(active.SnapshotJson);
        Assert.Equal(StateHasher.FullHash(engine.State), StateHasher.FullHash(restored.State));
        Assert.Equal(engine.State.Rng.S, restored.State.Rng.S);

        var data = await store.GetReplayAsync(matchId);
        var replay = new ReplayPlayer(new ReplayFile { Board = restored.Board, Initial = restored.Initial, Events = data!.Events });
        replay.Seek(replay.Length);
        Assert.Equal(StateHasher.Hash(engine.State), StateHasher.Hash(replay.State));
        Assert.Equal(CoreJson.Serialize(all), CoreJson.Serialize(data.Events));

        await store.TruncateEventsAsync(matchId, 10);
        Assert.Equal(10, (await store.GetReplayAsync(matchId))!.Events.Count);

        record.Turns = engine.State.TurnNumber;
        record.Players[0].Rank = 1;
        record.Players[0].NetWorth = 4321;
        record.Players[0].RatingChange = 12;
        await store.CompleteMatchAsync(record);
        Assert.DoesNotContain(await store.LoadActiveMatchesAsync(), m => m.Match.MatchId == matchId);
        var recent = (await store.RecentMatchesAsync(user.Id, 5)).Single();
        Assert.Equal(4321, recent.Players.First(p => p.Seat == 0).NetWorth);
        Assert.Equal(engine.State.TurnNumber, recent.Turns);
        Assert.NotNull(recent.EndedAt);
    }

    [SkippableFact]
    public async Task Redis_presence_tracks_players_rooms_and_invites()
    {
        Skip.If(Redis == null, "BOARDEMPIRE_TEST_REDIS not set");
        using var presence = new RedisPresence(Redis!);
        string u1 = "u-" + Guid.NewGuid().ToString("N")[..8], u2 = "u-" + Guid.NewGuid().ToString("N")[..8];
        await presence.SetOnlineAsync(u1, "ROOM01");
        var online = await presence.GetAsync(new[] { u1, u2 });
        Assert.Equal("ROOM01", online[u1]);
        Assert.False(online.ContainsKey(u2));
        await presence.SetOfflineAsync(u1);
        Assert.Empty(await presence.GetAsync(new[] { u1 }));
        Assert.Empty(await presence.GetAsync(Array.Empty<string>()));

        await presence.RegisterRoomAsync("ROOM01", "instance-a");
        Assert.Equal("instance-a", await presence.RoomInstanceAsync("ROOM01"));
        await presence.UnregisterRoomAsync("ROOM01");
        Assert.Null(await presence.RoomInstanceAsync("ROOM01"));

        await presence.InviteAsync(u1, "Name | with pipe", u2, "ROOM01");
        await presence.InviteAsync(u1, "Name | with pipe", u2, "ROOM02");
        var invite = (await presence.InvitesAsync(u2)).Single();
        Assert.Equal("ROOM02", invite.RoomCode);
        Assert.Equal("Name | with pipe", invite.FromName);
    }

    [SkippableFact]
    public async Task Server_on_postgres_and_redis_survives_a_restart_mid_match()
    {
        Skip.If(Pg == null || Redis == null, "backend services not configured");
        string secret = "integration-test-secret-0123456789";
        Environment.SetEnvironmentVariable("BoardEmpire__TokenSecret", secret);
        Environment.SetEnvironmentVariable("BoardEmpire__Postgres", Pg);
        Environment.SetEnvironmentVariable("BoardEmpire__Redis", Redis);
        Environment.SetEnvironmentVariable("BoardEmpire__BotDelaySeconds", "0");
        try
        {
            string code, matchId;
            Session alice, bob;
            var first = new TestServer();
            {
                Assert.IsType<PostgresStore>(first.Store);
                alice = await first.Register("pg_alice_" + Guid.NewGuid().ToString("N")[..6]);
                bob = await first.Guest("Bob");
                code = (await (await alice.Http.PostAsJsonAsync("/api/rooms", new { preset = "classic_plus" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
                var table = new Table();
                var a = await table.Add(first.Connect(alice, code));
                var b = await table.Add(first.Connect(bob, code));
                Assert.True(await table.Until(() => a.Lobby?.Seats.Count == 2, 10));
                var rules = RulePresets.Get("classic_plus");
                rules.MaximumRounds = 14;
                a.UpdateSettings(rules, null);
                a.AddBot(BotLevel.Medium);
                b.SetReady(true);
                Assert.True(await table.Until(() => a.Lobby!.Seats.Count == 3 && a.Lobby.Seats[1].Ready && a.Lobby.Rules.MaximumRounds == 14, 10));
                a.StartMatch();
                Assert.True(await table.Until(() => a.State is { Round: >= 3 }, 120));
                table.Autoplay = false;
                matchId = a.State!.MatchId;
                await first.Rooms.FlushAsync();
            }
            await first.DisposeAsync();

            await using var second = new TestServer();
            Assert.NotNull(second.Rooms.Find(code));
            var t2 = new Table();
            var a2 = await t2.Add(second.Connect(alice, code));
            var b2 = await t2.Add(second.Connect(bob, code));
            Assert.True(await t2.Until(() => a2.State?.IsOver == true && b2.State?.IsOver == true, 240));
            Assert.Equal(StateHasher.Hash(a2.State!), StateHasher.Hash(b2.State!));
            await second.Rooms.FlushAsync();
            await Task.Delay(100);
            await second.Rooms.FlushAsync();

            var http = second.CreateClient();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", alice.Token);
            var recent = await http.GetFromJsonAsync<JsonElement>("/api/matches/recent");
            Assert.Contains(recent.EnumerateArray(), m => m.GetProperty("matchId").GetString() == matchId);
            string replayJson = await http.GetStringAsync($"/api/matches/{matchId}/replay");
            var replay = new ReplayPlayer(ReplayFile.FromJson(replayJson));
            replay.Seek(replay.Length);
            Assert.Equal(StateHasher.Hash(a2.State!), StateHasher.Hash(replay.State));
            var me = await http.GetFromJsonAsync<JsonElement>("/api/me");
            Assert.Equal(1, me.GetProperty("profile").GetProperty("stats").GetProperty("gamesPlayed").GetInt32());
        }
        finally
        {
            foreach (string key in new[] { "TokenSecret", "Postgres", "Redis", "BotDelaySeconds" })
                Environment.SetEnvironmentVariable("BoardEmpire__" + key, null);
        }
    }
}
