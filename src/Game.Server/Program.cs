using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Game.Core.Board;
using Game.Core.Replay;
using Game.Core.Rules;
using Game.Net;
using Game.Protocol;
using Game.Server.Auth;
using Game.Server.Data;
using Game.Server.Rooms;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("BoardEmpire").Get<ServerOptions>() ?? new ServerOptions();
if (options.TokenSecret.Length < 16)
{
    if (!builder.Environment.IsDevelopment() && Environment.GetEnvironmentVariable("BOARDEMPIRE_ALLOW_EPHEMERAL_SECRET") != "1")
        throw new InvalidOperationException("Set BoardEmpire__TokenSecret (16+ characters) before running in production.");
    options.TokenSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    Console.WriteLine("warning: using an ephemeral token secret; sessions will not survive a restart.");
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new TokenService(options.TokenSecret));
builder.Services.AddSingleton<IStore>(_ => options.Postgres.Length > 0 ? new PostgresStore(options.Postgres) : new InMemoryStore());
builder.Services.AddSingleton<IPresence>(_ => options.Redis.Length > 0 ? new RedisPresence(options.Redis) : new MemoryPresence());
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RoomManager>());
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseRateLimiter();

var store = app.Services.GetRequiredService<IStore>();
await store.InitializeAsync();
var rooms = app.Services.GetRequiredService<RoomManager>();
await rooms.RecoverAsync();
var tokens = app.Services.GetRequiredService<TokenService>();
var presence = app.Services.GetRequiredService<IPresence>();

TokenClaims? Auth(HttpContext context)
{
    string header = context.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? tokens.Validate(header[7..].Trim()) : null;
}

static string CleanName(string? name, string fallback)
{
    name = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
    if (name.Length == 0) name = fallback;
    return name.Length > 16 ? name[..16] : name;
}

static object Profile(UserRecord u) => new
{
    userId = u.Id, username = u.Username, displayName = u.DisplayName, guest = u.IsGuest, rating = u.Rating,
    avatar = u.Avatar, stats = u.Stats, unlocks = u.Unlocks,
    averagePosition = u.Stats.GamesPlayed > 0 ? Math.Round((double)u.Stats.RankTotal / u.Stats.GamesPlayed, 2) : 0,
};

static object Summary(ServerRoom room)
{
    lock (room.Gate)
    {
        var lobby = room.Host.Lobby;
        return new
        {
            code = room.Code, name = lobby.RoomName, players = lobby.Seats.Count, maxPlayers = room.Host.Options.MaxSeats,
            inMatch = lobby.InMatch, preset = lobby.Rules.PresetName, board = lobby.BoardId, ranked = lobby.Ranked,
            spectators = lobby.Spectators,
        };
    }
}

