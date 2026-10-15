# HANDOFF — BoardEmpire

State as of 2026-10-01. Pushed to `github.com/sahuishan01/Monopoly` (`main`); the `CI` workflow
passes there. No release tag exists yet, so the Android workflow has not run.

## What exists and how it was verified

| Area | Status | Evidence |
| --- | --- | --- |
| Rules engine (classic rules, auctions, trades, debt, bankruptcy, jail, cards) | Done | 126 tests in `tests/Core.Tests` |
| Modules: market/city events, objectives, contracts, shares, abilities, projects, dev types, teams | Done | Module tests + invariant checks after every command for 7 presets x 2 boards |
| Determinism, replicas, redaction | Done | Same seed gives identical event stream; replicas fed only redacted JSON events keep the same hash |
| Bots (Easy/Medium/Hard/Expert with Monte Carlo) | Done | 10,000 games in ~30 s |
| Protocol, room authority, reconnect, timers, bot takeover, rematch | Done | 30 tests incl. latency/loss/duplication/reordering |
| Save/restore and host migration | Done | Tests over the loopback transport |
| LAN transport and discovery | Done | Socket test + three real client processes playing a full match with identical hashes |
| Server (auth, rooms, matchmaking, ranked Elo, friends, invites, replays, recovery) | Done | 14 tests incl. real PostgreSQL + Redis and a restart mid-match |
| Container image / compose | Done | Built and smoke-tested on 127.0.0.1:8091, then stopped |
| Online end to end | Done | Two real clients, quick match against the container, match stored in Postgres |
| Client: menus, setup, lobby, HUD, dialogs, results, settings, rules editor, replays | Done | Screenshots under Xvfb; autopiloted human seat through full matches |
| 2D view and 3D city view | Done | Screenshots (software renderer) |
| Android APK (arm64, debug-signed, without Nearby) | Builds | `apksigner verify` passes |

## Not verified

* The APK has **not been run on a device or emulator**. Touch input, haptics, performance,
  battery, notches/safe areas and audio latency are untested.
* The **Nearby plugin has not been compiled**: the Android Gradle toolchain needs x86-64.
  The Kotlin source, the Godot export addon and the C# bridge are written against the
  documented APIs but have never executed. `.github/workflows/android.yml` is meant to build
  it; that workflow and `ci.yml` have never run (no remote).
* Host migration and LAN discovery were exercised in tests and on loopback, not across real
  Wi-Fi networks or phones.
* 3D performance was only seen on a software rasteriser (High/Ultra are too slow there to
  judge).
* No load test, 24-hour soak, memory or battery profiling has been done.

## Deviations from the original plan

* **Godot 4.7 .NET instead of Unity** (builds on ARM64). No Cinemachine/VFX Graph/Addressables;
  the camera rig, particles and audio are custom. Board themes are data, not downloadable
  asset bundles.
* **Art and audio are procedural**: primitive meshes, drawn 2D shapes, synthesised sound and
  music. It is coherent and readable, not production art.
* Wire format is deflated JSON, not MessagePack.
* "Verifiably fair dice" is a seed combined from every client's contribution at match start,
  not a per-turn commit-reveal.
* Simplifications in the rules: a bankrupt player's property returns to the bank (no
  auction); a landmark that cannot be broken into four buildings drops as far as the bank's
  supply allows; no fee when a mortgaged property changes hands; a player who cannot pay the
  detention fine on the last attempt is released into debt without moving.

## Not implemented

Google Play sign-in and Play Integrity; a community board creator UI (custom boards load from
JSON only); temporary-ownership contracts; boats; voice lines; on-device subtitle toggle is a
setting only; iOS.

## Known issues / ideas

* Seat order matters in bot simulations: first seat wins ~27 %, fourth ~22 % (4 players,
  classic). A compensation rule for later seats would be a small `GameRules` addition.
* Classic bot games average ~170 turns (~2 hours at 45 s per turn); Quick and Blitz exist for
  shorter sessions.
* Hindi localisation covers the main labels only.
* The server keeps rooms in memory per instance; Redis stores presence and room-to-instance
  routing, but there is no cross-instance room hand-off.

## Environment notes (this machine)

* .NET 8 and 9 SDKs in `~/.dotnet`; Godot at
  `~/.local/opt/godot/Godot_v4.7.2-stable_mono_linux_arm64/`; export templates in
  `~/.local/share/godot/export_templates/4.7.2.stable.mono`; Android SDK in `~/Android/Sdk`.
* `~/.config/godot/editor_settings-4.7.tres` had `export/android/java_sdk_path` set (backup
  next to it with the suffix `.bak-boardempire`).
