# AGENTS.md — BoardEmpire

Guidance for AI agents (and humans) working in this repository.

## Invariants — do not break these

1. `Game.Core` has **no** Godot, networking or wall-clock dependencies. Rules never go into
   client scripts.
2. State changes only through events: a command handler validates first, then calls `Emit`.
   A handler must not return an error after emitting (the engine throws if it does).
3. Every event's `Apply` must be deterministic from the event's own data plus the state.
   Never read the RNG, the clock or a `Dictionary` iteration order inside `Apply`.
4. Money is `int`. No floating point in rules or reducers (bots may use it for scoring).
5. Anything a player must not see is redacted in **both** `GameState.RedactedFor` and the
   event's `RedactFor`, and must be excluded from the public hash (i.e. be redacted for -1).
6. New state fields need: a `Clone` update, JSON round-trip coverage
   (`State_survives_clone_and_json_roundtrip_mid_game`), and a thought about redaction.
7. Changing the shape of a command, event or message means bumping `ProtocolInfo.Version`
   (and `MinimumSupported` when old clients cannot cope).
8. Views (`IGameView`) only animate. They are given the display state and one event; they
   never send commands except through `BoardScreen`.
9. Server backends bind to loopback; secrets come from the environment (`.env` is ignored).

## Where things live

| Concern | Location |
| --- | --- |
| Commands / events / reducers | `src/Game.Core/Commands`, `src/Game.Core/Events/GameEvents.cs` |
| Turn flow, debts, bankruptcy | `src/Game.Core/Engine/GameEngine.cs` |
| Purchases, auctions, building, trades | `src/Game.Core/Engine/GameEngine.Property.cs` |
| Rule queries (rent, prices, legality) | `src/Game.Core/Rules/Calc.cs`, `TradeRules.cs` |
| Optional modules | `src/Game.Core/Rules/Modules/RuleModules.cs` |
| Presets | `src/Game.Core/Rules/GameRules.cs` |
| Boards | `src/Game.Core/Board/BoardLibrary.cs` (+ `boards/*.json`) |
| Bots | `src/Game.Core/AI` |
| Authority (lobby, timers, bots, reconnect, save) | `src/Game.Net/RoomHost.cs` |
| Replica, resync, resend | `src/Game.Net/RoomClient.cs` |
| Client session modes, autosave, host migration | `client/src/Net/GameSession.cs` |
| Event pacing | `client/src/Presentation/BoardPresenter.cs` |
| HUD and dialogs | `client/src/UI/BoardScreen*.cs` |
| 2D / 3D | `client/src/View2D`, `client/src/View3D` |
| Theme tokens and widgets | `client/src/Core/Ui.cs` |

## Workflow

* `dotnet test BoardEmpire.sln` must pass before a change is considered done. Rule changes
  need a unit test in `tests/Core.Tests`; the simulation tests check invariants after every
  command for every preset.
* After touching the client: `dotnet build client/BoardEmpire.csproj`, then run a headless
  bot match (`godot --headless --path client -- --demo tycoon --human --autoplay --speed minimal --quit-after 90`)
  and check the log for errors. For visual changes take a screenshot with `--shot`.
* The client UI is built in code (no `.tscn` layouts besides `Main.tscn`). Nodes are
  configured before they enter the tree, so do not rely on `_Ready` for fields that
  `Build()` needs.
* Balance changes: compare `tools/Simulator` output before and after (10,000 games takes
  about 30 seconds).
* Add a rule module by implementing `IRuleModule`, gating it with a `GameRules` flag and
  registering it in `RuleModules.For`.
