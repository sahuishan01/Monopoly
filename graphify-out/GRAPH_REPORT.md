# Graph Report - monopoly  (2026-10-01)

## Corpus Check
- 103 files · ~99,671 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2768 nodes · 7394 edges · 126 communities (112 shown, 14 thin omitted)
- Extraction: 84% EXTRACTED · 16% INFERRED · 0% AMBIGUOUS · INFERRED: 1166 edges (avg confidence: 0.83)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `8a5a9b19`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- .Private_room_match_is_played_persisted_and_replayable
- GameEvent
- GameState
- TestGame
- GameCommands.cs
- Synth
- GameEngine
- RoomClient
- IRuleModule
- Task
- LobbyScreen
- .BuildTiles
- ChaosTransport
- GameSession
- OnlineApi
- PostgresStore
- Harness
- Board2DView
- PlayerState
- BoardScreen
- RoomHost
- .VBox
- .OnHello
- ActiveEffect
- CityAmbience
- Server.Tests.csproj
- .Button
- TradeOffer
- Settings
- NearbyBridge
- LanHostTransport
- BoardPresenter
- GameRules
- CameraRig
- Game.Protocol
- Game.Core.Rules
- BoardLibrary
- App
- LocalProfile
- TradeSide
- Messages.cs
- .Ok
- .Create
- BoardEmpire.Core
- ProjectState
- NetworkEnvelope
- AuctionState
- PropertyState
- RoomManager
- .Receive
- WebSocketClientTransport
- Board3DView
- NearbyPlugin
- Screen
- SetupScreen
- MatchRecord
- GameCommand
- BoardDefinition
- TokenService
- .Fair_seed_combines_all_contributions
- Contract
- PlayerStats
- RoomHostOptions
- GameRules.cs
- HostSession
- CardEffect
- Modal
- DebtState
- UserStats
- SeatInfo
- .Server_on_postgres_and_redis_survives_a_restart_mid_match
- WebSocketRoomTransport
- MatchConfig
- IGameView
- .OnEventStarting
- Dice3D
- CommandResult
- RoomSave
- Settings.cs
- TileType
- GameEndReason
- AnimationSpeed
- LobbyInfo
- Fact
- ObjectiveKind
- RejectCode
- ServerOptions
- .Box
- SessionKind
- CoreJson
- SetupMode
- BankState
- ClientStatus
- LanAdvertisement
- Task
- HANDOFF — BoardEmpire
- .DisposeAsync
- ReplayFile
- Pcg32
- TileDef
- ReplayPlayer
- build-android.sh
- CardDef
- .Finished
- BoardEmpire
- .Enqueue
- JailExit
- BotDriver
- Table
- Results.cs
- set-version.sh
- EffectScope
- .Every_preset_resolves
- AGENTS.md — BoardEmpire
- AnimContext
- DistrictDef
- ScriptedRandom
- Autopilot
- .From
- rules/graphify.md
- workflows/graphify.md
- NearbyConnectionsPlugin/README.md

## God Nodes (most connected - your core abstractions)
1. `GameState` - 227 edges
2. `GameEvent` - 99 edges
3. `TestGame` - 93 edges
4. `GameEngine` - 91 edges
5. `RoomHost` - 88 edges
6. `RoomClient` - 84 edges
7. `BoardScreen` - 74 edges
8. `GameRules` - 61 edges
9. `BoardDefinition` - 59 edges
10. `Board2DView` - 58 edges

## Surprising Connections (you probably didn't know these)
- `LocalProfile` --references--> `Description`  [EXTRACTED]
  client/src/Core/LocalProfile.cs → src/Game.Core/State/GameState.cs
- `LocalProfile` --references--> `Id`  [EXTRACTED]
  client/src/Core/LocalProfile.cs → src/Game.Core/State/GameState.cs
- `SavedPreset` --references--> `GameRules`  [EXTRACTED]
  client/src/Core/Settings.cs → src/Game.Core/Rules/GameRules.cs
- `GameSession` --references--> `HostSession`  [EXTRACTED]
  client/src/Net/GameSession.cs → src/Game.Net/HostSession.cs
- `GameSession` --references--> `RoomClient`  [EXTRACTED]
  client/src/Net/GameSession.cs → src/Game.Net/RoomClient.cs

