using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Game.Core.AI;
using Game.Core.Engine;
using Game.Core.Replay;
using Game.Core.Rng;
using Game.Core.Rules;
using Game.Core.Serialization;
using Game.Net;
using Game.Net.Transport;
using Game.Protocol;
using Game.Server.Auth;
using Game.Server.Data;
using Game.Server.Rooms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests;

public sealed class TestServer : WebApplicationFactory<Program>
{
    private readonly IStore? _store;

    public TestServer(IStore? store = null) => _store = store;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            if (_store != null) services.AddSingleton(_store);
        });
    }

    public ServerOptions Options => Services.GetRequiredService<ServerOptions>();
    public RoomManager Rooms => Services.GetRequiredService<RoomManager>();
    public IStore Store => Services.GetRequiredService<IStore>();

    public async Task<Session> Guest(string name)
    {
        var http = CreateClient();
        var response = await http.PostAsJsonAsync("/api/auth/guest", new { name });
        response.EnsureSuccessStatusCode();
        return Session.From(http, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    public async Task<Session> Register(string username, string password = "correct horse")
    {
        var http = CreateClient();
        var response = await http.PostAsJsonAsync("/api/auth/register", new { username, password, displayName = username });
        response.EnsureSuccessStatusCode();
        return Session.From(http, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    public RoomClient Connect(Session session, string code, PeerRole role = PeerRole.Player)
    {
        var transport = new WebSocketClientTransport(async cancel =>
        {
            var ws = Server.CreateWebSocketClient();
            return await ws.ConnectAsync(new Uri(Server.BaseAddress, $"/ws/{code}?token={Uri.EscapeDataString(session.Token)}"), cancel);
        });
        return new RoomClient(transport, session.Name) { AuthToken = session.Token, Role = role };
    }
}

public sealed record Session(HttpClient Http, string UserId, string Token, string Name)
{
    public static Session From(HttpClient http, JsonElement json)
    {
        string token = json.GetProperty("token").GetString()!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Session(http, json.GetProperty("userId").GetString()!, token, json.GetProperty("displayName").GetString()!);
    }
}

/// <summary>Drives clients against the real server loop using wall-clock time.</summary>
public sealed class Table
{
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Dictionary<RoomClient, BotBrain> _brains = new();
    private readonly Dictionary<RoomClient, (int Version, double At)> _sent = new();

    public List<RoomClient> Clients { get; } = new();
    public bool Autoplay { get; set; } = true;

    public async Task<RoomClient> Add(RoomClient client)
    {
        Clients.Add(client);
        _brains[client] = new BotBrain(BotLevel.Medium, new Pcg32((ulong)Clients.Count * 13)) { Rollouts = 0 };
        await client.ConnectAsync();
        return client;
    }

    public async Task<bool> Until(Func<bool> done, double seconds = 60)
    {
        double end = _clock.Elapsed.TotalSeconds + seconds;
        while (_clock.Elapsed.TotalSeconds < end)
        {
            double now = _clock.Elapsed.TotalSeconds;
            foreach (var c in Clients)
            {
                c.Poll(now);
                if (Autoplay) Play(c, now);
            }
            if (done()) return true;
            await Task.Delay(3);
        }
        return done();
    }

    private void Play(RoomClient c, double now)
    {
        var s = c.State;
        if (s == null || s.IsOver || !c.IsLinkUp) return;
        if (_sent.TryGetValue(c, out var last) && last.Version == s.Version && now - last.At < 5) return;
        foreach (int seat in TurnInfo.PendingActors(s))
        {
            if (!c.Controls(seat)) continue;
            var command = _brains[c].Decide(s, seat) ?? (s.Trades.Any(t => t.From == seat) ? null : BotBrain.TimeoutAction(s, seat));
            if (command == null) continue;
            c.SendCommand(command);
            _sent[c] = (s.Version, now);
            return;
        }
    }
}

[Collection("server")]
public class ServerTests
{
    private static TestServer Fast(IStore? store = null)
    {
        var server = new TestServer(store);
        server.Options.BotDelaySeconds = 0;
        server.Options.MatchmakingFillSeconds = 1;
        return server;
    }

    [Fact]
    public async Task Health_and_version_are_public()
    {
        await using var server = Fast();
        var http = server.CreateClient();
        var health = await http.GetFromJsonAsync<JsonElement>("/healthz");
        Assert.Equal("ok", health.GetProperty("status").GetString());
        var version = await http.GetFromJsonAsync<JsonElement>("/api/version");
        Assert.Equal(ProtocolInfo.Version, version.GetProperty("protocol").GetInt32());
        Assert.Contains("classic", version.GetProperty("presets").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Accounts_register_login_and_protect_profiles()
    {
        await using var server = Fast();
        var http = server.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/me")).StatusCode);

        var ishan = await server.Register("ishan");
        var me = await ishan.Http.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("ishan", me.GetProperty("profile").GetProperty("username").GetString());
        Assert.Equal(1000, me.GetProperty("profile").GetProperty("rating").GetInt32());

        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/auth/register", new { username = "ISHAN", password = "another pass" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/auth/register", new { username = "x", password = "another pass" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/auth/register", new { username = "valid_name", password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/auth/login", new { username = "ishan", password = "wrong password" })).StatusCode);
        var login = await http.PostAsJsonAsync("/api/auth/login", new { username = "ishan", password = "correct horse" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var tampered = server.CreateClient();
        tampered.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ishan.Token[..^3] + "abc");
        Assert.Equal(HttpStatusCode.Unauthorized, (await tampered.GetAsync("/api/me")).StatusCode);

        var updated = await ishan.Http.PutAsJsonAsync("/api/me", new { displayName = "Ishan S", avatar = 3 });
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ishan S", body.GetProperty("profile").GetProperty("displayName").GetString());
    }

    [Fact]
    public void Tokens_expire_and_passwords_are_salted()
    {
        var tokens = new TokenService("0123456789abcdef0123");
        string token = tokens.Issue("u1", "Name", false);
        Assert.Equal("u1", tokens.Validate(token)!.UserId);
        Assert.Null(new TokenService("another-secret-value-here").Validate(token));
        tokens.Lifetime = TimeSpan.FromSeconds(-5);
        Assert.Null(tokens.Validate(tokens.Issue("u1", "Name", false)));
        Assert.Null(tokens.Validate("garbage"));
        Assert.Throws<ArgumentException>(() => new TokenService("short"));

        string a = PasswordHasher.Hash("hunter2hunter2");
        Assert.NotEqual(a, PasswordHasher.Hash("hunter2hunter2"));
        Assert.True(PasswordHasher.Verify("hunter2hunter2", a));
        Assert.False(PasswordHasher.Verify("hunter3hunter3", a));
        Assert.False(PasswordHasher.Verify("x", "not-a-hash"));
    }

    [Fact]
    public async Task Private_room_match_is_played_persisted_and_replayable()
    {
        await using var server = Fast();
        var alice = await server.Guest("Alice");
        var bob = await server.Guest("Bob");
        var created = await alice.Http.PostAsJsonAsync("/api/rooms", new { name = "Friday", preset = "classic_plus", maxPlayers = 4 });
        string code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        Assert.Equal(6, code.Length);
        var info = await bob.Http.GetFromJsonAsync<JsonElement>($"/api/rooms/{code.ToLowerInvariant()}");
        Assert.Equal("Friday", info.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.GetAsync("/api/rooms/NOPE00")).StatusCode);

        var table = new Table();
        var a = await table.Add(server.Connect(alice, code));
        var b = await table.Add(server.Connect(bob, code));
        var watcher = await table.Add(server.Connect(await server.Guest("Watcher"), code, PeerRole.Spectator));
        Assert.True(await table.Until(() => a.Lobby?.Seats.Count == 2 && b.Lobby?.Seats.Count == 2));
        Assert.True(a.IsHost);
        Assert.Equal(alice.UserId, a.Lobby!.Seats[0].UserId);

        var rules = RulePresets.Get("classic_plus");
        rules.MaximumRounds = 15;
        a.UpdateSettings(rules, null);
        a.AddBot(BotLevel.Medium);
        b.SetReady(true);
        Assert.True(await table.Until(() => a.Lobby!.Seats.Count == 3 && a.Lobby.Seats[1].Ready && a.Lobby.Rules.MaximumRounds == 15));
        a.StartMatch();
        Assert.True(await table.Until(() => a.State?.IsOver == true && b.State?.IsOver == true && watcher.State?.IsOver == true, 120));
        Assert.Equal(StateHasher.Hash(a.State!), StateHasher.Hash(b.State!));
        Assert.Equal(StateHasher.Hash(a.State!), StateHasher.Hash(watcher.State!));
        string matchId = a.State!.MatchId;

        await server.Rooms.FlushAsync();
        await Task.Delay(50);
        await server.Rooms.FlushAsync();

        var recent = await alice.Http.GetFromJsonAsync<JsonElement>("/api/matches/recent");
        var match = recent.EnumerateArray().Single();
        Assert.Equal(matchId, match.GetProperty("matchId").GetString());
        Assert.Equal(3, match.GetProperty("players").GetArrayLength());

        var me = await alice.Http.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(1, me.GetProperty("profile").GetProperty("stats").GetProperty("gamesPlayed").GetInt32());
        var players = await bob.Http.GetFromJsonAsync<JsonElement>("/api/recent-players");
        Assert.Equal(alice.UserId, players.EnumerateArray().Single().GetProperty("userId").GetString());

        string replayJson = await alice.Http.GetStringAsync($"/api/matches/{matchId}/replay");
        var replay = new ReplayPlayer(ReplayFile.FromJson(replayJson));
        replay.Seek(replay.Length);
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(replay.State));
        Assert.NotEmpty(MatchStory.Build(ReplayFile.FromJson(replayJson)));
    }

    [Fact]
    public async Task Unauthenticated_sockets_are_refused()
    {
        await using var server = Fast();
        var alice = await server.Guest("Alice");
        string code = (await (await alice.Http.PostAsJsonAsync("/api/rooms", new { })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        var table = new Table();
        var intruder = server.Connect(alice with { Token = "forged.token" }, code);
        Reject? rejected = null;
        intruder.Rejected += r => rejected = r;
        await table.Add(intruder);
        Assert.True(await table.Until(() => rejected != null, 10));
        Assert.Equal(RejectCode.BadToken, rejected!.Code);
    }

    [Fact]
    public async Task Quick_match_fills_with_bots_and_starts_by_itself()
    {
        await using var server = Fast();
        var alice = await server.Guest("Alice");
        var response = await alice.Http.PostAsJsonAsync("/api/matchmaking/quick", new { preset = "blitz" });
        string code = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        var bob = await server.Guest("Bob");
        string second = (await (await bob.Http.PostAsJsonAsync("/api/matchmaking/quick", new { preset = "blitz" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        var table = new Table();
        var a = await table.Add(server.Connect(alice, code));
        Assert.True(await table.Until(() => a.Lobby != null, 10));
        Assert.False(a.IsHost);
        a.SetReady(true);
        Assert.True(await table.Until(() => a.State != null, 30));
        Assert.True(a.State!.Players.Count >= 3);
        Assert.Equal(2, a.State.Players.Count(p => p.IsBot));
        Assert.True(await table.Until(() => a.State.IsOver, 120));
        Assert.NotEqual(HttpStatusCode.OK, (await bob.Http.PostAsJsonAsync("/api/matchmaking/unknown", new { })).StatusCode);
        Assert.Equal(6, second.Length);
    }

    [Fact]
    public async Task Ranked_matches_need_accounts_and_move_ratings()
    {
        await using var server = Fast();
        var guest = await server.Guest("Guest");
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.Http.PostAsJsonAsync("/api/matchmaking/ranked", new { })).StatusCode);

        var p1 = await server.Register("ranked_one");
        var p2 = await server.Register("ranked_two");
        string code = (await (await p1.Http.PostAsJsonAsync("/api/matchmaking/ranked", new { })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        string code2 = (await (await p2.Http.PostAsJsonAsync("/api/matchmaking/ranked", new { })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        Assert.Equal(code, code2);
        lock (server.Rooms.Find(code)!.Gate) server.Rooms.Find(code)!.Host.Lobby.Rules.MaximumRounds = 10;

        var table = new Table();
        var a = await table.Add(server.Connect(p1, code));
        var b = await table.Add(server.Connect(p2, code));
        Assert.True(await table.Until(() => a.Lobby?.Seats.Count == 2, 10));
        a.SetReady(true);
        b.SetReady(true);
        Assert.True(await table.Until(() => a.State?.IsOver == true, 240));
        await server.Rooms.FlushAsync();
        await Task.Delay(50);
        await server.Rooms.FlushAsync();

        var u1 = (await server.Store.GetUserAsync(p1.UserId))!;
        var u2 = (await server.Store.GetUserAsync(p2.UserId))!;
        Assert.Equal(2000, u1.Rating + u2.Rating);
        Assert.NotEqual(u1.Rating, u2.Rating);
        Assert.Equal(1, u1.Stats.RankedGames);
        var board = await p1.Http.GetFromJsonAsync<JsonElement>("/api/leaderboard");
        Assert.Equal(2, board.GetArrayLength());
        Assert.True(board[0].GetProperty("rating").GetInt32() > 1000);
        var winner = u1.Rating > u2.Rating ? u1 : u2;
        Assert.Contains("first_win", await server.Store.AchievementsAsync(winner.Id));
    }

    [Fact]
    public void Multiplayer_elo_is_zero_sum_and_rewards_upsets()
    {
        int[] even = Ratings.Changes(new[] { (1000, 1), (1000, 2), (1000, 3), (1000, 4) });
        Assert.Equal(0, even.Sum());
        Assert.True(even[0] > 0 && even[3] < 0 && even[0] > even[1]);
        int[] upset = Ratings.Changes(new[] { (800, 1), (1400, 2) });
        int[] expected = Ratings.Changes(new[] { (1400, 1), (800, 2) });
        Assert.True(upset[0] > expected[0]);
        Assert.Empty(Ratings.Changes(Array.Empty<(int, int)>()));
    }

    [Fact]
    public async Task Friends_presence_and_invites()
    {
        await using var server = Fast();
        var a = await server.Register("friend_a");
        var b = await server.Register("friend_b");
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.PostAsJsonAsync("/api/friends", new { username = "nobody" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Http.PostAsJsonAsync("/api/friends", new { username = "friend_b" })).StatusCode);
        var list = await b.Http.GetFromJsonAsync<JsonElement>("/api/friends");
        Assert.Equal(a.UserId, list[0].GetProperty("userId").GetString());
        Assert.False(list[0].GetProperty("online").GetBoolean());

        string code = (await (await a.Http.PostAsJsonAsync("/api/rooms", new { @public = true })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        var table = new Table();
        var client = await table.Add(server.Connect(a, code));
        Assert.True(await table.Until(() => client.Lobby != null, 10));
        list = await b.Http.GetFromJsonAsync<JsonElement>("/api/friends");
        Assert.True(list[0].GetProperty("online").GetBoolean());
        Assert.Equal(code, list[0].GetProperty("room").GetString());

        Assert.Equal(HttpStatusCode.Accepted, (await a.Http.PostAsJsonAsync("/api/invites", new { userId = b.UserId, roomCode = code })).StatusCode);
        var me = await b.Http.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(code, me.GetProperty("invites")[0].GetProperty("roomCode").GetString());
        var open = await b.Http.GetFromJsonAsync<JsonElement>("/api/rooms");
        Assert.Equal(code, open[0].GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await b.Http.DeleteAsync($"/api/friends/{a.UserId}")).StatusCode);
        Assert.Equal(0, (await a.Http.GetFromJsonAsync<JsonElement>("/api/friends")).GetArrayLength());
    }

    [Fact]
    public async Task Unfinished_matches_are_recovered_after_a_server_restart()
    {
        var store = new InMemoryStore();
        string code;
        string matchId;
        int versionAtCrash;
        Session alice, bob;

        var first = Fast(store);
        {
            alice = await first.Guest("Alice");
            bob = await first.Guest("Bob");
            code = (await (await alice.Http.PostAsJsonAsync("/api/rooms", new { preset = "tycoon" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
            var table = new Table();
            var a = await table.Add(first.Connect(alice, code));
            var b = await table.Add(first.Connect(bob, code));
            Assert.True(await table.Until(() => a.Lobby?.Seats.Count == 2, 10));
            var rules = RulePresets.Get("tycoon");
            rules.MaximumRounds = 25;
            a.UpdateSettings(rules, null);
            a.AddBot(BotLevel.Medium);
            b.SetReady(true);
            Assert.True(await table.Until(() => a.Lobby!.Seats.Count == 3 && a.Lobby.Seats[1].Ready && a.Lobby.Rules.MaximumRounds == 25, 10));
            a.StartMatch();
            Assert.True(await table.Until(() => a.State is { Round: >= 4 }, 120));
            table.Autoplay = false;
            await first.Rooms.FlushAsync();
            matchId = a.State!.MatchId;
            versionAtCrash = a.State.Version;
        }
        // Tokens are signed with a per-process secret in development, so keep the same one.
        string secret = first.Options.TokenSecret;
        await first.DisposeAsync();

        Environment.SetEnvironmentVariable("BoardEmpire__TokenSecret", secret);
        Environment.SetEnvironmentVariable("BoardEmpire__BotDelaySeconds", "0");
        try
        {
            await using var second = Fast(store);
            var room = second.Rooms.Find(code);
            Assert.NotNull(room);
            Assert.Equal(matchId, room!.Host.Config!.MatchId);
            Assert.True(room.Host.Engine!.State.Version <= versionAtCrash + 50);

            var table = new Table();
            var a = await table.Add(second.Connect(alice, code));
            var b = await table.Add(second.Connect(bob, code));
            Assert.True(await table.Until(() => a.State != null && b.State != null, 15));
            Assert.Equal(new[] { 0 }, a.Seats);
            Assert.Equal(new[] { 1 }, b.Seats);
            Assert.True(await table.Until(() => a.State!.IsOver && b.State!.IsOver, 240));
            Assert.Equal(StateHasher.Hash(a.State!), StateHasher.Hash(b.State!));
            await second.Rooms.FlushAsync();
            await Task.Delay(50);
            await second.Rooms.FlushAsync();
            Assert.Empty(await store.LoadActiveMatchesAsync());
            Assert.Single(await store.RecentMatchesAsync(alice.UserId, 5));
        }
        finally
        {
            Environment.SetEnvironmentVariable("BoardEmpire__TokenSecret", null);
            Environment.SetEnvironmentVariable("BoardEmpire__BotDelaySeconds", null);
        }
    }
}