static bool IsOpen(ServerRoom room)
{
    lock (room.Gate) return room.Public && room.Host.Engine == null;
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", rooms = rooms.RoomCount, pendingWrites = rooms.PendingWrites }));

app.MapGet("/api/version", () => Results.Ok(new
{
    protocol = ProtocolInfo.Version, minimum = ProtocolInfo.MinimumSupported,
    presets = RulePresets.Ids, boards = BoardLibrary.BoardIds,
}));

// ---------------------------------------------------------------------- accounts

app.MapPost("/api/auth/guest", async (GuestRequest request) =>
{
    var user = new UserRecord { Id = Guid.NewGuid().ToString("N"), DisplayName = CleanName(request.Name, "Guest"), IsGuest = true };
    await store.CreateUserAsync(user);
    return Results.Ok(new { userId = user.Id, token = tokens.Issue(user.Id, user.DisplayName, true), displayName = user.DisplayName, guest = true });
}).RequireRateLimiting("auth");

app.MapPost("/api/auth/register", async (RegisterRequest request) =>
{
    string username = (request.Username ?? "").Trim();
    if (username.Length < 3 || username.Length > 20 || !username.All(c => char.IsLetterOrDigit(c) || c == '_'))
        return Results.BadRequest(new { error = "Usernames are 3-20 letters, digits or underscores" });
    if ((request.Password ?? "").Length < 8) return Results.BadRequest(new { error = "Passwords need at least 8 characters" });
    var user = new UserRecord
    {
        Id = Guid.NewGuid().ToString("N"), Username = username, DisplayName = CleanName(request.DisplayName, username),
        PasswordHash = PasswordHasher.Hash(request.Password!),
    };
    if (!await store.CreateUserAsync(user)) return Results.Conflict(new { error = "That username is taken" });
    return Results.Ok(new { userId = user.Id, token = tokens.Issue(user.Id, user.DisplayName, false), displayName = user.DisplayName, guest = false });
}).RequireRateLimiting("auth");

app.MapPost("/api/auth/login", async (LoginRequest request) =>
{
    var user = await store.GetUserByNameAsync((request.Username ?? "").Trim());
    if (user == null || user.IsGuest || !PasswordHasher.Verify(request.Password ?? "", user.PasswordHash))
        return Results.Json(new { error = "Wrong username or password" }, statusCode: StatusCodes.Status401Unauthorized);
    return Results.Ok(new { userId = user.Id, token = tokens.Issue(user.Id, user.DisplayName, false), displayName = user.DisplayName, guest = false });
}).RequireRateLimiting("auth");

app.MapGet("/api/me", async (HttpContext context) =>
{
    var claims = Auth(context);
    if (claims == null) return Results.Unauthorized();
    var user = await store.GetUserAsync(claims.UserId);
    if (user == null) return Results.Unauthorized();
    return Results.Ok(new
    {
        profile = Profile(user),
        achievements = await store.AchievementsAsync(user.Id),
        catalog = Achievements.All,
        invites = await presence.InvitesAsync(user.Id),
    });
});

app.MapPut("/api/me", async (HttpContext context, ProfileUpdate update) =>
{
    var claims = Auth(context);
    if (claims == null) return Results.Unauthorized();
    var user = await store.GetUserAsync(claims.UserId);
    if (user == null) return Results.Unauthorized();
    if (update.DisplayName != null) user.DisplayName = CleanName(update.DisplayName, user.DisplayName);
    if (update.Avatar is { } avatar) user.Avatar = Math.Clamp(avatar, 0, 15);
    await store.UpdateUserAsync(user);
    return Results.Ok(new { profile = Profile(user), token = tokens.Issue(user.Id, user.DisplayName, user.IsGuest) });
});

app.MapGet("/api/stats/{userId}", async (string userId) =>
{
    var user = await store.GetUserAsync(userId);
    return user == null ? Results.NotFound() : Results.Ok(new { profile = Profile(user), achievements = await store.AchievementsAsync(userId) });
});

app.MapGet("/api/leaderboard", async () =>
    Results.Ok((await store.LeaderboardAsync(50)).Select((u, i) => new
    {
        rank = i + 1, userId = u.Id, displayName = u.DisplayName, rating = u.Rating, games = u.Stats.RankedGames, wins = u.Stats.Wins,
    })));

// ---------------------------------------------------------------------- social

app.MapGet("/api/friends", async (HttpContext context) =>
{
    var claims = Auth(context);
    if (claims == null) return Results.Unauthorized();
    var friends = await store.FriendsAsync(claims.UserId);
    var online = await presence.GetAsync(friends.Select(f => f.Id));
    return Results.Ok(friends.Select(f => new
    {
        userId = f.Id, displayName = f.DisplayName, rating = f.Rating, online = online.ContainsKey(f.Id), room = online.GetValueOrDefault(f.Id, ""),
    }));
});

app.MapPost("/api/friends", async (HttpContext context, FriendRequest request) =>
{
    var claims = Auth(context);
    if (claims == null) return Results.Unauthorized();
    var friend = !string.IsNullOrEmpty(request.UserId)
        ? await store.GetUserAsync(request.UserId)
        : await store.GetUserByNameAsync(request.Username ?? "");
    if (friend == null || friend.Id == claims.UserId) return Results.NotFound(new { error = "Player not found" });
    await store.AddFriendAsync(claims.UserId, friend.Id);
    return Results.Ok(new { userId = friend.Id, displayName = friend.DisplayName });
});

app.MapDelete("/api/friends/{friendId}", async (HttpContext context, string friendId) =>
{
    var claims = Auth(context);
    if (claims == null) return Results.Unauthorized();
    await store.RemoveFriendAsync(claims.UserId, friendId);
    return Results.NoContent();
});

app.MapGet("/api/recent-players", async (HttpContext context) =>
{
    var claims = Auth(context);
    if (claims == null) return Results.Unauthorized();
    return Results.Ok((await store.RecentPlayersAsync(claims.UserId)).Select(u => new { userId = u.Id, displayName = u.DisplayName, rating = u.Rating }));
});

app.MapPost("/api/invites", async (HttpContext context, InviteRequest request) =>
{
    var claims = Auth(context);
    if (claims == null) return Results.Unauthorized();
    if (rooms.Find(request.RoomCode ?? "") == null) return Results.NotFound(new { error = "Room not found" });
    await presence.InviteAsync(claims.UserId, claims.Name, request.UserId ?? "", request.RoomCode!.ToUpperInvariant());
    return Results.Accepted();
});

// ---------------------------------------------------------------------- rooms

app.MapPost("/api/rooms", (HttpContext context, CreateRoomRequest request) =>
{
    var claims = Auth(context);
    if (claims == null) return Results.Unauthorized();
    string preset = RulePresets.Ids.Contains(request.Preset ?? "") ? request.Preset! : "classic";
    string board = BoardLibrary.BoardIds.Contains(request.Board ?? "") ? request.Board! : BoardLibrary.DefaultBoardId;
    var room = rooms.Create(CleanName(request.Name, claims.Name + "'s game"), request.Rules ?? RulePresets.Get(preset), board,
        request.MaxPlayers ?? 6, request.Public ?? false, managed: false, ranked: false);
    return room == null
        ? Results.Json(new { error = "The server is full" }, statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new { code = room.Code });
});

app.MapGet("/api/rooms", () => Results.Ok(rooms.Rooms.Where(IsOpen).Select(Summary).ToList()));

app.MapGet("/api/rooms/{code}", (string code) =>
{
    var room = rooms.Find(code);
    return room == null ? Results.NotFound(new { error = "Room not found" }) : Results.Ok(Summary(room));
});

app.MapPost("/api/matchmaking/{kind}", (HttpContext context, string kind, MatchmakingRequest? request) =>
{
    var claims = Auth(context);
    if (claims == null) return Results.Unauthorized();
    bool ranked = kind == "ranked";
    if (!ranked && kind != "quick") return Results.NotFound();
    if (ranked && claims.Guest) return Results.Json(new { error = "Ranked play needs an account" }, statusCode: StatusCodes.Status403Forbidden);
    string preset = ranked ? "classic_plus" : RulePresets.Ids.Contains(request?.Preset ?? "") ? request!.Preset! : "quick";
    var room = rooms.FindOrCreateMatch(preset, ranked);
    return room == null
        ? Results.Json(new { error = "The server is full" }, statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new { code = room.Code });
});

// ---------------------------------------------------------------------- matches

app.MapGet("/api/matches/recent", async (HttpContext context) =>
{
    var claims = Auth(context);
    if (claims == null) return Results.Unauthorized();
    return Results.Ok(await store.RecentMatchesAsync(claims.UserId, 20));
});

app.MapGet("/api/matches/{matchId}/replay", async (string matchId) =>
{
    var data = await store.GetReplayAsync(matchId);
    if (data == null) return Results.NotFound();
    var save = RoomSave.FromJson(data.SnapshotJson);
    var replay = new ReplayFile { Board = save.Board, Initial = save.Initial, Events = data.Events };
    return Results.Text(replay.ToJson(), "application/json");
});

// ---------------------------------------------------------------------- realtime

app.Map("/ws/{code}", async (HttpContext context, string code) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    var room = rooms.Find(code);
    if (room == null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    var claims = tokens.Validate(context.Request.Query["token"]);
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    if (claims != null) await presence.SetOnlineAsync(claims.UserId, room.Code);
    try
    {
        await room.Transport.RunAsync(socket, context.Connection.RemoteIpAddress?.ToString() ?? "", context.RequestAborted);
    }
    finally
    {
        if (claims != null) await presence.SetOfflineAsync(claims.UserId);
    }
});

app.Run();

public sealed record GuestRequest(string? Name);
public sealed record RegisterRequest(string? Username, string? Password, string? DisplayName);
public sealed record LoginRequest(string? Username, string? Password);
public sealed record ProfileUpdate(string? DisplayName, int? Avatar);
public sealed record FriendRequest(string? Username, string? UserId);
public sealed record InviteRequest(string? UserId, string? RoomCode);
public sealed record CreateRoomRequest(string? Name, string? Preset, string? Board, int? MaxPlayers, bool? Public, GameRules? Rules);
public sealed record MatchmakingRequest(string? Preset);

public partial class Program;