## Import Cycles
- None detected.

## Communities (126 total, 14 thin omitted)

### Community 0 - ".Private_room_match_is_played_persisted_and_replayable"
Cohesion: 0.43
Nodes (5): Fact, Func, JsonElement, Task, ServerTests

### Community 1 - "GameEvent"
Cohesion: 0.04
Nodes (42): Ability, IReadOnlyCollection, TileType, AbilityAssigned, AbilityUsed, AuctionCompleted, AuctionPassed, BidPlaced (+34 more)

### Community 2 - "GameState"
Cohesion: 0.04
Nodes (45): Func, List, Calc, TradeRules, Func, IEnumerable, IReadOnlyCollection, DevelopmentType (+37 more)

### Community 3 - "TestGame"
Cohesion: 0.14
Nodes (11): RentPaid, BuildHouseCommand, RollDiceCommand, ClassicRulesTests, Fact, TestGame, Dice, Engine (+3 more)

### Community 4 - "GameCommands.cs"
Cohesion: 0.10
Nodes (19): Dictionary, BotBrain, Level, Rollouts, Smart, BuyPropertyCommand, CancelTradeCommand, CounterTradeCommand (+11 more)

### Community 5 - "Synth"
Cohesion: 0.10
Nodes (24): AudioStream, AudioStreamPlayer, AudioStreamWav, AudioDirector, Sfx, Bankrupt, Build, Card (+16 more)

### Community 6 - "GameEngine"
Cohesion: 0.09
Nodes (13): IReadOnlyList, List, TileType, Board, Current, Modules, Rules, State (+5 more)

### Community 7 - "RoomClient"
Cohesion: 0.06
Nodes (31): PendingCommand, SortedDictionary, BotLevel, CancellationToken, ConcurrentQueue, Dictionary, HashSet, List (+23 more)

### Community 8 - "IRuleModule"
Cohesion: 0.11
Nodes (17): ObjectiveCompleted, GameRules, List, AbilityRuleModule, Id, ClassicRules, Id, ContractRuleModule (+9 more)

### Community 9 - "Task"
Cohesion: 0.07
Nodes (24): MatchData, ConcurrentDictionary, HashSet, IEnumerable, IReadOnlyList, List, Task, ActiveMatch (+16 more)

### Community 10 - "LobbyScreen"
Cohesion: 0.17
Nodes (7): DevAutomation, Autoplay, App, Task, LobbyScreen, HBoxContainer, Task

### Community 11 - ".BuildTiles"
Cohesion: 0.11
Nodes (9): BoardLayout, CornerSize, Count, TileWidth, List, Rect2, Vector2, ImageTexture (+1 more)

### Community 12 - "ChaosTransport"
Cohesion: 0.07
Nodes (25): IAsyncDisposable, LoopbackClient, Outgoing, Packet, Task, At, CancellationToken, ConcurrentDictionary (+17 more)

### Community 13 - "GameSession"
Cohesion: 0.08
Nodes (18): GameSession, Api, Client, HasSave, Host, IsAuthority, Kind, LanPort (+10 more)

### Community 14 - "OnlineApi"
Cohesion: 0.13
Nodes (14): ApiResult, OnlineApi, BaseUrl, Token, HttpClient, JsonElement, Task, OnlineScreen (+6 more)

### Community 15 - "PostgresStore"
Cohesion: 0.11
Nodes (16): NpgsqlDataReader, NpgsqlDataSource, NpgsqlParameter, SkippableFact, IEnumerable, IReadOnlyList, JsonElement, List (+8 more)

### Community 16 - "Harness"
Cohesion: 0.18
Nodes (18): Action, Func, Harness, Chaos, Clients, Host, HostHash, Hub (+10 more)

### Community 17 - "Board2DView"
Cohesion: 0.13
Nodes (16): Board2DView, BoardPixels, Node, FloatText, Particle, Dictionary, InputEvent, List (+8 more)

### Community 18 - "PlayerState"
Cohesion: 0.08
Nodes (21): Ability, ObjectiveProgress, Completed, Id, PlayerState, AbilityArmed, AbilityUsed, Bankrupt (+13 more)

### Community 19 - "BoardScreen"
Cohesion: 0.09
Nodes (11): BoxContainer, Currency, D, IsReplay, Button, Control, Label, VBoxContainer (+3 more)

