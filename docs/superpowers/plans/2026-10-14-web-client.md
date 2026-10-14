# BoardEmpire Web Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players join and play full BoardEmpire matches in a browser at `https://monopoly.algosculptor.com`, with 2D and 3D board views.

**Architecture:** A Blazor WebAssembly app (`src/Game.Web`) reuses `Game.Core`/`Game.Protocol`/`Game.Net` unchanged through a small non-UI library (`src/Game.WebClient`) that wraps `RoomClient` over a browser WebSocket. The 3D board is a vendored three.js module driven through JS interop. Caddy serves the published static files at `/` and keeps proxying `/api/*` and `/ws/*` to `127.0.0.1:8091`.

**Tech Stack:** .NET 8, Blazor WebAssembly (no AOT), xunit, three.js 0.160.0 (vendored), Playwright (smoke test), Caddy.

**Spec:** `docs/superpowers/specs/2026-10-14-web-client-design.md`

## Global Constraints

- `Game.Core`, `Game.Protocol`, `Game.Net`, `Game.Server` behavior and the wire protocol are not changed (spec Non-goals).
- Target framework `net8.0`, `<Nullable>enable</Nullable>` in every new project (matches existing projects).
- Blazor WASM without AOT (no `wasm-tools` workload on this host).
- Same-origin hosting: the browser connects to `/api/*` and `/ws/{code}?token=` on its own origin. No CORS configuration.
- Views only animate; they never mutate state or send commands except through `GameSession.Client.SendCommand`.
- three.js is vendored and version-pinned (0.160.0); no runtime CDN requests.
- Secrets never in git: no tokens or passwords committed.
- `Game.Core` invariants in `AGENTS.md` still hold; this plan adds no rules code.
- Release: version bump via `tools/set-version.sh`, new tag for every pushed code change, push the tag only, never force push.

## Review Focus

1. Browser `ClientWebSocket` rejects options such as `KeepAliveInterval`; the connector must not set them (Task 3 pins this with a test on the connector factory).
2. Snapshot or hash-mismatch resync while events are queued in the pacer must discard the queue and reset `Display` to the snapshot (Task 2 + 3 tests).
3. Page refresh mid-match must resume the same seat via the stored `ResumeToken` instead of joining as a new player (Task 3 test).
4. `Reject` with a protocol version mismatch must surface as an "update required" state, not a silent hang (Task 3 test).
5. WebGL unavailable or three.js init throwing must fall back to the 2D board without losing the match (Task 8).

---

## File Structure

```
src/Game.WebClient/Game.WebClient.csproj      non-UI library (net8.0)
src/Game.WebClient/ApiClient.cs               REST client + DTOs
src/Game.WebClient/EventPacer.cs              paces events for animation
src/Game.WebClient/GameSession.cs             RoomClient + display state + tick loop
src/Game.WebClient/BrowserSocket.cs           WebSocket connector safe for browsers
src/Game.WebClient/DisplayPayload.cs          JSON for the 3D module
src/Game.WebClient/TileGrid.cs                tile index -> grid cell
src/Game.Web/Game.Web.csproj                  Blazor WASM app
src/Game.Web/Program.cs, App.razor, _Imports.razor
src/Game.Web/wwwroot/index.html, css/app.css
src/Game.Web/wwwroot/js/board3d.js            three.js board module
src/Game.Web/wwwroot/js/vendor/three.module.min.js
src/Game.Web/Pages/Home.razor, Lobby.razor, Play.razor
src/Game.Web/Components/Board2D.razor, Board3D.razor, Hud.razor, Dialogs.razor
tests/Web.Tests/Web.Tests.csproj, *.cs        tests for Game.WebClient
tools/deploy-web.sh                           publish + copy to Caddy-served dir
tools/web-smoke/                              Playwright end-to-end smoke test
deploy/Caddyfile.snippet                      updated site block
```

---

### Task 1: Projects scaffold and ApiClient

**Files:**
- Create: `src/Game.WebClient/Game.WebClient.csproj`, `src/Game.WebClient/ApiClient.cs`
- Create: `tests/Web.Tests/Web.Tests.csproj`, `tests/Web.Tests/ApiClientTests.cs`
- Modify: `BoardEmpire.sln`

**Interfaces:**
- Produces:
  - `record AuthResult(string UserId, string Token, string DisplayName, bool Guest)`
  - `record RoomSummary(string Code, string Name, int Players, int MaxPlayers, bool InMatch, string Preset, string Board, bool Ranked, int Spectators)`
  - `sealed class ApiClient(HttpClient http)` with `string Token { get; set; }`, `Task<AuthResult> GuestAsync(string name)`, `Task<AuthResult> LoginAsync(string username, string password)`, `Task<AuthResult> RegisterAsync(string username, string password, string displayName)`, `Task<IReadOnlyList<RoomSummary>> RoomsAsync()`, `Task<string> CreateRoomAsync(string name, string preset, string board, int maxPlayers, bool isPublic)`, `Task<string> QuickMatchAsync(string preset)`, `Task<bool> RoomExistsAsync(string code)`
  - `class ApiException(string message, int status) : Exception` with `int Status`

- [ ] **Step 1: Create the projects**

`src/Game.WebClient/Game.WebClient.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Game.Core\Game.Core.csproj" />
    <ProjectReference Include="..\Game.Protocol\Game.Protocol.csproj" />
    <ProjectReference Include="..\Game.Net\Game.Net.csproj" />
  </ItemGroup>
</Project>
```

`tests/Web.Tests/Web.Tests.csproj` (copy `Net.Tests.csproj`'s package versions):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Game.Core\Game.Core.csproj" />
    <ProjectReference Include="..\..\src\Game.Protocol\Game.Protocol.csproj" />
    <ProjectReference Include="..\..\src\Game.Net\Game.Net.csproj" />
    <ProjectReference Include="..\..\src\Game.WebClient\Game.WebClient.csproj" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln BoardEmpire.sln add src/Game.WebClient/Game.WebClient.csproj tests/Web.Tests/Web.Tests.csproj
```

- [ ] **Step 2: Write the failing test**

`tests/Web.Tests/ApiClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public sealed class FakeHandler : HttpMessageHandler
{
    public HttpRequestMessage? Last;
    public string? LastBody;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string Body = "{}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        Last = request;
        LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancel);
        return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
    }
}

public class ApiClientTests
{
    private static (ApiClient api, FakeHandler h) Make()
    {
        var h = new FakeHandler();
        return (new ApiClient(new HttpClient(h) { BaseAddress = new Uri("https://example.test/") }), h);
    }

    [Fact]
    public async Task Guest_posts_name_and_returns_token()
    {
        var (api, h) = Make();
        h.Body = "{\"userId\":\"u1\",\"token\":\"tok\",\"displayName\":\"Ann\",\"guest\":true}";
        var r = await api.GuestAsync("Ann");
        Assert.Equal("/api/auth/guest", h.Last!.RequestUri!.AbsolutePath);
        Assert.Contains("\"name\":\"Ann\"", h.LastBody);
        Assert.Equal("tok", r.Token);
        Assert.True(r.Guest);
    }

