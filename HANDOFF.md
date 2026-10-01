# HANDOFF — BoardEmpire

State as of 2026-10-01. Nothing has been committed; the repository is initialised but empty
of commits.

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

## Suggested next steps

1. Install the APK on a phone; fix whatever touch/safe-area/performance issues appear.
2. Push to GitHub, let `android.yml` build the Nearby plugin, test Nearby on two phones.
3. Deploy the server behind Caddy and set the client's default server URL
   (`Settings.ServerUrl`).
4. Replace primitive art and synthesised audio where it matters most (tokens, buildings).