### Community 20 - "RoomHost"
Cohesion: 0.06
Nodes (29): Deadline, Key, SeatState, Action, Dictionary, HashSet, IEnumerable, IReadOnlyList (+21 more)

### Community 21 - ".VBox"
Cohesion: 0.11
Nodes (16): CheckButton, Ui, Action, Control, IEnumerable, LineEdit, PanelContainer, VBoxContainer (+8 more)

### Community 22 - ".OnHello"
Cohesion: 0.25
Nodes (4): SeatState, PeerStatus, Reject, VersionCheck

### Community 23 - "ActiveEffect"
Cohesion: 0.09
Nodes (24): CityEventOccurred, EconomyChanged, ProjectCompleted, MarketRuleModule, Id, ActiveEffect, BuildCostPercent, Description (+16 more)

### Community 24 - "CityAmbience"
Cohesion: 0.12
Nodes (25): Quality, High, Low, Medium, Ultra, CityAmbience, Night, Progress (+17 more)

### Community 25 - "Server.Tests.csproj"
Cohesion: 0.06
Nodes (36): BoardEmpire, net8.0, net9.0, Microsoft.AspNetCore.Mvc.Testing (8.0.11), Npgsql (8.0.5), StackExchange.Redis (2.8.16), Xunit.SkippableFact (1.4.13), Godot.NET.Sdk/4.7.2 (+28 more)

### Community 26 - ".Button"
Cohesion: 0.20
Nodes (7): Color, HBoxContainer, Label, Action, Contract, Control, SpinBox

### Community 27 - "TradeOffer"
Cohesion: 0.11
Nodes (15): Func, IEnumerable, List, TradePlanner, Contract, Valuation, TradeCountered, TradeModified (+7 more)

### Community 28 - "Settings"
Cohesion: 0.07
Nodes (28): Settings, AccountIsGuest, AmbientVolume, AnimationSpeed, AuthToken, CameraShake, ColorBlindPatterns, Haptics (+20 more)

### Community 29 - "NearbyBridge"
Cohesion: 0.09
Nodes (11): NearbyBridge, Available, NearbyClientTransport, IsConnected, NearbyHostTransport, CancellationToken, ConcurrentDictionary, Dictionary (+3 more)

### Community 30 - "LanHostTransport"
Cohesion: 0.09
Nodes (27): Ad, Connection, IDisposable, IPAddress, NetworkStream, Seen, CancellationToken, CancellationTokenSource (+19 more)

### Community 31 - "BoardPresenter"
Cohesion: 0.18
Nodes (9): BoardPresenter, Backlog, Display, IsIdle, Paused, SpeedOverride, View, Queue (+1 more)

### Community 32 - "GameRules"
Cohesion: 0.06
Nodes (33): GameRules, AbilitiesEnabled, AdvancedDevelopment, AuctionMinIncrement, AuctionMode, AuctionsEnabled, CollectRentInJail, ContractsEnabled (+25 more)

### Community 33 - "CameraRig"
Cohesion: 0.09
Nodes (20): Camera3D, InputEvent, CameraRig, AllowShake, Aspect, BoardSize, Camera, ReduceMotion (+12 more)

### Community 34 - "Game.Protocol"
Cohesion: 0.13
Nodes (17): Game.Server.Data, Game.Core.Serialization, Game.Server.Rooms, Game.Net, Net.Tests, Game.Protocol, Server.Tests, Game.Net.Transport (+9 more)

### Community 35 - "Game.Core.Rules"
Cohesion: 0.21
Nodes (12): EventText, Game.Core.Events, Game.Core.Rules.Modules, Game.Core.State, Core.Tests, BoardEmpire.Presentation, Game.Core.Board, Game.Core.Rng (+4 more)

### Community 36 - "BoardLibrary"
Cohesion: 0.19
Nodes (11): House, Lazy, Skin, Id, Dictionary, IEnumerable, IReadOnlyCollection, BoardLibrary (+3 more)

### Community 37 - "App"
Cohesion: 0.10
Nodes (15): App, Audio, Current, I, Profile, Session, Settings, InputEvent (+7 more)