* `.env` in the repo root holds generated secrets for the compose stack (ignored by git).
* Port 8090 was already taken on this host, so the stack uses 8091.

## Android signing (one key for every build)

* Every APK, local or CI, debug or release, is signed with the same key so that a new build
  installs as an update. Its SHA-256 is pinned in `deploy/android-signing-sha256.txt`;
  `tools/build-android.sh` refuses to produce an APK with any other signature and never
  falls back to a generated key.
* The keystore and its password live outside the repo in `~/.config/boardempire/`
  (`boardempire-release.keystore`, `signing.env`, mode 600) and in the GitHub secrets
  `RELEASE_KEYSTORE_BASE64`, `RELEASE_KEY_ALIAS`, `RELEASE_KEY_PASSWORD`.
  **Back the keystore up**: if it is lost, installed copies can never be updated.
* Android also requires a growing versionCode. `tools/set-version.sh X.Y.Z` writes the
  version to `project.godot` and both export presets (code = major*10000 + minor*100 + patch);
  the Android workflow fails if the tag does not match.

## Backend deployment

* The compose stack runs on this host (`restart: unless-stopped`, server on 127.0.0.1:8091).
* `/home/opc/manager/Caddyfile` has a `monopoly.algosculptor.com` block (same origin
  certificate as the other sites; backup `Caddyfile.bak-20261001`).
* The client's default server URL is `https://monopoly.algosculptor.com`.
* Cloudflare has a proxied `A` record `monopoly -> 140.238.242.70`. Verified from outside:
  `/healthz` returns 200 and two clients played a full quick match over `wss://`.

## Suggested next steps

1. Install the APK on a phone; fix whatever touch/safe-area/performance issues appear.
2. Push to GitHub, let `android.yml` build the Nearby plugin, test Nearby on two phones.
3. Deploy the server behind Caddy and set the client's default server URL
   (`Settings.ServerUrl`).
4. Replace primitive art and synthesised audio where it matters most (tokens, buildings).

---

# Web client (Blazor WebAssembly, "Studio" redesign)

State as of 2026-10-15, branch `feat/web-client`. Not deployed by this work, not tagged and not pushed.

## Structure

| Concern | Location |
| --- | --- |
| Pages | `src/Game.Web/Pages/Home.razor`, `Lobby.razor`, `Play.razor` |
| Board views | `Components/Board2D.razor` (DOM/CSS board, `wwwroot/css/board2d.css`, `js/board2d-fx.js`) and `Components/Board3D.razor` (`js/board3d.js`) |
| 3D scene | `js/3d/scene.js` (world: lights, slab, inlay, tiles, labels, pawns, buildings), `materials.js` (theme-driven materials, felt/inlay canvas textures), `camera.js` (fitted orbit camera, touch), `motion.js` (tweens, hops, dice) |
| Effects | `js/fx.js` (particle pool, bloom + vignette composer on High, frame-time probe) |
| Home hero | `Components/HeroBoard.razor` + `js/hero3d.js` (scripted demo on the 3D world) / `js/hero-policy.js` (gl / css / still) |
| HUD and dialogs | `Hud.razor`, `HudBar.razor` (mobile peek strip), `PropertyCard.razor`, `TradeDialog.razor`, `EventLog.razor`, `SettingsSheet.razor`, `Sheet.razor`, `Toasts.razor`, `TurnBanner.razor`, `DiceTray.razor`, `Money.razor` |
| Audio | `Components/AudioBridge.razor` + `js/audio.js` (WebAudio synthesis, no files) |
| Shortcuts | `js/keys.js` (Space roll, B buy, E end turn, M mute, L log, Esc close) |
| Services | `Services/SettingsService.cs` (localStorage `boardempire.settings`), `Session.cs`, `GameLoop.cs` |
| Shared, testable logic | `src/Game.WebClient` (`GameSession`, `EventPacer`, `Cues`, `EventText`, `LobbyText`, `ThemeCatalog`, `DisplayPayload`, `BoardLayout2D`, `TurnActions`) |
| CSS | `css/tokens.css` (tokens), `layout.css`, `components.css`, `ui.css` (Play), `board2d.css`, `mobile.css` (≤800 px sheet / landscape rail), `screens.css` (Home/Lobby, loaded last) |

## Themes

The four themes are `neon-noir` (default), `classic-gold`, `frost` and `sakura`. Each is defined in `wwwroot/themes/<id>.json`:
- `css`: tokens set on `<html>`.
- `scene`: 3D values. `sky[2]`, `felt`, `fog`, `ambient`, `sun` and `bloom` are required; `exposure` is optional and defaults to 1.3.
- `token`: `metal` and `rough`.
- `sound`: `wave` and `root` (Hz).