    [Fact]
    public async Task Authenticated_calls_send_bearer_token()
    {
        var (api, h) = Make();
        api.Token = "tok";
        h.Body = "[]";
        await api.RoomsAsync();
        Assert.Equal("Bearer", h.Last!.Headers.Authorization!.Scheme);
        Assert.Equal("tok", h.Last.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Error_body_becomes_ApiException_with_server_message()
    {
        var (api, h) = Make();
        h.Status = HttpStatusCode.Unauthorized;
        h.Body = "{\"error\":\"Wrong username or password\"}";
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync("a", "b"));
        Assert.Equal(401, ex.Status);
        Assert.Equal("Wrong username or password", ex.Message);
    }

    [Fact]
    public async Task RoomExists_is_false_on_404()
    {
        var (api, h) = Make();
        h.Status = HttpStatusCode.NotFound;
        h.Body = "{\"error\":\"Room not found\"}";
        Assert.False(await api.RoomExistsAsync("ABCD"));
    }

    [Fact]
    public async Task Rooms_parse_camel_case_summaries()
    {
        var (api, h) = Make();
        h.Body = "[{\"code\":\"ABCD\",\"name\":\"Ann's game\",\"players\":2,\"maxPlayers\":6,\"inMatch\":false,\"preset\":\"classic\",\"board\":\"neo_city\",\"ranked\":false,\"spectators\":0}]";
        var rooms = await api.RoomsAsync();
        Assert.Single(rooms);
        Assert.Equal("ABCD", rooms[0].Code);
        Assert.Equal(6, rooms[0].MaxPlayers);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Web.Tests -v q`
Expected: FAIL to compile, `ApiClient` not defined.

- [ ] **Step 4: Implement**

`src/Game.WebClient/ApiClient.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Game.WebClient;

public sealed record AuthResult(string UserId, string Token, string DisplayName, bool Guest);

public sealed record RoomSummary(string Code, string Name, int Players, int MaxPlayers, bool InMatch,
    string Preset, string Board, bool Ranked, int Spectators);

public sealed class ApiException : Exception
{
    public int Status { get; }
    public ApiException(string message, int status) : base(message) => Status = status;
}

/// <summary>REST client for the match server. Same-origin in the browser.</summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public string Token { get; set; } = "";

    public ApiClient(HttpClient http) => _http = http;

    public Task<AuthResult> GuestAsync(string name) =>
        Send<AuthResult>(HttpMethod.Post, "api/auth/guest", new { name });

    public Task<AuthResult> LoginAsync(string username, string password) =>
        Send<AuthResult>(HttpMethod.Post, "api/auth/login", new { username, password });

    public Task<AuthResult> RegisterAsync(string username, string password, string displayName) =>
        Send<AuthResult>(HttpMethod.Post, "api/auth/register", new { username, password, displayName });

    public async Task<IReadOnlyList<RoomSummary>> RoomsAsync() =>
        await Send<List<RoomSummary>>(HttpMethod.Get, "api/rooms", null) ?? new();

    public async Task<string> CreateRoomAsync(string name, string preset, string board, int maxPlayers, bool isPublic)
    {
        var r = await Send<CodeResult>(HttpMethod.Post, "api/rooms",
            new { name, preset, board, maxPlayers, @public = isPublic });
        return r.Code;
    }

    public async Task<string> QuickMatchAsync(string preset)
    {
        var r = await Send<CodeResult>(HttpMethod.Post, "api/matchmaking/quick", new { preset });
        return r.Code;
    }

    public async Task<bool> RoomExistsAsync(string code)
    {
        try
        {
            await Send<RoomSummary>(HttpMethod.Get, "api/rooms/" + Uri.EscapeDataString(code), null);
            return true;
        }
        catch (ApiException e) when (e.Status == 404)
        {
            return false;
        }
    }

    private sealed record CodeResult(string Code);

    private async Task<T> Send<T>(HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        if (Token.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (body != null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string message = response.ReasonPhrase ?? "Request failed";
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorBody>(Json);
                if (!string.IsNullOrEmpty(error?.Error)) message = error.Error;
            }
            catch (JsonException) { }
            throw new ApiException(message, (int)response.StatusCode);
        }
        var result = await response.Content.ReadFromJsonAsync<T>(Json);
        return result ?? throw new ApiException("Empty response", (int)HttpStatusCode.BadGateway);
    }

    private sealed record ErrorBody(string? Error);
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Web.Tests -v q`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add BoardEmpire.sln src/Game.WebClient tests/Web.Tests
git commit -m "feat(web): add WebClient library with REST ApiClient"
```

---

### Task 2: EventPacer and TileGrid

**Files:**
- Create: `src/Game.WebClient/EventPacer.cs`, `src/Game.WebClient/TileGrid.cs`
- Test: `tests/Web.Tests/EventPacerTests.cs`, `tests/Web.Tests/TileGridTests.cs`

**Interfaces:**
- Produces:
  - `sealed class EventPacer` with `int Pending`, `void Enqueue(IEnumerable<GameEvent> events)`, `void Clear()`, `IReadOnlyList<GameEvent> Advance(double dt)`, `static double DelayFor(GameEvent e, int backlog)`
  - `static class TileGrid`: `static (int Row, int Col) Cell(int tile, int side)` where `side = tileCount / 4`; grid is `(side + 1) x (side + 1)`.

- [ ] **Step 1: Write the failing tests**

`tests/Web.Tests/TileGridTests.cs`:

```csharp
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class TileGridTests
{
    [Fact]
    public void Corners_follow_the_board_loop_for_side_10()
    {
        Assert.Equal((10, 10), TileGrid.Cell(0, 10));
        Assert.Equal((10, 0), TileGrid.Cell(10, 10));
        Assert.Equal((0, 0), TileGrid.Cell(20, 10));
        Assert.Equal((0, 10), TileGrid.Cell(30, 10));
    }

    [Fact]
    public void Every_tile_gets_a_distinct_perimeter_cell()
    {
        var cells = Enumerable.Range(0, 40).Select(i => TileGrid.Cell(i, 10)).ToHashSet();
        Assert.Equal(40, cells.Count);
        Assert.All(cells, c => Assert.True(c.Row is 0 or 10 || c.Col is 0 or 10));
    }
}
```

`tests/Web.Tests/EventPacerTests.cs`:

```csharp
using Game.Core.Events;
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class EventPacerTests
{
    private static GameEvent Roll() => new DiceRolled(0, 3, 4, 0);

    [Fact]
    public void First_event_is_released_immediately_then_the_next_after_its_delay()
    {
        var p = new EventPacer();
        p.Enqueue(new[] { Roll(), Roll() });
        Assert.Single(p.Advance(0));
        Assert.Empty(p.Advance(0.1));
        Assert.Single(p.Advance(EventPacer.DelayFor(Roll(), 1)));
        Assert.Equal(0, p.Pending);
    }

    [Fact]
    public void Large_backlog_catches_up_faster()
    {
        Assert.True(EventPacer.DelayFor(Roll(), 60) < EventPacer.DelayFor(Roll(), 1));
    }

    [Fact]
    public void Clear_drops_queued_events()
    {
        var p = new EventPacer();
        p.Enqueue(new[] { Roll(), Roll(), Roll() });
        p.Advance(0);
        p.Clear();
        Assert.Equal(0, p.Pending);
        Assert.Empty(p.Advance(10));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Web.Tests -v q`
Expected: FAIL to compile (`TileGrid`, `EventPacer` missing).

- [ ] **Step 3: Implement**

`src/Game.WebClient/TileGrid.cs`:

```csharp
namespace Game.WebClient;

/// <summary>Maps a tile index to a cell on the (side+1) x (side+1) perimeter grid. Tile 0 is bottom-right and the loop runs clockwise.</summary>
public static class TileGrid
{
    public static (int Row, int Col) Cell(int tile, int side)
    {
        int i = ((tile % (side * 4)) + side * 4) % (side * 4);
        if (i < side) return (side, side - i);
        if (i < side * 2) return (side - (i - side), 0);
        if (i < side * 3) return (0, i - side * 2);
        return (i - side * 3, side);
    }
}
```

`src/Game.WebClient/EventPacer.cs`:

```csharp
using Game.Core.Events;

namespace Game.WebClient;

/// <summary>Releases queued events one at a time so views can animate them. The queue is only a presentation concern: the replica state is already ahead.</summary>
public sealed class EventPacer
{
    private readonly Queue<GameEvent> _queue = new();
    private double _wait;

    public int Pending => _queue.Count;

    public void Enqueue(IEnumerable<GameEvent> events)
    {
        foreach (var e in events) _queue.Enqueue(e);
    }

    public void Clear()
    {
        _queue.Clear();
        _wait = 0;
    }

    public IReadOnlyList<GameEvent> Advance(double dt)
    {
        var released = new List<GameEvent>();
        _wait -= dt;
        while (_queue.Count > 0 && _wait <= 0)
        {
            var e = _queue.Dequeue();
            released.Add(e);
            _wait = DelayFor(e, _queue.Count);
        }
        return released;
    }

    public static double DelayFor(GameEvent e, int backlog)
    {
        if (backlog > 40) return 0.02;
        double scale = backlog > 12 ? 0.35 : 1.0;
        double basis = e switch
        {
            DiceRolled => 0.9,
            PlayerMoved => 0.7,
            _ => 0.3,
        };
        return basis * scale;
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Web.Tests -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Game.WebClient tests/Web.Tests
git commit -m "feat(web): add EventPacer and TileGrid"
```

---

### Task 3: GameSession, browser socket, DisplayPayload

**Files:**
- Create: `src/Game.WebClient/GameSession.cs`, `src/Game.WebClient/BrowserSocket.cs`, `src/Game.WebClient/DisplayPayload.cs`
- Test: `tests/Web.Tests/GameSessionTests.cs`, `tests/Web.Tests/BrowserSocketTests.cs`, `tests/Web.Tests/DisplayPayloadTests.cs`

**Interfaces:**
- Consumes: `EventPacer` (Task 2); `RoomClient`, `LoopbackHub`, `HostSession`, `RoomHostOptions` from `Game.Net`.
- Produces:
  - `sealed class GameSession` with: `GameSession(IGameTransport transport, string playerName, string authToken = "", string resumeToken = "")`, `RoomClient Client`, `GameState? Display`, `BoardDefinition? Board`, `string? UpdateRequired` (non-null after a protocol `Reject`), `event Action? Changed`, `event Action<GameEvent, GameState>? EventPlayed`, `Task ConnectAsync()`, `void Tick(double now)`, `Task DisconnectAsync()`.
  - `static class BrowserSocket`: `static WebSocketClientTransport Create(Uri uri)` which builds a `ClientWebSocket` and sets **no** options.
  - `static class DisplayPayload`: `static string State(GameState s)`, `static string Event(GameEvent e)`, `static string Board(BoardDefinition b)`, all via `CoreJson.Options`.

- [ ] **Step 1: Write the failing tests**

`tests/Web.Tests/GameSessionTests.cs`:

```csharp
using Game.Core.Rules;
using Game.Net;
using Game.Net.Transport;
using Game.Protocol;
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class GameSessionTests
{
    private sealed class Rig
    {
        public LoopbackHub Hub = new();
        public HostSession Host;
        public double Now;

        public Rig()
        {
            var o = new RoomHostOptions { RoomCode = "TEST", BotDelaySeconds = 0, FixedSeed = 5, RapidAuctionSeconds = 0 };
            Host = new HostSession(o, Hub);
            Host.StartAsync().GetAwaiter().GetResult();
        }

        public void Pump(GameSession s, double seconds)
        {
            double end = Now + seconds;
            while (Now < end)
            {
                Now += 0.05;
                Host.Pump(Now);
                s.Tick(Now);
            }
        }
    }

    [Fact]
    public async Task Connecting_reaches_the_lobby_and_raises_Changed()
    {
        var rig = new Rig();
        var s = new GameSession(rig.Hub.CreateClient(), "Ann");
        int changed = 0;
        s.Changed += () => changed++;
        await s.ConnectAsync();
        rig.Pump(s, 1);
        Assert.NotNull(s.Client.Lobby);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task Display_follows_events_through_the_pacer_and_reaches_the_replica_state()
    {
        var rig = new Rig();
        var s = new GameSession(rig.Hub.CreateClient(), "Ann");
        await s.ConnectAsync();
        rig.Pump(s, 0.5);
        s.Client.AddBot(BotLevel.Easy);
        s.Client.SetReady(true);
        rig.Pump(s, 0.5);
        s.Client.StartMatch();
        rig.Pump(s, 30);
        Assert.NotNull(s.Display);
        Assert.Equal(s.Client.State!.Version, s.Display!.Version);
    }

    [Fact]
    public async Task Resume_token_reclaims_the_same_seat_after_a_reconnect()
    {
        var rig = new Rig();
        var a = new GameSession(rig.Hub.CreateClient(), "Ann");
        await a.ConnectAsync();
        rig.Pump(a, 0.5);
        var seats = a.Client.Seats;
        string token = a.Client.ResumeToken;
        Assert.NotEmpty(token);
        await a.DisconnectAsync();
        rig.Pump(a, 0.5);

        var b = new GameSession(rig.Hub.CreateClient(), "Ann", resumeToken: token);
        await b.ConnectAsync();
        rig.Pump(b, 1);
        Assert.Equal(seats, b.Client.Seats);
    }

    [Fact]
    public void Update_required_is_null_until_a_version_reject_arrives()
    {
        var rig = new Rig();
        var s = new GameSession(rig.Hub.CreateClient(), "Ann");
        Assert.Null(s.UpdateRequired);
    }
}
```

`tests/Web.Tests/BrowserSocketTests.cs`:

```csharp
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class BrowserSocketTests
{
    [Fact]
    public void Create_returns_a_transport_without_connecting()
    {
        var t = BrowserSocket.Create(new Uri("wss://example.test/ws/ABCD?token=x"));
        Assert.False(t.IsConnected);
    }

    [Fact]
    public void Url_builder_converts_http_origin_to_ws_and_escapes_the_token()
    {
        var uri = BrowserSocket.RoomUri(new Uri("https://monopoly.algosculptor.com/"), "AB CD", "t k+n");
        Assert.Equal("wss", uri.Scheme);
        Assert.Equal("/ws/AB%20CD", uri.AbsolutePath);
        Assert.Contains("token=t%20k%2Bn", uri.Query);
    }
}
```

`tests/Web.Tests/DisplayPayloadTests.cs`:

```csharp
using Game.Core.Board;
using Game.Core.Events;
using Game.WebClient;
using System.Text.Json;
using Xunit;

namespace Web.Tests;

public class DisplayPayloadTests
{
    [Fact]
    public void Event_json_carries_the_type_discriminator_and_fields()
    {
        string json = DisplayPayload.Event(new DiceRolled(1, 3, 4, 0));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("DiceRolled", doc.RootElement.GetProperty("$t").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("D1").GetInt32());
    }

    [Fact]
    public void Board_json_lists_tiles()
    {
        var board = BoardLibrary.Get(BoardLibrary.DefaultBoardId);
        using var doc = JsonDocument.Parse(DisplayPayload.Board(board));
        Assert.Equal(board.Tiles.Count, doc.RootElement.GetProperty("Tiles").GetArrayLength());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Web.Tests -v q`
Expected: FAIL to compile. If `BoardLibrary.Get` is not the real accessor, read `src/Game.Core/Board/BoardLibrary.cs`, use the actual lookup method, and fix the test (not the production code) before continuing.

- [ ] **Step 3: Implement**

`src/Game.WebClient/BrowserSocket.cs`:

```csharp
using System.Net.WebSockets;
using Game.Net.Transport;

namespace Game.WebClient;

/// <summary>Browser-safe WebSocket factory. The browser WebSocket rejects most ClientWebSocketOptions, so none are set.</summary>
public static class BrowserSocket
{
    public static WebSocketClientTransport Create(Uri uri) =>
        new(async cancel =>
        {
            var socket = new ClientWebSocket();
            await socket.ConnectAsync(uri, cancel);
            return socket;
        }, uri.Host);

    public static Uri RoomUri(Uri origin, string code, string token)
    {
        var b = new UriBuilder(origin)
        {
            Scheme = origin.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = "/ws/" + Uri.EscapeDataString(code),
            Query = token.Length == 0 ? "" : "token=" + Uri.EscapeDataString(token),
        };
        return b.Uri;
    }
}
```

`src/Game.WebClient/DisplayPayload.cs`:

```csharp
using System.Text.Json;
using Game.Core.Board;
using Game.Core.Events;
using Game.Core.Serialization;
using Game.Core.State;

namespace Game.WebClient;

/// <summary>JSON handed to the three.js module. States are already redacted for the viewer by the server.</summary>
public static class DisplayPayload
{
    public static string State(GameState s) => JsonSerializer.Serialize(s, CoreJson.Options);
    public static string Event(GameEvent e) => JsonSerializer.Serialize<GameEvent>(e, CoreJson.Options);
    public static string Board(BoardDefinition b) => JsonSerializer.Serialize(b, CoreJson.Options);
}
```

`src/Game.WebClient/GameSession.cs`:

```csharp
using Game.Core.Board;
using Game.Core.Events;
using Game.Core.State;
using Game.Net;
using Game.Net.Transport;
using Game.Protocol;

namespace Game.WebClient;

/// <summary>One browser connection to a room: wraps RoomClient, keeps a paced display copy of the match state and a tick loop.</summary>
public sealed class GameSession : IDisposable
{
    private readonly EventPacer _pacer = new();
    private double _last = -1;

    public RoomClient Client { get; }
    public GameState? Display { get; private set; }
    public BoardDefinition? Board => Client.Board;
    public string? UpdateRequired { get; private set; }

    public event Action? Changed;
    public event Action<GameEvent, GameState>? EventPlayed;

    public GameSession(IGameTransport transport, string playerName, string authToken = "", string resumeToken = "")
    {
        Client = new RoomClient(transport, playerName) { AuthToken = authToken, ResumeToken = resumeToken };
        Client.LobbyChanged += () => Changed?.Invoke();
        Client.PeerStatusChanged += _ => Changed?.Invoke();
        Client.Disconnected += _ => Changed?.Invoke();
        Client.ReturnedToLobby += () =>
        {
            _pacer.Clear();
            Display = null;
            Changed?.Invoke();
        };
        Client.SnapshotLoaded += s =>
        {
            _pacer.Clear();
            Display = s.Clone();
            Changed?.Invoke();
        };
        Client.EventsApplied += events => _pacer.Enqueue(events);
        Client.Rejected += r =>
        {
            if (r.Code == RejectCode.VersionMismatch || r.MinimumVersion > ProtocolInfo.Version)
                UpdateRequired = r.Reason;
            Changed?.Invoke();
        };
    }

    public Task ConnectAsync() => Client.ConnectAsync();

    public Task DisconnectAsync() => Client.DisconnectAsync();

    public void Tick(double now)
    {
        double dt = _last < 0 ? 0 : Math.Max(0, now - _last);
        _last = now;
        Client.Poll(now);
        if (Display == null) return;
        foreach (var e in _pacer.Advance(dt))
        {
            e.Apply(Display);
            Display.Version = e.Version;
            EventPlayed?.Invoke(e, Display);
            Changed?.Invoke();
        }
    }

    public void Dispose() => _ = Client.DisconnectAsync();
}
```

Note for the implementer: confirm `RejectCode.VersionMismatch` exists (`grep -n "enum RejectCode" -A8 src/Game.Protocol/Messages.cs`) and use the real member name for protocol-version rejects. Confirm whether `GameEvent.Apply` already updates `State.Version`; if it does, drop the explicit `Display.Version = e.Version;` line so the version is not set twice.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Web.Tests -v q`
Expected: PASS. If `Display_follows_events...` fails on version equality, inspect whether `Version` is applied inside `Apply` (see the note above) and fix `GameSession.Tick`, not the test.

- [ ] **Step 5: Commit**

```bash
git add src/Game.WebClient tests/Web.Tests
git commit -m "feat(web): add GameSession, browser socket factory and display payload"
```

---

### Task 4: Blazor WASM shell, Home page

**Files:**
- Create: `src/Game.Web/Game.Web.csproj`, `Program.cs`, `App.razor`, `_Imports.razor`, `Services/Session.cs`, `Pages/Home.razor`, `wwwroot/index.html`, `wwwroot/css/app.css`
- Modify: `BoardEmpire.sln`

**Interfaces:**
- Consumes: `ApiClient`, `AuthResult`, `RoomSummary` (Task 1).
- Produces: scoped service `Session` with `ApiClient Api`, `AuthResult? Auth`, `Task<AuthResult> EnsureGuestAsync(string name)`, `string PlayerName`, and routes `/` (Home), `/room/{code}` (Lobby, Task 5), `/play/{code}` (Play, Task 6).

- [ ] **Step 1: Create the project**

`src/Game.Web/Game.Web.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Game.Web</RootNamespace>
    <BlazorWebAssemblyLoadAllGlobalizationData>false</BlazorWebAssemblyLoadAllGlobalizationData>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="8.0.11" />
    <ProjectReference Include="..\Game.WebClient\Game.WebClient.csproj" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln BoardEmpire.sln add src/Game.Web/Game.Web.csproj
```

`wwwroot/index.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>BoardEmpire</title>
  <base href="/" />
  <link rel="stylesheet" href="css/app.css" />
</head>
<body>
  <div id="app"><p class="boot">Loading BoardEmpire…</p></div>
  <script src="_framework/blazor.webassembly.js"></script>
</body>
</html>
```

`Program.cs`:

```csharp
using Game.Web;
using Game.Web.Services;
using Game.WebClient;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped(sp => new ApiClient(sp.GetRequiredService<HttpClient>()));
builder.Services.AddScoped<Session>();
await builder.Build().RunAsync();
```

`_Imports.razor`:

```razor
@using System.Net.Http
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.JSInterop
@using Game.Web
@using Game.Web.Components
@using Game.Web.Services
@using Game.WebClient
@using Game.Core.State
@using Game.Core.Board
@using Game.Core.Commands
@using Game.Core.Events
@using Game.Protocol
```

`App.razor`:

```razor
<Router AppAssembly="typeof(App).Assembly">
  <Found Context="route"><RouteView RouteData="route" /></Found>
  <NotFound><p class="notice">Page not found. <a href="/">Back to BoardEmpire</a></p></NotFound>
</Router>
```

`Services/Session.cs`:

```csharp
using Game.WebClient;
using Microsoft.JSInterop;

namespace Game.Web.Services;

/// <summary>Per-tab identity and the active GameSession, persisted to sessionStorage so a refresh resumes the seat.</summary>
public sealed class Session
{
    private readonly IJSRuntime _js;
    public ApiClient Api { get; }
    public AuthResult? Auth { get; private set; }
    public string PlayerName { get; set; } = "";
    public GameSession? Game { get; set; }

    public Session(ApiClient api, IJSRuntime js)
    {
        Api = api;
        _js = js;
    }

    public async Task<AuthResult> EnsureGuestAsync(string name)
    {
        if (Auth != null) return Auth;
        PlayerName = name;
        Auth = await Api.GuestAsync(name);
        Api.Token = Auth.Token;
        return Auth;
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        Auth = await Api.LoginAsync(username, password);
        Api.Token = Auth.Token;
        PlayerName = Auth.DisplayName;
        return Auth;
    }

    public async Task<AuthResult> RegisterAsync(string username, string password, string displayName)
    {
        Auth = await Api.RegisterAsync(username, password, displayName);
        Api.Token = Auth.Token;
        PlayerName = Auth.DisplayName;
        return Auth;
    }

    public async Task SaveResumeAsync(string code, string token)
    {
        try { await _js.InvokeVoidAsync("sessionStorage.setItem", "be.resume." + code, token); }
        catch (JSException) { }
    }

    public async Task<string> LoadResumeAsync(string code)
    {
        try { return await _js.InvokeAsync<string?>("sessionStorage.getItem", "be.resume." + code) ?? ""; }
        catch (JSException) { return ""; }
    }
}
```

`Pages/Home.razor`:

```razor
@page "/"
@inject Session Session
@inject NavigationManager Nav

<main class="home">
  <h1>BoardEmpire</h1>
  @if (error != null) { <p class="error" role="alert">@error</p> }

  <section class="card">
    <label>Your name <input @bind="name" maxlength="20" placeholder="Guest" /></label>
    <div class="row">
      <button disabled="@busy" @onclick="Quick">Quick match</button>
      <button disabled="@busy" @onclick="Create">Create room</button>
    </div>
    <div class="row">
      <input @bind="joinCode" maxlength="8" placeholder="Room code" />
      <button disabled="@busy" @onclick="Join">Join</button>
    </div>
  </section>

  <section class="card">
    <h2>Open rooms</h2>
    <button @onclick="Refresh">Refresh</button>
    @if (rooms.Count == 0) { <p>No open rooms. Create one!</p> }
    <ul>
      @foreach (var r in rooms)
      {
        <li><span>@r.Name — @r.Players/@r.MaxPlayers — @r.Preset</span>
            <button @onclick="() => GoTo(r.Code)">Join</button></li>
      }
    </ul>
  </section>
</main>

@code {
    private string name = "";
    private string joinCode = "";
    private string? error;
    private bool busy;
    private IReadOnlyList<RoomSummary> rooms = Array.Empty<RoomSummary>();

    protected override Task OnInitializedAsync() => Refresh();

    private async Task Refresh()
    {
        try { rooms = await Session.Api.RoomsAsync(); }
        catch (ApiException e) { error = e.Message; }
        catch (HttpRequestException) { error = "Can't reach the server."; }
    }

    private async Task Run(Func<Task> action)
    {
        busy = true;
        error = null;
        try { await action(); }
        catch (ApiException e) { error = e.Message; }
        catch (HttpRequestException) { error = "Can't reach the server."; }
        finally { busy = false; }
    }

    private Task Quick() => Run(async () =>
    {
        await Session.EnsureGuestAsync(Clean());
        string code = await Session.Api.QuickMatchAsync("quick");
        Nav.NavigateTo("/room/" + code);
    });

    private Task Create() => Run(async () =>
    {
        var auth = await Session.EnsureGuestAsync(Clean());
        string code = await Session.Api.CreateRoomAsync(auth.DisplayName + "'s game", "classic", "", 6, true);
        Nav.NavigateTo("/room/" + code);
    });

    private Task Join() => Run(async () =>
    {
        string code = joinCode.Trim().ToUpperInvariant();
        if (code.Length == 0) { error = "Enter a room code."; return; }
        if (!await Session.Api.RoomExistsAsync(code)) { error = "Room not found."; return; }
        await Session.EnsureGuestAsync(Clean());
        Nav.NavigateTo("/room/" + code);
    });

    private async Task GoTo(string code)
    {
        await Run(async () =>
        {
            await Session.EnsureGuestAsync(Clean());
            Nav.NavigateTo("/room/" + code);
        });
    }

    private string Clean() => string.IsNullOrWhiteSpace(name) ? "Guest" : name.Trim();
}
```

`wwwroot/css/app.css` (tokens, dark theme; extended in later tasks):

```css
:root { --bg:#0b0d12; --panel:#151922; --text:#e8ecf4; --muted:#8b93a7; --accent:#34d399; --bad:#f87171; --line:#263042; }
* { box-sizing: border-box; }
html, body { margin: 0; background: var(--bg); color: var(--text); font-family: system-ui, sans-serif; }
.boot, .notice { padding: 2rem; }
.home { max-width: 640px; margin: 0 auto; padding: 1.5rem 16px; }
.card { background: var(--panel); border: 1px solid var(--line); border-radius: 12px; padding: 1rem; margin: 1rem 0; }
.row { display: flex; gap: .5rem; margin-top: .5rem; }
input { background: #0e1219; color: var(--text); border: 1px solid var(--line); border-radius: 8px; padding: .5rem; }
button { background: var(--accent); color: #04130d; border: 0; border-radius: 8px; padding: .55rem .9rem; font-weight: 600; cursor: pointer; }
button:disabled { opacity: .5; cursor: default; }
.error { color: var(--bad); }
ul { list-style: none; padding: 0; }
li { display: flex; justify-content: space-between; align-items: center; padding: .4rem 0; border-top: 1px solid var(--line); }
```

- [ ] **Step 2: Build and publish**

Run: `dotnet publish src/Game.Web -c Release -o /tmp/claude-1000/-home-opc/bb95740b-58ef-48d2-9ba7-b430c6e59156/scratchpad/webout`
Expected: succeeds; `wwwroot/_framework/blazor.webassembly.js` exists in the output.

- [ ] **Step 3: Commit**

```bash
git add BoardEmpire.sln src/Game.Web
git commit -m "feat(web): add Blazor WASM shell and home page"
```

---

### Task 5: Lobby page

**Files:**
- Create: `src/Game.Web/Pages/Lobby.razor`, `src/Game.Web/Services/GameLoop.cs`
- Modify: `src/Game.Web/wwwroot/css/app.css`

**Interfaces:**
- Consumes: `Session`, `GameSession`, `BrowserSocket` (Tasks 3, 4); `LobbyInfo`, `SeatInfo`, `BotLevel` (Game.Protocol).
- Produces: `GameLoop` helper: `static async Task<GameSession> OpenAsync(Session session, NavigationManager nav, string code)` which creates the session over `BrowserSocket.RoomUri(...)`, loads the resume token, connects, stores `session.Game`, and starts a `PeriodicTimer(50ms)` calling `Tick`.

- [ ] **Step 1: Implement `GameLoop`**

```csharp
using System.Diagnostics;
using Game.WebClient;
using Microsoft.AspNetCore.Components;

namespace Game.Web.Services;

public static class GameLoop
{
    public static async Task<GameSession> OpenAsync(Session session, NavigationManager nav, string code)
    {
        if (session.Game != null && session.Game.Client.Lobby?.RoomCode == code) return session.Game;
        if (session.Game != null) await session.Game.DisconnectAsync();
        var uri = BrowserSocket.RoomUri(new Uri(nav.BaseUri), code, session.Auth?.Token ?? "");
        string resume = await session.LoadResumeAsync(code);
        var game = new GameSession(BrowserSocket.Create(uri), session.PlayerName, session.Auth?.Token ?? "", resume);
        session.Game = game;
        await game.ConnectAsync();
        _ = RunAsync(game, session, code);
        return game;
    }

    private static async Task RunAsync(GameSession game, Session session, string code)
    {
        var clock = Stopwatch.StartNew();
        string saved = "";
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (await timer.WaitForNextTickAsync())
        {
            if (!ReferenceEquals(session.Game, game)) return;
            game.Tick(clock.Elapsed.TotalSeconds);
            string token = game.Client.ResumeToken;
            if (token.Length > 0 && token != saved)
            {
                saved = token;
                await session.SaveResumeAsync(code, token);
            }
        }
    }
}
```

- [ ] **Step 2: Implement `Lobby.razor`**

```razor
@page "/room/{Code}"
@implements IDisposable
@inject Session Session
@inject NavigationManager Nav

<main class="lobby">
  @if (game == null) { <p>Connecting…</p> }
  else if (game.UpdateRequired != null)
  {
    <p class="error" role="alert">This version is out of date. <a href="javascript:location.reload()">Refresh</a> to update.</p>
  }
  else if (game.Client.Lobby is { } lobby)
  {
    <h1>@lobby.RoomName <small>#@lobby.RoomCode</small></h1>
    <section class="card">
      <h2>Players</h2>
      <ul>
        @foreach (var seat in lobby.Seats)
        {
          <li>
            <span>@seat.Name @(seat.IsBot ? "(bot)" : "") @(seat.Ready ? "✓" : "")</span>
            @if (game.Client.IsHost && seat.PeerId != game.Client.PeerId)
            {
              <button @onclick="() => game.Client.Kick(seat.Seat)">Remove</button>
            }
          </li>
        }
      </ul>
      <div class="row">
        <button @onclick="() => game.Client.SetReady(true)">Ready</button>
        @if (game.Client.IsHost)
        {
          <button @onclick="() => game.Client.AddBot(BotLevel.Medium)">Add bot</button>
          <button @onclick="() => game.Client.StartMatch()">Start match</button>
        }
      </div>
    </section>
  }
  else { <p>Joining room…</p> }
  @if (error != null) { <p class="error" role="alert">@error</p> }
</main>

@code {
    [Parameter] public string Code { get; set; } = "";
    private GameSession? game;
    private string? error;

    protected override async Task OnParametersSetAsync()
    {
        if (Session.Auth == null) { Nav.NavigateTo("/"); return; }
        try
        {
            game = await GameLoop.OpenAsync(Session, Nav, Code);
            game.Changed += OnChanged;
        }
        catch (Exception e) when (e is HttpRequestException or System.Net.WebSockets.WebSocketException or InvalidOperationException)
        {
            error = "Could not join this room.";
        }
    }

    private void OnChanged()
    {
        if (game?.Client.Status == ClientStatus.InMatch && game.Display != null)
        {
            Nav.NavigateTo("/play/" + Code);
            return;
        }
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        if (game != null) game.Changed -= OnChanged;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Game.Web -c Release`
Expected: Build succeeded, no warnings about missing members. Fix any `Status`/`ClientStatus` namespace issues by adding `@using Game.Net` to `_Imports.razor`.

- [ ] **Step 4: Commit**

```bash
git add src/Game.Web
git commit -m "feat(web): add lobby page and connection loop"
```

---

### Task 6: Play page with 2D board and core actions

**Files:**
- Create: `src/Game.Web/Pages/Play.razor`, `src/Game.Web/Components/Board2D.razor`, `src/Game.Web/Components/Hud.razor`
- Modify: `src/Game.Web/wwwroot/css/app.css`

**Interfaces:**
- Consumes: `TileGrid.Cell`, `GameSession` (Display, Board, Client.Controls, Client.SendCommand, EventPlayed), command records `RollDiceCommand`, `EndTurnCommand`, `BuyPropertyCommand`, `DeclinePropertyCommand`, `PlaceBidCommand`, `PassAuctionCommand`, `PayJailFineCommand`, `UseJailCardCommand` (all `(int PlayerId, ...)`).
- Produces: `Board2D` component parameters `[Parameter] GameState State`, `[Parameter] BoardDefinition Board`, `[Parameter] EventCallback<int> OnTileClick`; `Hud` parameters `GameSession Game`.

- [ ] **Step 1: Board2D**

`Components/Board2D.razor`:

```razor
@{
    int side = Board.Tiles.Count / 4;
    int dim = side + 1;
}
<div class="board2d" style="--dim:@dim">
  @for (int i = 0; i < Board.Tiles.Count; i++)
  {
    int tile = i;
    var def = Board.Tiles[tile];
    var (row, col) = TileGrid.Cell(tile, side);
    var prop = State.Property(tile);
    var here = State.Players.Where(p => !p.Bankrupt && p.Position == tile).ToList();
    <button class="tile @(prop?.Mortgaged == true ? "mortgaged" : "")"
            style="grid-row:@(row + 1);grid-column:@(col + 1)"
            @onclick="() => OnTileClick.InvokeAsync(tile)" aria-label="@def.Name">
      <span class="name">@def.Name</span>
      @if (prop != null && prop.Owner >= 0) { <span class="owner o@(prop.Owner % 8)">P@(prop.Owner + 1)</span> }
      @if (prop != null && prop.Level > 0) { <span class="level">@(prop.Level == 5 ? "★" : new string('▪', prop.Level))</span> }
      <span class="tokens">@foreach (var p in here) { <i class="tok o@(p.Id % 8)">@(p.Id + 1)</i> }</span>
    </button>
  }
  <div class="center" style="grid-row:2 / span @(dim - 2);grid-column:2 / span @(dim - 2)">
    <div class="dice">@State.Die1 · @State.Die2</div>
    <div class="round">Round @State.Round</div>
  </div>
</div>

@code {
    [Parameter, EditorRequired] public GameState State { get; set; } = null!;
    [Parameter, EditorRequired] public BoardDefinition Board { get; set; } = null!;
    [Parameter] public EventCallback<int> OnTileClick { get; set; }
}
```

- [ ] **Step 2: Hud with the core actions**

`Components/Hud.razor`:

```razor
@{
    var s = Game.Display!;
    int me = Game.Client.Seats.Length > 0 ? Game.Client.Seats[0] : -1;
    bool myTurn = me >= 0 && s.CurrentPlayer == me;
    string cur = s.Players[s.CurrentPlayer].Name;
}
<aside class="hud">
  <h2>@(myTurn ? "Your turn" : cur + "'s turn")</h2>
  <ul class="players">
    @foreach (var p in s.Players)
    {
      <li class="@(p.Id == s.CurrentPlayer ? "cur" : "") @(p.Bankrupt ? "out" : "")">
        <span>@p.Name @(p.IsBot ? "🤖" : "")</span><b>@Board.Currency@p.Money</b>
      </li>
    }
  </ul>
  @if (myTurn)
  {
    <div class="actions">
      @switch (s.Phase)
      {
        case TurnPhase.PreRoll:
          <button @onclick="() => Send(new RollDiceCommand(me))">Roll dice</button>
          @if (s.Players[me].InJail)
          {
            <button @onclick="() => Send(new PayJailFineCommand(me))">Pay fine</button>
            @if (s.Players[me].JailCards > 0) { <button @onclick="() => Send(new UseJailCardCommand(me))">Use card</button> }
          }
          break;
        case TurnPhase.BuyDecision:
          <button @onclick="() => Send(new BuyPropertyCommand(me))">Buy</button>
          <button class="ghost" @onclick="() => Send(new DeclinePropertyCommand(me))">Pass</button>
          break;
        case TurnPhase.PostRoll:
          <button @onclick="() => Send(new EndTurnCommand(me))">End turn</button>
          break;
      }
    </div>
  }
  @if (s.Phase == TurnPhase.Auction && s.Auction != null && me >= 0)
  {
    <div class="actions">
      <input type="number" @bind="bid" min="1" />
      <button @onclick="() => Send(new PlaceBidCommand(me, bid))">Bid</button>
      <button class="ghost" @onclick="() => Send(new PassAuctionCommand(me))">Pass</button>
    </div>
  }
  @if (message != null) { <p class="error">@message</p> }
  @if (s.IsOver)
  {
    <p class="winner">Game over</p>
    <button @onclick="() => Game.Client.Send(new RequestRematch())">Rematch</button>
  }
  <div class="actions chat">
    @for (int preset = 0; preset < 4; preset++)
    {
      int id = preset;
      <button class="ghost" @onclick="() => Game.Client.Chat(id)">@ChatLabels[id]</button>
    }
  </div>
</aside>

@code {
    [Parameter, EditorRequired] public GameSession Game { get; set; } = null!;
    private BoardDefinition Board => Game.Board!;
    private int bid = 10;
    private string? message;
    // Preset ids 0-3; confirm the real preset list in client/src (grep "SendChat\|ChatPreset") and match labels to it.
    private static readonly string[] ChatLabels = { "👍", "👏", "😅", "GG" };

    protected override void OnInitialized() => Game.Client.CommandFailed += (_, reason) =>
    {
        message = reason;
        InvokeAsync(StateHasChanged);
    };

    private void Send(GameCommand command)
    {
        message = null;
        Game.Client.SendCommand(command);
    }
}
```

- [ ] **Step 3: Play page**

`Pages/Play.razor`:

```razor
@page "/play/{Code}"
@implements IDisposable
@inject Session Session
@inject NavigationManager Nav

<main class="play">
  @if (game?.Display != null && game.Board != null)
  {
    <div class="stage">
      <Board2D State="game.Display" Board="game.Board" OnTileClick="OnTile" />
    </div>
    <Hud Game="game" />
  }
  else if (game?.UpdateRequired != null) { <p class="error">Out of date, please refresh.</p> }
  else { <p>Loading match…</p> }
</main>

@code {
    [Parameter] public string Code { get; set; } = "";
    private GameSession? game;

    protected override async Task OnParametersSetAsync()
    {
        if (Session.Auth == null) { Nav.NavigateTo("/"); return; }
        game = await GameLoop.OpenAsync(Session, Nav, Code);
        game.Changed += OnChanged;
        game.Client.ReturnedToLobby += BackToLobby;
    }

    private void BackToLobby() => Nav.NavigateTo("/room/" + Code);
    private void OnChanged() => InvokeAsync(StateHasChanged);
    private Task OnTile(int tile) => Task.CompletedTask;

    public void Dispose()
    {
        if (game == null) return;
        game.Changed -= OnChanged;
        game.Client.ReturnedToLobby -= BackToLobby;
    }
}
```

Append to `app.css`:

```css
.play { display: grid; grid-template-columns: minmax(0, 1fr) 280px; gap: 12px; padding: 12px; }
@media (max-width: 800px) { .play { grid-template-columns: 1fr; } }
.board2d { display: grid; grid-template-columns: repeat(var(--dim), 1fr); grid-template-rows: repeat(var(--dim), 1fr); aspect-ratio: 1; gap: 2px; max-width: 100%; }
.tile { position: relative; background: var(--panel); border: 1px solid var(--line); color: var(--text); font-size: 10px; padding: 2px; border-radius: 4px; overflow: hidden; text-align: left; }
.tile.mortgaged { opacity: .45; }
.tile .name { display: block; line-height: 1.1; }
.tile .owner, .tile .tok { font-size: 9px; border-radius: 6px; padding: 0 3px; margin-right: 2px; }
.tokens { position: absolute; bottom: 2px; left: 2px; }
.tok { font-style: normal; background: var(--accent); color: #04130d; }
.center { display: flex; flex-direction: column; align-items: center; justify-content: center; background: #0e1219; border-radius: 8px; }
.hud { background: var(--panel); border: 1px solid var(--line); border-radius: 12px; padding: 12px; }
.players li.cur { color: var(--accent); }
.players li.out { text-decoration: line-through; color: var(--muted); }
.actions { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 8px; }
button.ghost { background: transparent; color: var(--text); border: 1px solid var(--line); }
.o0{background:#f87171}.o1{background:#60a5fa}.o2{background:#fbbf24}.o3{background:#a78bfa}.o4{background:#f472b6}.o5{background:#2dd4bf}.o6{background:#fb923c}.o7{background:#a3e635}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Game.Web -c Release`
Expected: Build succeeded. If a command constructor has a different signature, read `src/Game.Core/Commands/` and fix the call sites.

- [ ] **Step 5: Commit**

```bash
git add src/Game.Web
git commit -m "feat(web): add play page with 2D board and core turn actions"
```

---

### Task 7: Management dialogs (build, mortgage, trade, bankruptcy)

**Files:**
- Create: `src/Game.Web/Components/PropertyPanel.razor`, `src/Game.Web/Components/TradeDialog.razor`
- Modify: `src/Game.Web/Pages/Play.razor`, `src/Game.Web/Components/Hud.razor`

**Interfaces:**
- Consumes: `GameState.Property(int)`, `BuildHouseCommand(int PlayerId, int Tile, DevelopmentType DevType)`, `SellHouseCommand(PlayerId, Tile)`, `MortgageCommand(PlayerId, Tile)`, `UnmortgageCommand(PlayerId, Tile)`, `CreateTradeCommand(PlayerId, To, TradeSide Give, TradeSide Receive)`, `AcceptTradeCommand(PlayerId, TradeId)`, `RejectTradeCommand(PlayerId, TradeId)`, `DeclareBankruptcyCommand(PlayerId)`.
- Produces: `PropertyPanel` parameters `GameSession Game`, `int Tile`; `TradeDialog` parameters `GameSession Game`, `int Partner`.

- [ ] **Step 1: Read `TradeSide` and trade rules**

Run: `grep -n "class TradeSide" -A15 src/Game.Core/State/GameState.cs`
Expected: the fields (money, tiles, cards). Use exactly those in `TradeDialog`.

- [ ] **Step 2: PropertyPanel**

```razor
@{
    var s = Game.Display!;
    var def = Game.Board!.Tiles[Tile];
    var prop = s.Property(Tile);
    int me = Game.Client.Seats.Length > 0 ? Game.Client.Seats[0] : -1;
    bool mine = prop != null && prop.Owner == me;
}
<div class="panel card">
  <h3>@def.Name</h3>
  @if (prop == null) { <p>Not for sale.</p> }
  else
  {
    <p>@(prop.Owner < 0 ? "Owned by the bank" : "Owner: " + s.Players[prop.Owner].Name) · Level @prop.Level @(prop.Mortgaged ? "· mortgaged" : "")</p>
    @if (mine)
    {
      <div class="actions">
        <button @onclick="() => Game.Client.SendCommand(new BuildHouseCommand(me, Tile, DevelopmentType.Residential))">Build</button>
        <button class="ghost" @onclick="() => Game.Client.SendCommand(new SellHouseCommand(me, Tile))">Sell</button>
        @if (!prop.Mortgaged) { <button class="ghost" @onclick="() => Game.Client.SendCommand(new MortgageCommand(me, Tile))">Mortgage</button> }
        else { <button @onclick="() => Game.Client.SendCommand(new UnmortgageCommand(me, Tile))">Unmortgage</button> }
      </div>
    }
  }
</div>

@code {
    [Parameter, EditorRequired] public GameSession Game { get; set; } = null!;
    [Parameter] public int Tile { get; set; }
}
```

- [ ] **Step 3: TradeDialog (money-only first, incoming trades)**

```razor
@{
    var s = Game.Display!;
    int me = Game.Client.Seats.Length > 0 ? Game.Client.Seats[0] : -1;
}
<div class="panel card">
  <h3>Trades</h3>
  @foreach (var t in s.Trades.Where(t => t.To == me))
  {
    <div class="row">
      <span>@s.Players[t.From].Name offers a trade</span>
      <button @onclick="() => Game.Client.SendCommand(new AcceptTradeCommand(me, t.Id))">Accept</button>
      <button class="ghost" @onclick="() => Game.Client.SendCommand(new RejectTradeCommand(me, t.Id))">Reject</button>
    </div>
  }
  <div class="row">
    <select @bind="partner">
      @foreach (var p in s.Players.Where(p => p.Id != me && !p.Bankrupt)) { <option value="@p.Id">@p.Name</option> }
    </select>
    <input type="number" @bind="give" min="0" placeholder="You give" />
    <input type="number" @bind="receive" min="0" placeholder="You receive" />
    <button @onclick="Offer">Offer</button>
  </div>
</div>

@code {
    [Parameter, EditorRequired] public GameSession Game { get; set; } = null!;
    private int partner;
    private int give;
    private int receive;

    private void Offer()
    {
        int me = Game.Client.Seats[0];
        // Use the real TradeSide shape found in Step 1.
        var giveSide = new TradeSide { Money = give };
        var receiveSide = new TradeSide { Money = receive };
        Game.Client.SendCommand(new CreateTradeCommand(me, partner, giveSide, receiveSide));
    }
}
```

- [ ] **Step 4: Wire into Play and Hud**

In `Play.razor` keep `private int? selected;`, set it in `OnTile`, render `<PropertyPanel Game="game" Tile="selected.Value" />` under the HUD when set, and render `<TradeDialog Game="game" />` below it. In `Hud.razor`, add a Bankrupt button for the player's own seat: `<button class="ghost" @onclick="() => Send(new DeclareBankruptcyCommand(me))">Declare bankruptcy</button>` shown when `s.Phase == TurnPhase.DebtResolution` and it is the player's turn.

- [ ] **Step 5: Build**

Run: `dotnet build src/Game.Web -c Release`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/Game.Web
git commit -m "feat(web): add property, trade and bankruptcy controls"
```

---

### Task 8: 3D view with three.js and fallback

**Files:**
- Create: `src/Game.Web/wwwroot/js/board3d.js`, `src/Game.Web/wwwroot/js/vendor/three.module.min.js`, `src/Game.Web/Components/Board3D.razor`
- Modify: `src/Game.Web/Pages/Play.razor`, `src/Game.Web/wwwroot/css/app.css`

**Interfaces:**
- Consumes: `DisplayPayload.State/Event/Board` (Task 3), `GameSession.EventPlayed`, `GameSession.Display`.
- Produces: JS module `board3d.js` exports `init(canvas, boardJson)`, `applyState(stateJson)`, `playEvent(eventJson)`, `dispose()`; `init` returns `true` on success, `false` when WebGL is unavailable. `Board3D` parameters `[Parameter] GameSession Game`, `[Parameter] EventCallback OnUnavailable`.

- [ ] **Step 1: Vendor three.js**

```bash
cd /tmp/claude-1000/-home-opc/bb95740b-58ef-48d2-9ba7-b430c6e59156/scratchpad && rm -rf three && mkdir three && cd three && npm pack three@0.160.0 >/dev/null && tar xzf three-0.160.0.tgz
mkdir -p ~/projects/monopoly/src/Game.Web/wwwroot/js/vendor
cp package/build/three.module.min.js ~/projects/monopoly/src/Game.Web/wwwroot/js/vendor/three.module.min.js
cp package/LICENSE ~/projects/monopoly/src/Game.Web/wwwroot/js/vendor/three.LICENSE
```

Expected: file exists, size roughly 700 KB.

- [ ] **Step 2: Write `board3d.js`**

```javascript
import * as THREE from './vendor/three.module.min.js';

let renderer, scene, camera, raf, canvas, board;
const tokens = new Map();
const tiles = [];
let side = 10;

function cell(tile) {
  const i = ((tile % (side * 4)) + side * 4) % (side * 4);
  if (i < side) return [side, side - i];
  if (i < side * 2) return [side - (i - side), 0];
  if (i < side * 3) return [0, i - side * 2];
  return [i - side * 3, side];
}

function pos(tile) {
  const [r, c] = cell(tile);
  return new THREE.Vector3((c - side / 2) * 1.2, 0, (r - side / 2) * 1.2);
}

export function init(canvasEl, boardJson) {
  try {
    canvas = canvasEl;
    board = JSON.parse(boardJson);
    side = board.Tiles.length / 4;
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    scene = new THREE.Scene();
    scene.background = new THREE.Color(0x0b0d12);
    camera = new THREE.PerspectiveCamera(45, 1, 0.1, 100);
    camera.position.set(0, side * 1.5, side * 1.3);
    camera.lookAt(0, 0, 0);
    scene.add(new THREE.AmbientLight(0xffffff, 0.7));
    const sun = new THREE.DirectionalLight(0xffffff, 0.8);
    sun.position.set(5, 10, 6);
    scene.add(sun);

    const colors = {};
    for (const d of board.Districts) colors[d.Id] = d.Color;
    board.Tiles.forEach((t, i) => {
      const color = new THREE.Color(colors[t.District] || '#2a3344');
      const mesh = new THREE.Mesh(
        new THREE.BoxGeometry(1.1, 0.12, 1.1),
        new THREE.MeshStandardMaterial({ color }));
      mesh.position.copy(pos(i));
      scene.add(mesh);
      tiles.push({ mesh, building: null });
    });
    resize();
    window.addEventListener('resize', resize);
    loop();
    return true;
  } catch (e) {
    console.warn('board3d init failed', e);
    dispose();
    return false;
  }
}

function resize() {
  if (!renderer) return;
  const w = canvas.clientWidth || 600;
  const h = canvas.clientHeight || 600;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}

function loop() {
  raf = requestAnimationFrame(loop);
  for (const t of tokens.values()) t.mesh.position.lerp(t.target, 0.15);
  renderer.render(scene, camera);
}

function ensureToken(p) {
  let t = tokens.get(p.Id);
  if (!t) {
    const mesh = new THREE.Mesh(
      new THREE.CylinderGeometry(0.22, 0.22, 0.5, 16),
      new THREE.MeshStandardMaterial({ color: new THREE.Color().setHSL((p.Id * 0.17) % 1, 0.7, 0.55) }));
    scene.add(mesh);
    t = { mesh, target: pos(p.Position).setY(0.35) };
    mesh.position.copy(t.target);
    tokens.set(p.Id, t);
  }
  return t;
}

export function applyState(stateJson) {
  if (!scene) return;
  const s = JSON.parse(stateJson);
  for (const p of s.Players) {
    const t = ensureToken(p);
    t.mesh.visible = !p.Bankrupt;
    t.target = pos(p.Position).setY(0.35);
  }
  s.Properties.forEach((prop, i) => {
    if (!prop || !tiles[i]) return;
    const tile = tiles[i];
    const h = 0.12 + prop.Level * 0.25;
    if (tile.building) { scene.remove(tile.building); tile.building = null; }
    if (prop.Level > 0 || prop.Owner >= 0) {
      const b = new THREE.Mesh(
        new THREE.BoxGeometry(0.6, Math.max(h, 0.2), 0.6),
        new THREE.MeshStandardMaterial({
          color: prop.Owner >= 0 ? new THREE.Color().setHSL((prop.Owner * 0.17) % 1, 0.6, 0.5) : 0x555555,
          transparent: prop.Mortgaged, opacity: prop.Mortgaged ? 0.4 : 1 }));
      b.position.copy(pos(i)).setY(h / 2 + 0.06);
      scene.add(b);
      tile.building = b;
    }
  });
}

export function playEvent(eventJson) {
  if (!scene) return;
  const e = JSON.parse(eventJson);
  if (e['$t'] === 'PlayerMoved') {
    const t = tokens.get(e.Player);
    if (t) t.target = pos(e.To).setY(0.35);
  }
}

export function dispose() {
  if (raf) cancelAnimationFrame(raf);
  window.removeEventListener('resize', resize);
  tokens.clear();
  tiles.length = 0;
  if (renderer) { renderer.dispose(); renderer = null; }
  scene = null;
}
```

- [ ] **Step 3: Board3D component**

`Components/Board3D.razor`:

```razor
@implements IAsyncDisposable
@inject IJSRuntime Js

<canvas @ref="canvas" class="board3d"></canvas>

@code {
    [Parameter, EditorRequired] public GameSession Game { get; set; } = null!;
    [Parameter] public EventCallback OnUnavailable { get; set; }
    private ElementReference canvas;
    private IJSObjectReference? module;
    private bool ready;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            module = await Js.InvokeAsync<IJSObjectReference>("import", "./js/board3d.js");
            ready = await module.InvokeAsync<bool>("init", canvas, DisplayPayload.Board(Game.Board!));
        }
        catch (JSException) { ready = false; }
        if (!ready)
        {
            await OnUnavailable.InvokeAsync();
            return;
        }
        await module!.InvokeVoidAsync("applyState", DisplayPayload.State(Game.Display!));
        Game.EventPlayed += OnEvent;
    }

    private async void OnEvent(GameEvent e, GameState after)
    {
        if (!ready || module == null) return;
        await module.InvokeVoidAsync("playEvent", DisplayPayload.Event(e));
        await module.InvokeVoidAsync("applyState", DisplayPayload.State(after));
    }

    public async ValueTask DisposeAsync()
    {
        Game.EventPlayed -= OnEvent;
        if (module != null)
        {
            try { await module.InvokeVoidAsync("dispose"); await module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}
```

Note: `OnEvent` re-applies the whole state after each event, which keeps the 3D view correct even if an event type is not specially animated.

- [ ] **Step 4: Toggle and fallback in Play.razor**

Add `private bool use3d;` and a button in the stage header: `<button class="ghost" @onclick="() => use3d = !use3d">@(use3d ? "2D" : "3D")</button>`. Render `@if (use3d) { <Board3D Game="game" OnUnavailable="Fallback" /> } else { <Board2D .../> }`. Add `private Task Fallback() { use3d = false; return InvokeAsync(StateHasChanged); }`. Append CSS:

```css
.board3d { width: 100%; aspect-ratio: 1; border-radius: 12px; background: #0b0d12; }
```

- [ ] **Step 5: Build and publish**

Run: `dotnet publish src/Game.Web -c Release -o /tmp/claude-1000/-home-opc/bb95740b-58ef-48d2-9ba7-b430c6e59156/scratchpad/webout && ls /tmp/claude-1000/-home-opc/bb95740b-58ef-48d2-9ba7-b430c6e59156/scratchpad/webout/wwwroot/js/vendor`
Expected: `three.module.min.js` present in published output.

- [ ] **Step 6: Commit**

```bash
git add src/Game.Web
git commit -m "feat(web): add three.js 3D board view with 2D fallback"
```

---

### Task 9: Deploy tooling and Caddy

**Files:**
- Create: `tools/deploy-web.sh`
- Modify: `deploy/Caddyfile.snippet`, `/home/opc/manager/Caddyfile` (host, not in this repo)

**Interfaces:**
- Produces: static site root `/home/opc/web/boardempire` served by Caddy.

- [ ] **Step 1: Check how Caddy mounts host directories**

Run: `grep -n -E "volumes|/srv|/usr/share|certs" /home/opc/manager/compose.yaml | head`
Expected: the mounts of `caddy-proxy`. If `/home/opc/web` is not mounted, add a read-only bind mount `/home/opc/web:/srv/web:ro` to the `caddy-proxy` service in `compose.yaml` and recreate only that service with `podman compose up -d caddy-proxy`.

- [ ] **Step 2: Write `tools/deploy-web.sh`**

```bash
#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-/home/opc/web/boardempire}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
dotnet publish "$ROOT/src/Game.Web" -c Release -o "$STAGE"
mkdir -p "$OUT"
rsync -a --delete "$STAGE/wwwroot/" "$OUT/"
echo "Deployed to $OUT"
```

```bash
chmod +x tools/deploy-web.sh
```

- [ ] **Step 3: Update the Caddy site block**

Replace the `monopoly.algosculptor.com` block in `/home/opc/manager/Caddyfile` (and mirror it in `deploy/Caddyfile.snippet`) with:

```caddyfile
monopoly.algosculptor.com {
	tls /certs/origin.pem /certs/origin.key

	@api path /api/* /ws/* /healthz
	handle @api {
		reverse_proxy 127.0.0.1:8091
	}

	handle {
		root * /srv/web/boardempire
		try_files {path} /index.html
		encode zstd gzip
		file_server
	}
}
```

Validate and restart: `podman exec caddy-proxy caddy validate --config /etc/caddy/Caddyfile && podman restart caddy-proxy`
Expected: `Valid configuration`, container up.

- [ ] **Step 4: Deploy and check**

Run: `tools/deploy-web.sh && curl -s -o /dev/null -w '%{http_code}\n' https://monopoly.algosculptor.com/ && curl -s -o /dev/null -w '%{http_code}\n' https://monopoly.algosculptor.com/api/version`
Expected: `200` and `200`.

- [ ] **Step 5: Commit**

```bash
git add tools/deploy-web.sh deploy/Caddyfile.snippet
git commit -m "feat(web): add deploy script and Caddy site config"
```

---

### Task 10: End-to-end smoke test, version bump, release

**Files:**
- Create: `tools/web-smoke/package.json`, `tools/web-smoke/smoke.mjs`
- Modify: version files via `tools/set-version.sh`

**Interfaces:**
- Consumes: the deployed site (Task 9).

- [ ] **Step 1: Write the smoke test**

`tools/web-smoke/package.json`:

```json
{ "name": "web-smoke", "private": true, "type": "module", "dependencies": { "playwright": "1.48.0" } }
```

`tools/web-smoke/smoke.mjs`:

```javascript
import { chromium } from 'playwright';

const base = process.argv[2] || 'https://monopoly.algosculptor.com/';
const browser = await chromium.launch({ args: ['--use-gl=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage();
const errors = [];
page.on('pageerror', e => errors.push(String(e)));
page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });

await page.goto(base, { waitUntil: 'networkidle' });
await page.fill('input[placeholder="Guest"]', 'SmokeBot');
await page.click('text=Create room');
await page.waitForURL(/\/room\//, { timeout: 30000 });
await page.click('text=Add bot');
await page.click('text=Ready');
await page.click('text=Start match');
await page.waitForURL(/\/play\//, { timeout: 30000 });
await page.waitForSelector('.board2d', { timeout: 30000 });
await page.click('text=Roll dice');
await page.waitForTimeout(3000);
await page.click('button:has-text("3D")');
await page.waitForSelector('canvas.board3d', { timeout: 10000 });
await page.waitForTimeout(1500);

if (errors.length) {
  console.error('Browser errors:\n' + errors.join('\n'));
  process.exit(1);
}
console.log('smoke ok');
await browser.close();
```

- [ ] **Step 2: Run it**

```bash
cd tools/web-smoke && npm install --silent && npx playwright install chromium >/dev/null 2>&1; node smoke.mjs
```

Expected: `smoke ok`. On a failure, read the printed browser errors (socket, JS interop, WASM) and fix the underlying code in the earlier task that owns it; do not weaken the test.

- [ ] **Step 3: Full test run**

Run: `dotnet test BoardEmpire.sln -v q`
Expected: all existing suites plus Web.Tests pass.

- [ ] **Step 4: Version bump and tag**

Ask the user for the version number first (current is in `tools/set-version.sh`'s files; latest committed bump was 0.1.3). Then:

```bash
tools/set-version.sh <X.Y.Z>
git add -A -- ':!graphify-out'
git commit -m "chore(release): bump version to <X.Y.Z>"
git tag v<X.Y.Z>
```

Do not push without the user's go-ahead; push the tag only (`git push origin v<X.Y.Z>`), never `--tags`, and monitor CI until it passes before pushing `main`.

- [ ] **Step 5: Notify**

Send the `agent-tasks` ntfy notification described in the user's CLAUDE.md with a bulleted summary and the site URL.