### Community 38 - "LocalProfile"
Cohesion: 0.07
Nodes (27): LocalProfile, Achievements, AuctionsWon, AveragePosition, Bankruptcies, BuildingsBuilt, GamesPlayed, LargestPayment (+19 more)

### Community 39 - "TradeSide"
Cohesion: 0.09
Nodes (22): UseAbilityCommand, RentWaived, ContractTerm, Amount, Count, Kind, Percent, Tile (+14 more)

### Community 40 - "Messages.cs"
Cohesion: 0.12
Nodes (29): BotLevel, AddBot, AddLocalPlayer, ChatMessage, EventBatch, Heartbeat, Hello, KickPlayer (+21 more)

### Community 41 - ".Ok"
Cohesion: 0.17
Nodes (11): AcceptTradeCommand, ContributeToProjectCommand, CreateTradeCommand, DeclareBankruptcyCommand, DeclinePropertyCommand, EndTurnCommand, MortgageCommand, PassAuctionCommand (+3 more)

### Community 42 - ".Create"
Cohesion: 0.24
Nodes (7): StateHasher, SimulationTests, IEnumerable, InlineData, List, MemberData, Theory

### Community 43 - "BoardEmpire.Core"
Cohesion: 0.22
Nodes (7): BoardEmpire.UI, BoardEmpire, BoardEmpire.Core, BoardEmpire.View3D, BoardEmpire.View2D, Game.Core.Replay, BoardEmpire.Net

### Community 44 - "ProjectState"
Cohesion: 0.09
Nodes (18): ProjectContribution, ProjectFailed, ProjectProposed, Name, PublicProjectModule, Id, Contribution, Amount (+10 more)

### Community 45 - "NetworkEnvelope"
Cohesion: 0.13
Nodes (16): Exception, ReadOnlySpan, Dictionary, JsonElement, JsonSerializerOptions, NetworkEnvelope, MatchId, MessageId (+8 more)

### Community 46 - "AuctionState"
Cohesion: 0.10
Nodes (15): AuctionStarted, SealedBidsRevealed, List, AuctionState, HighBid, HighBidder, Mode, Participants (+7 more)

### Community 47 - "PropertyState"
Cohesion: 0.08
Nodes (23): MoneyReason, Card, Contract, Gift, Jackpot, JailFine, ProjectBonus, Tax (+15 more)

### Community 48 - "RoomManager"
Cohesion: 0.08
Nodes (25): BackgroundService, Channel, ILogger, CancellationToken, ConcurrentDictionary, Func, IEnumerable, Random (+17 more)

### Community 49 - ".Receive"
Cohesion: 0.20
Nodes (3): Peer, ChatPresets, CommandRejected

### Community 50 - "WebSocketClientTransport"
Cohesion: 0.23
Nodes (9): CancellationToken, CancellationTokenSource, Func, SemaphoreSlim, Task, ValueTask, WebSocket, WebSocketClientTransport (+1 more)

### Community 51 - "Board3DView"
Cohesion: 0.12
Nodes (21): Board3DView, Node, TileVisual, Control, CpuParticles3D, Dictionary, DirectionalLight3D, List (+13 more)

### Community 52 - "NearbyPlugin"
Cohesion: 0.13
Nodes (5): NearbyPlugin, ByteArray, ConnectionsClient, GodotPlugin, SignalInfo

### Community 53 - "Screen"
Cohesion: 0.06
Nodes (18): LanBrowserScreen, NearbyScreen, Dictionary, Label, VBoxContainer, PlayMenuScreen, Control, ProfileScreen (+10 more)

### Community 54 - "SetupScreen"
Cohesion: 0.14
Nodes (12): LocalSeat, BotLevel, IsBot, Name, BotLevel, SetupScreen, Label, List (+4 more)

### Community 55 - "MatchRecord"
Cohesion: 0.10
Nodes (19): DateTime, MatchPlayerRecord, IsBot, Name, NetWorth, Rank, RatingChange, Seat (+11 more)

### Community 56 - "GameCommand"
Cohesion: 0.15
Nodes (11): Action, IReadOnlyList, MonteCarlo, GameCommand, CommandId, ExpectedVersion, MatchId, Timestamp (+3 more)

### Community 57 - "BoardDefinition"
Cohesion: 0.10
Nodes (17): Dictionary, List, BoardDefinition, BoardId, CivicCards, Count, Currency, Districts (+9 more)