`theme.js` applies a theme:
- It sets `data-theme` and the tokens.
- It sets meta theme-color to the theme's `--bg`.
- It sets `html.theme-switching` for two frames while it swaps the tokens. That class turns transitions off; without it, Chrome can leave var()/color-mix colour transitions stuck on the old theme.

To add a theme:
1. Add `wwwroot/themes/<id>.json` with all keys (`ThemeCatalog.Validate` lists any that are missing).
2. Add the id to `ThemeCatalog.Ids`.
3. Add its picker colours to `ThemeCatalog.Swatches`. A test checks these against the JSON.

`ThemeCatalogTests` fails until all three are in step. The 3D scene, hero, audio and 2D board read the theme at runtime; nothing else needs changing.

## Quality tiers (3D)

- **High**: shadows (2048 PCF soft), bloom + vignette composer, 400 particles, DPR up to 2.
- **Low**:
  - No shadows and no composer. 80 particles.
  - DPR is capped at 1.5 on devices with high DPR and more than 4 cores, and at 1 otherwise.
  - Lights, slab, inlay and felt are the same as High. They cost a few draw calls plus canvas textures made once per theme.
- **Auto**:
  - Picks Low at ≤700 px wide or ≤4 cores (`hero-policy.js autoTier`), High otherwise.
  - On High it runs a 20-frame probe. One slow probe (median >28 ms) drops to Low. Two good probes restore High.
- **Reduced motion** (OS setting or in-app): hops, dice and springs complete instantly, and there is no idle bob or camera easing. The hero becomes a still CSS board.
- **WebGL context loss**:
  - The Home hero switches to the CSS hero for that visit.
  - Play falls back to 2D with a toast, and the saved 3D preference is kept. Picking 3D again retries.

## Audio (event -> cue)

`Cues.For` (Game.WebClient) maps each event to a sound and the pacer hold time:

| Event | Sound | Hold |
| --- | --- | --- |
| DiceRolled | dice | 0.9 s |
| PlayerMoved | hop | 0.7 s |
| PropertyPurchased | buy | 0.5 s |
| BuildingConstructed | build | 0.6 s |
| BuildingSold | sell | 0.4 s |
| RentPaid / MoneyTransferred / SalaryPaid | cash | 0.5 s |
| PlayerSentToJail | jail | 0.8 s |
| CardDrawn | card | 0.6 s |
| AuctionStarted / AuctionCompleted | hammer | 0.6 s |
| TradeAccepted | trade | 0.5 s |
| PlayerBankrupt | bankrupt | 0.9 s |
| GameEnded | win | 1.2 s |
| TurnStarted | chime | 0.3 s |

Bursts (resync, more than 12 pending events) skip to the end and keep only the win and bankrupt cues; `Cues.Collapse` allows at most 3 cues of each kind.

`audio.js` details:
- The AudioContext is unlocked on the first gesture.
- There are master, sfx and music buses, with the theme's waveform and root note.
- A pentatonic ambient pad plays.
- Voices are capped at 12.
- Every entry point does nothing when AudioContext is missing.

Volumes and mute are persisted, and `M` toggles mute.

## Checks

See `tools/web-smoke/README.md`. In short:

```bash
dotnet test BoardEmpire.sln
export CHROMIUM_PATH=~/.cache/ms-playwright/chromium-1234/chrome-linux/chrome
node tools/web-smoke/run-all.mjs --out=/tmp/web-checks
```

`run-all.mjs` runs the JS unit tests, render-check, smoke, e2e-check, home-check and mobile-check. It publishes the client and starts Game.Server and a proxy on free loopback ports itself, and kills them afterwards. On arm64, WebGL needs `--use-gl=angle --use-angle=gl-egl` (the default there); never use SwiftShader on this host.

The last full run on 2026-10-15 (arm64, 2 cores, software GL) passed:
- `dotnet test`: 242 passed, 4 skipped.
- JS unit: 5/5.
- render-check: 14 renders and 5 framings.
- smoke: ok.
- e2e-check: 29/29.
- home-check: 87/87. It was re-run after the camera and 2D changes: 87/87.
- mobile-check: 204/204.

## Size (Release publish)

