# BoardEmpire Web Client — Design

Date: 2026-10-14

## Goal
Players open `https://monopoly.algosculptor.com`, play as guest or with an account, create or
join rooms and play full matches in the browser against bots and humans on the Godot and
Android clients. 2D and 3D board views are both available.

## Non-goals
LAN / Nearby / pass-and-play modes, replay editor, changes to `Game.Core`, `Game.Net` or
`Game.Server` behavior or protocol.

## Architecture
- New project `src/Game.Web`: Blazor WebAssembly (.NET 8, no AOT). References `Game.Core`,
  `Game.Protocol` and `Game.Net` unchanged so rules, state hash and resync equal the Godot client.
- Connection: `RoomClient` over `WebSocketClientTransport` to same-origin `/ws/{code}`;
  REST via same-origin `/api/*` (guest/register/login, rooms, matchmaking, profile).
- Hosting: `dotnet publish` output copied to a Caddy-served directory; Caddy serves `/`
  statically and keeps proxying `/api/*` and `/ws/*` to `127.0.0.1:8091`. No new container,
  port or CORS configuration.
- Compatibility: `ProtocolInfo.Version` mismatch or `Reject` shows an "update/refresh" screen.

## Screens
1. Home: guest name or login/register; open rooms from `GET /api/rooms`; join by code; quick match.
2. Lobby: seats, bots, ready, board/rules settings, start (mirrors `LobbyState`).
3. Board: 2D or 3D board, HUD, dialogs (buy, auction, trade, build, debt), chat presets,
   reconnect (stored `ResumeToken`), rematch.
View choice (2D/3D) is a local setting; views only animate and cannot change the match.

## Views
- `Board2D`: Blazor/SVG-or-CSS rendering from `GameState` + events.
- `Board3D`: Blazor component owning a canvas; `wwwroot/js/board3d.js` is a bundled, version-pinned
  three.js module exposing `init(canvas, board)`, `applyState(displayState)`,
  `playEvent(event)`, `dispose()`. Called via `IJSRuntime` with JSON display state and one
  event at a time; clicks return to Blazor as intents which become `GameCommand`s.
- Both implement one view contract (display state in, events in, click intents out) and share
  the event-pacing layer. Redaction is already applied server-side before packets are sent.
- Assets reused from `client/assets` where size/licensing allow, otherwise generated geometry.
  If WebGL init fails, fall back to 2D.

## Data flow
Input -> `GameCommand` -> `RoomClient.SendCommand` -> server authority -> `EventBatch`/`Snapshot`
-> replica apply -> pacing layer -> active view. Hash mismatch or gap triggers the existing
resync.

## Error handling
Dropped socket: reconnect with resume token and `ResyncRequest`. `CommandRejected`: toast.
`RoomClosed`: return to home. WebGL failure: 2D fallback.

## Testing
- Session-layer tests (new project) driving `Game.Web` session code against a loopback `RoomHost`.
- Headless-browser (Playwright) smoke test against a running server: guest login, create room,
  add bot, start, take a turn, switch to 3D and verify canvas renders.
- Existing `dotnet test BoardEmpire.sln` must stay green.

## Delivery
`tools/` deploy script publishes into the Caddy-served directory; Caddyfile change for
`monopoly.algosculptor.com`; version bump (`tools/set-version.sh`) and tag per release rules.

## Risks
First load roughly 8-15 MB (cached afterwards). `Game.Core` must only use APIs available in
Blazor WASM (AGENTS.md says it has no Godot, network or wall-clock dependencies).
