# BoardEmpire

An original multiplayer property-trading and economy board game: one deterministic rules
engine, a 2D board and a 3D "living city" over the same event stream, solo / pass-and-play /
LAN / Nearby / online play, bots, replays and composable rule modules.

Everything is C#. The client is **Godot 4.7 (.NET)** rather than Unity so that the whole
project builds on Linux ARM64.

```
src/Game.Core       rules engine, bots, replays (no engine or network dependencies)
src/Game.Protocol   versioned wire messages and packet codec
src/Game.Net        room authority, replica client, transports (loopback, LAN, WebSocket)
src/Game.Server     ASP.NET Core match server (accounts, rooms, matchmaking, persistence)
client/             Godot project: UI, 2D view, 3D view, audio, session management
android/            Kotlin plugin for Google Nearby Connections
tools/Simulator     balance simulator, bot tournament, replay recorder/viewer
tests/              Core (126), Net (30), Server (14) tests
deploy/, compose.yaml   container image and the server + Postgres + Redis stack
boards/             the built-in boards as JSON (the format custom boards use)
```

## How it fits together

```
input -> Command -> authority runs Game.Core -> Events -> replicas apply the same events
                                                       -> BoardPresenter -> 2D or 3D view
```

* Clients send intent ("roll"), never results. The authority (a phone hosting a local game,
  or the server) validates and answers with events.
* Events carry everything needed to mutate state, so the authority and every client run the
  same reducer. A SHA-256 of the public state is compared after each batch; a mismatch or a
  gap triggers an automatic resync.
* Private data (the dice stream, sealed bids, other players' secret objectives) is redacted
  per viewer before it leaves the authority and is excluded from the hash.
* The 2D/3D choice is a local setting. Views only animate; they cannot change the match.

## Build, test, run

```bash
# .NET 8 SDK (the Android export additionally needs .NET 9)
dotnet test BoardEmpire.sln
dotnet run --project tools/Simulator -c Release -- --games 10000 --players 4
dotnet run --project tools/Simulator -c Release -- tournament --games 400
dotnet run --project tools/Simulator -c Release -- record --out match.json --preset tycoon
dotnet run --project tools/Simulator -c Release -- replay match.json --story

# Tests against real PostgreSQL / Redis run when these are set:
#   BOARDEMPIRE_TEST_POSTGRES="Host=127.0.0.1;Port=55432;Username=postgres;Password=...;Database=boardempire"
#   BOARDEMPIRE_TEST_REDIS="127.0.0.1:56379"
```

Client (Godot 4.7.2 .NET editor binary as `godot`):

```bash
cd client
godot --headless --import            # first time
godot                                # run
godot -- --demo classic --view 3d    # watch four bots play
```

Developer flags after `--` (see `client/src/DevAutomation.cs`): `--screen`, `--demo <preset>`,
`--human --autoplay`, `--view 2d|3d`, `--speed`, `--shot file.png --after N`,
`--host-lan`, `--join-lan host:port`, `--online-quick <url>`. On a machine without a display:
`xvfb-run -a godot --rendering-driver opengl3 --audio-driver Dummy -- --demo classic --shot out.png`.

Android:

```bash
cd client
godot --headless --export-debug "Android" build/BoardEmpire-debug.apk      # works on ARM64 hosts
```

The `Android (Gradle)` preset additionally bundles the Nearby plugin; it needs an x86-64
machine for the Android build tools and is what `.github/workflows/android.yml` runs.

Server:

```bash
cp deploy/.env.example .env      # fill in the two secrets
podman compose up -d             # server on 127.0.0.1:8091, Postgres and Redis internal
curl http://127.0.0.1:8091/healthz
```

Put a reverse proxy in front (`deploy/Caddyfile.snippet`) and point the client's server URL
at it. Without `BoardEmpire__Postgres` / `BoardEmpire__Redis` the server runs in memory.

## Game modes

Presets are configurations of `GameRules`, never code forks: Classic, Classic+ (sealed
auctions, secret objectives), Quick, Blitz, Market Mayhem (economy cycle, city events, public
projects), Tycoon (property shares, contracts, development types, abilities), Team Empire, and
custom presets built in the rules editor.

## Known limits

See `HANDOFF.md` for what has and has not been verified, and what is intentionally left out.

Fonts: Space Mono, Outfit and Noto Sans Devanagari (SIL Open Font License, texts in
`client/assets/fonts`).