- `wwwroot`: 13.8 MB on disk, which includes the precompressed `.gz`/`.br` copies and the icons.
- First load (`_framework` + `js` + `css` + `themes`, 93 files): 8.19 MB raw, 3.08 MB with gzip -9, 2.57 MB with brotli -q 11. The build already ships `.br` copies totalling 2.38 MB.
- That is well inside the 25 MB budget.
- `three.module.min.js` (0.67 MB raw) is fetched only when a 3D view or the GL hero starts.

## Known limitations

- **Phone boards**: on a 360 px phone, 2D edge tiles are about 30 px, below the 44 px touch rule. A square 40-tile board cannot meet it. The 44 px rule is enforced for HUD, toolbar, sheet and peek controls. Side prices are about 6 px there, and pawns can cover a price or corner label.
- **3D legibility**:
  - At desktop size, 40-tile boards make tiles about 40 px on screen. Names are readable on the near rows but small on the far row.
  - Portrait 3D is width-bound, so sky and felt bands remain above and below the board.
  - Low on a 2-core, DPR-2 phone renders at 1x and looks soft.
- **3D taps**: a 3D tile tap while the follow camera is still gliding can miss once (seen in scripted runs, not root-caused).
- **Not reproduced in tests**: the original 2D hop flicker cannot be reproduced by the frame sampler, so its fix is verified by reasoning and by the sampler only.
- **Untested platforms**: not checked on a real GPU, on iOS Safari or on Firefox. All browser checks use Chromium with Mesa llvmpipe. Frame times were measured only under software GL (see "Frame time" below). They are not device numbers, and no real-device frame budget has been checked.
- **Cold load**: the boot screen ("Loading BoardEmpire…") uses the default tokens until Blazor applies the saved theme, about a second later.
- **Mobile layout**: the landscape rail repeats "Your turn" in the peek strip and the HUD head.
- **Lobby**: the sticky action bar covers content while it scrolls past, as sticky bars do. It is opaque, and the end of the page clears it.
- **Event text and toasts**:
  - `EventText` omits ability, objective, economy, project and contract events.
  - Toasts show bot events too.
- **Context loss**: the hero stays CSS for the rest of the visit after a context loss.

## Frame time (software GL, not device numbers)

How it was measured:
- Play vs 3 bots at 1280x800 in headless Chromium on this 2-core aarch64 host.
- WebGL ran on Mesa llvmpipe (`--use-angle=gl-egl`): every pixel is rasterised on the CPU, which Game.Server shares.
- Intervals between `requestAnimationFrame` calls over 6 s, with CDP `Emulation.setCPUThrottlingRate` set to 1 and then 4.

```
3d high cpu x1: frames 15, median 433.3 ms, p95 566.6 ms, ~2.5 fps
3d high cpu x4: frames 9, median 699.9 ms, p95 750.0 ms, ~1.5 fps
3d low  cpu x1: frames 18, median 316.7 ms, p95 450.0 ms, ~3.0 fps
3d low  cpu x4: frames 11, median 500.0 ms, p95 733.2 ms, ~1.8 fps
2d low  cpu x1: frames 34, median 66.7 ms, p95 150.0 ms, ~5.7 fps
2d low  cpu x4: frames 48, median 116.6 ms, p95 216.6 ms, ~8.0 fps
```

What this does and does not show:
- The ordering holds: Low is cheaper than High, and 2D is much cheaper than 3D.
- The absolute values are dominated by CPU rasterisation, and the throttle does not slow the GPU process, so they do not predict a phone or desktop with a GPU.
- Measuring on real hardware is still open: a mid-range Android phone and an integrated-GPU laptop, using the in-app Auto probe (it downgrades past a 28 ms median).

## Screenshots

`docs/screenshots/*.webp` (0.47 MB in total) shows each of the four themes:
- desktop 1280x800: Home, Play 3D and Play 2D, downscaled to 800 px wide;
- phone 390x844: Play 3D and Play 2D, downscaled to 360 px wide;
- plus one desktop and one phone Lobby.

They are named `<theme>-<desktop|phone>-<screen>.webp`, for example `neon-noir-desktop-play-3d.webp`. They were captured on software GL (llvmpipe).

## Deploy notes

- `tools/deploy-web.sh [/home/opc/web/<dir>]` publishes into a temp dir. It refuses an incomplete output, then rsyncs `wwwroot/` into the target, which must be a subdirectory of `/home/opc/web`. It was **not** run for this work.
- Releases need explicit confirmation: version bump (`tools/set-version.sh`), tag and push.
- Serve the precompressed `.br`/`.gz` files when the proxy supports it. Caddy needs `file_server { precompressed br gzip }`.
- Keep `/api` and `/ws` routed to Game.Server on loopback.
- No service worker is shipped on purpose.