### Community 58 - "TokenService"
Cohesion: 0.23
Nodes (6): TimeSpan, PasswordHasher, TokenClaims, TokenService, Lifetime, ArgumentException

### Community 60 - "Contract"
Cohesion: 0.10
Nodes (20): Contract, Amount, Beneficiary, Grantor, Id, Kind, Percent, Remaining (+12 more)

### Community 61 - "PlayerStats"
Cohesion: 0.10
Nodes (20): PlayerStats, AuctionsWon, DiceTotal, Doubles, EliminatedOnTurn, HousesBuilt, LargestPayment, MoneySpentOnProperty (+12 more)

### Community 62 - "RoomHostOptions"
Cohesion: 0.10
Nodes (21): BotLevel, Func, Name, RoomHostOptions, AllowLocalPlayers, Authenticate, BoardId, BotDelaySeconds (+13 more)

### Community 63 - "GameRules.cs"
Cohesion: 0.12
Nodes (15): Ability, Banker, Builder, Investor, Negotiator, None, AuctionMode, Classic (+7 more)

### Community 64 - "HostSession"
Cohesion: 0.09
Nodes (15): Data, Kind, CancellationToken, ConcurrentQueue, List, Task, ValueTask, HostSession (+7 more)

### Community 65 - "CardEffect"
Cohesion: 0.17
Nodes (12): CardEffect, Collect, CollectFromEach, GoToJail, JailCard, MoveRelative, MoveTo, MoveToNearestTransit (+4 more)

### Community 67 - "DebtState"
Cohesion: 0.12
Nodes (15): DebtIncurred, PhaseChanged, DebtState, Amount, Creditor, Debtor, Reason, Tile (+7 more)

### Community 68 - "UserStats"
Cohesion: 0.09
Nodes (21): Rank, Rating, UserStats, AuctionsWon, Bankruptcies, BuildingsBuilt, GamesPlayed, LargestPayment (+13 more)

### Community 69 - "SeatInfo"
Cohesion: 0.12
Nodes (14): Seconds, Ability, SeatInfo, Ability, BotLevel, Connected, IsBot, Name (+6 more)

### Community 70 - ".Server_on_postgres_and_redis_survives_a_restart_mid_match"
Cohesion: 0.20
Nodes (9): IWebHostBuilder, Session, Program, JsonElement, TestServer, Options, Rooms, Store (+1 more)

### Community 71 - "WebSocketRoomTransport"
Cohesion: 0.19
Nodes (10): CancellationToken, CancellationTokenSource, ConcurrentDictionary, SemaphoreSlim, Task, ValueTask, WebSocket, Connection (+2 more)

### Community 72 - "MatchConfig"
Cohesion: 0.15
Nodes (13): List, MatchConfig, BoardId, MatchId, Players, Rules, Seed, PlayerSetup (+5 more)

### Community 73 - "IGameView"
Cohesion: 0.29
Nodes (3): IGameView, Node, Control

### Community 75 - "Dice3D"
Cohesion: 0.27
Nodes (5): Dice3D, ImageTexture, Random, Task, Vector3

### Community 76 - "CommandResult"
Cohesion: 0.38
Nodes (5): IReadOnlyList, CommandResult, Error, Events, Ok

### Community 77 - "RoomSave"
Cohesion: 0.17
Nodes (8): RoomSave, Board, Config, Events, Initial, Lobby, State, IReadOnlyList

### Community 78 - "Settings.cs"
Cohesion: 0.17
Nodes (10): Loc, Language, Dictionary, SavedPreset, Name, Rules, ViewMode, City3D (+2 more)

### Community 79 - "TileType"
Cohesion: 0.17
Nodes (11): IEnumerable, TileType, Civic, Fortune, GoToJail, Jail, Plaza, Start (+3 more)

### Community 80 - "GameEndReason"
Cohesion: 0.17
Nodes (10): GameEnded, GameEndReason, FirstBankruptcy, LastStanding, None, RoundLimit, Standing, NetWorth (+2 more)

### Community 81 - "AnimationSpeed"
Cohesion: 0.50
Nodes (4): AnimationSpeed, Fast, Minimal, Normal

### Community 82 - "LobbyInfo"
Cohesion: 0.17
Nodes (12): List, LobbyInfo, BoardId, HostPeerId, InMatch, Ranked, Revision, RoomCode (+4 more)

### Community 83 - "Fact"
Cohesion: 0.32
Nodes (4): InvalidOperationException, InfrastructureTests, ArgumentException, Fact

### Community 84 - "ObjectiveKind"
Cohesion: 0.16
Nodes (13): ObjectiveCatalog, ObjectiveDef, ObjectiveKind, BuildHouses, CollectSalary, CompleteTrades, EarnRent, HoldCash (+5 more)

### Community 85 - "RejectCode"
Cohesion: 0.25
Nodes (8): RejectCode, BadToken, Kicked, MatchInProgress, RoomClosed, RoomFull, VersionTooNew, VersionTooOld

### Community 86 - "ServerOptions"
Cohesion: 0.18
Nodes (11): ServerOptions, AbandonedMatchSeconds, BotDelaySeconds, IdleRoomSeconds, InstanceId, MatchmakingFillSeconds, MatchmakingTargetSeats, MaxRooms (+3 more)

### Community 87 - ".Box"
Cohesion: 0.17
Nodes (8): ButtonKind, Danger, Ghost, Primary, Secondary, Tokens, Button, StyleBoxFlat

### Community 88 - "SessionKind"
Cohesion: 0.25
Nodes (8): SessionKind, LanClient, LanHost, Local, NearbyClient, NearbyHost, None, Online

### Community 89 - "CoreJson"
Cohesion: 0.33
Nodes (3): JsonTypeInfo, JsonSerializerOptions, CoreJson

### Community 90 - "SetupMode"
Cohesion: 0.33
Nodes (6): SetupMode, HostLan, HostNearby, OnlinePrivate, PassAndPlay, Solo

### Community 91 - "BankState"
Cohesion: 0.40
Nodes (4): BankState, HotelsLeft, HousesLeft, Jackpot

### Community 92 - "ClientStatus"
Cohesion: 0.40
Nodes (5): ClientStatus, Connecting, Disconnected, InMatch, Lobby

### Community 93 - "LanAdvertisement"
Cohesion: 0.15
Nodes (12): LanAdvertisement, Address, Board, HostName, InMatch, MaxPlayers, Players, Port (+4 more)

### Community 94 - "Task"
Cohesion: 0.12
Nodes (13): ConnectionMultiplexer, IDatabase, ConcurrentDictionary, Dictionary, IEnumerable, List, Task, TimeSpan (+5 more)

### Community 95 - "HANDOFF — BoardEmpire"
Cohesion: 0.18
Nodes (10): Android signing (one key for every build), Backend deployment, Deviations from the original plan, Environment notes (this machine), HANDOFF — BoardEmpire, Known issues / ideas, Not implemented, Not verified (+2 more)

### Community 101 - "ReplayFile"
Cohesion: 0.19
Nodes (9): List, Award, MatchStory, ReplayFile, Board, Events, FormatVersion, Initial (+1 more)

### Community 102 - "Pcg32"
Cohesion: 0.31
Nodes (4): Pcg32, RngState, Inc, S

### Community 103 - "TileDef"
Cohesion: 0.22
Nodes (9): TileDef, District, HouseCost, IsOwnable, Name, Price, Rents, TaxAmount (+1 more)

### Community 104 - "ReplayPlayer"
Cohesion: 0.33
Nodes (5): ReplayPlayer, AtEnd, Length, Position, State

### Community 105 - "build-android.sh"
Cohesion: 0.25
Nodes (7): GODOT_ANDROID_KEYSTORE_DEBUG_PASSWORD, GODOT_ANDROID_KEYSTORE_DEBUG_PATH, GODOT_ANDROID_KEYSTORE_DEBUG_USER, GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD, GODOT_ANDROID_KEYSTORE_RELEASE_PATH, GODOT_ANDROID_KEYSTORE_RELEASE_USER, build-android.sh script

### Community 106 - "CardDef"
Cohesion: 0.18
Nodes (8): CardDef, Amount, Amount2, Effect, Id, Target, Text, Street

### Community 107 - ".Finished"
Cohesion: 0.33
Nodes (4): Anim, Task, Node, Tween

### Community 108 - "BoardEmpire"
Cohesion: 0.33
Nodes (5): BoardEmpire, Build, test, run, Game modes, How it fits together, Known limits

### Community 110 - "JailExit"
Cohesion: 0.50
Nodes (4): JailExit, Card, Doubles, Fine

### Community 111 - "BotDriver"
Cohesion: 0.29
Nodes (3): Dictionary, BotDriver, BotLevel

### Community 112 - "Table"
Cohesion: 0.25
Nodes (8): At, Dictionary, List, Stopwatch, Table, Autoplay, Clients, Version

### Community 115 - "EffectScope"
Cohesion: 0.33
Nodes (6): EffectScope, All, District, NearTransit, Side, TileType

### Community 117 - "AGENTS.md — BoardEmpire"
Cohesion: 0.40
Nodes (4): AGENTS.md — BoardEmpire, Invariants — do not break these, Where things live, Workflow

### Community 119 - "DistrictDef"
Cohesion: 0.40
Nodes (4): DistrictDef, Glyph, Id, Name

### Community 121 - "Autopilot"
Cohesion: 0.40
Nodes (5): List, Autopilot, Enabled, Failures, Sent

## Knowledge Gaps
- **760 isolated node(s):** `net8.0`, `net9.0`, `Godot.NET.Sdk/4.7.2`, `I`, `Settings` (+755 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **14 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `GameState` connect `GameState` to `GameEvent`, `TestGame`, `GameCommands.cs`, `GameEngine`, `RoomClient`, `IRuleModule`, `LobbyScreen`, `Harness`, `Board2DView`, `PlayerState`, `BoardScreen`, `RoomHost`, `ActiveEffect`, `CityAmbience`, `.Button`, `TradeOffer`, `BoardPresenter`, `GameRules`, `Game.Core.Rules`, `LocalProfile`, `TradeSide`, `Messages.cs`, `.Create`, `ProjectState`, `AuctionState`, `PropertyState`, `Board3DView`, `GameCommand`, `BoardDefinition`, `Contract`, `DebtState`, `UserStats`, `IGameView`, `.OnEventStarting`, `RoomSave`, `GameEndReason`, `ObjectiveKind`, `BankState`, `ReplayFile`, `Pcg32`, `ReplayPlayer`, `BotDriver`, `AnimContext`?**
  _High betweenness centrality (0.324) - this node is a cross-community bridge._
- **Why does `RoomClient` connect `RoomClient` to `.Private_room_match_is_played_persisted_and_replayable`, `GameEvent`, `Game.Protocol`, `GameState`, `Autopilot`, `.Server_on_postgres_and_redis_survives_a_restart_mid_match`, `Messages.cs`, `LobbyScreen`, `ChaosTransport`, `GameSession`, `Harness`, `Table`, `LobbyInfo`, `BoardScreen`, `GameCommand`, `BoardDefinition`, `ClientStatus`?**
  _High betweenness centrality (0.107) - this node is a cross-community bridge._
- **Why does `RoomHost` connect `RoomHost` to `GameEvent`, `GameState`, `GameEngine`, `.BuildTiles`, `GameSession`, `Harness`, `.OnHello`, `GameRules`, `Game.Core.Rules`, `RoomManager`, `.Receive`, `MatchRecord`, `BoardDefinition`, `RoomHostOptions`, `HostSession`, `SeatInfo`, `MatchConfig`, `RoomSave`, `LobbyInfo`, `BotDriver`?**
  _High betweenness centrality (0.101) - this node is a cross-community bridge._
- **Are the 51 inferred relationships involving `TestGame` (e.g. with `.Advance_card_moves_and_pays_salary()` and `.Bankrupt_player_cannot_act()`) actually correct?**
  _`TestGame` has 51 INFERRED edges - model-reasoned connections that need verification._
- **What connects `net8.0`, `net9.0`, `Godot.NET.Sdk/4.7.2` to the rest of the system?**
  _760 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `GameEvent` be split into smaller, more focused modules?**
  _Cohesion score 0.03734567901234568 - nodes in this community are weakly interconnected._
- **Should `GameState` be split into smaller, more focused modules?**
  _Cohesion score 0.041353383458646614 - nodes in this community are weakly interconnected._