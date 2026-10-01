# Graph Report - monopoly  (2026-10-01)

## Corpus Check
- 103 files · ~99,582 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2797 nodes · 7358 edges · 135 communities (106 shown, 29 thin omitted)
- Extraction: 84% EXTRACTED · 16% INFERRED · 0% AMBIGUOUS · INFERRED: 1168 edges (avg confidence: 0.83)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `8a5a9b19`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- ReplayFile
- GameEvent
- GameState
- TestGame
- GameCommand
- Synth
- GameEngine
- RoomClient
- Calc
- Task
- LobbyScreen
- Board3DView
- ChaosTransport
- GameSession
- OnlineApi
- PostgresStore
- Harness
- Board2DView
- PlayerState
- BoardScreen
- RoomHost
- .Button
- MemoryPresence
- ActiveEffect
- CityAmbience
- Server.Tests
- .OpenTradeComposer
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
- .OnHello
- UserRecord
- .Play
- NearbyPlugin
- Screen
- SetupScreen
- MatchRecord
- .RunToEnd
- BoardDefinition
- Node3D
- .Fair_seed_combines_all_contributions
- GameState.cs
- PlayerStats
- RoomHostOptions
- GameRules.cs
- HostSession
- CardEffect
- .OpenMenu
- DebtState
- UserStats
- SeatInfo
- ServerTests.cs
- WebSocketRoomTransport
- MatchConfig
- IGameView
- .OnEventStarting
- Dice3D
- CommandResult
- RoomSave
- Loc
- TileType
- GameEndReason
- Settings.cs
- LobbyInfo
- Fact
- ObjectiveKind
- Reject
- ServerOptions
- Ui
- SessionKind
- CoreJson
- SetupMode
- BankState
- ClientStatus
- .Normalize
- Task
- HANDOFF — BoardEmpire
- .DisposeAsync
- .ShouldBecomeHost
- Pcg32
- TileDef
- PlayerSetup
- build-android.sh
- CardDef
- .Finished
- BoardEmpire
- .Enqueue
- JailExit
- .IsValid
- TurnInfo.cs
- Results.cs
- set-version.sh
- List
- .Every_preset_resolves
- .DisposeAsync
- .PresetBoardMatrix
- App
- Rect2
- StyleBoxFlat
- CpuParticles3D
- DirectionalLight3D
- Mesh
- MeshInstance3D
- Node3D
- StandardMaterial3D
- Vector3
- IReadOnlyCollection
- ArgumentException
- Fact
- InlineData
- MemberData
- Theory

## God Nodes (most connected - your core abstractions)
1. `GameState` - 210 edges
2. `GameEvent` - 95 edges
3. `TestGame` - 93 edges
4. `GameEngine` - 91 edges
5. `RoomHost` - 88 edges
6. `RoomClient` - 84 edges
7. `BoardScreen` - 74 edges
8. `GameRules` - 60 edges
9. `Board2DView` - 58 edges
10. `Board3DView` - 58 edges

## Surprising Connections (you probably didn't know these)
- `LocalProfile` --references--> `Description`  [EXTRACTED]
  client/src/Core/LocalProfile.cs → src/Game.Core/State/GameState.cs
- `TestServer` --references--> `Program`  [EXTRACTED]
  tests/Server.Tests/ServerTests.cs → src/Game.Server/Program.cs
- `Table` --references--> `BotBrain`  [EXTRACTED]
  tests/Server.Tests/ServerTests.cs → src/Game.Core/AI/BotBrain.cs
- `Table` --references--> `RoomClient`  [EXTRACTED]
  tests/Server.Tests/ServerTests.cs → src/Game.Net/RoomClient.cs
- `TestServer` --references--> `IStore`  [EXTRACTED]
  tests/Server.Tests/ServerTests.cs → src/Game.Server/Data/Store.cs

## Import Cycles
- None detected.

## Communities (135 total, 29 thin omitted)

### Community 0 - "ReplayFile"
Cohesion: 0.05
Nodes (44): IWebHostBuilder, Session, List, Award, MatchStory, ReplayFile, Board, Events (+36 more)

### Community 1 - "GameEvent"
Cohesion: 0.04
Nodes (40): Ability, IReadOnlyCollection, TileType, AbilityAssigned, AbilityUsed, AuctionPassed, AuctionStarted, BidPlaced (+32 more)

### Community 2 - "GameState"
Cohesion: 0.04
Nodes (44): Func, IEnumerable, List, TradePlanner, AuctionCompleted, DebtPaid, OptionExercised, PropertyPurchased (+36 more)

### Community 3 - "TestGame"
Cohesion: 0.14
Nodes (11): RentPaid, BuildHouseCommand, RollDiceCommand, ClassicRulesTests, Fact, TestGame, Dice, Engine (+3 more)

### Community 4 - "GameCommand"
Cohesion: 0.08
Nodes (27): Dictionary, BotBrain, Level, Rollouts, Smart, BuyPropertyCommand, CancelTradeCommand, CounterTradeCommand (+19 more)

### Community 5 - "Synth"
Cohesion: 0.10
Nodes (24): AudioStream, AudioStreamPlayer, AudioStreamWav, AudioDirector, Sfx, Bankrupt, Build, Card (+16 more)

### Community 6 - "GameEngine"
Cohesion: 0.09
Nodes (13): IReadOnlyList, List, TileType, Board, Current, Modules, Rules, State (+5 more)

### Community 7 - "RoomClient"
Cohesion: 0.06
Nodes (32): PendingCommand, SortedDictionary, BotLevel, ConcurrentQueue, Dictionary, HashSet, List, Queue (+24 more)

### Community 8 - "Calc"
Cohesion: 0.19
Nodes (7): Func, Calc, DevelopmentType, Commercial, Industrial, Luxury, Residential

### Community 9 - "Task"
Cohesion: 0.08
Nodes (13): MatchData, ConcurrentDictionary, HashSet, IEnumerable, IReadOnlyList, List, Task, ActiveMatch (+5 more)

### Community 10 - "LobbyScreen"
Cohesion: 0.09
Nodes (9): NearbyEndpoint, LanBrowserScreen, LobbyScreen, NearbyScreen, Dictionary, HBoxContainer, Label, Task (+1 more)

### Community 11 - "Board3DView"
Cohesion: 0.13
Nodes (21): CameraRig, CityAmbience, Board3DView, Node, BoardDefinition, BoardLayout, Control, Dictionary (+13 more)

### Community 12 - "ChaosTransport"
Cohesion: 0.07
Nodes (23): LoopbackClient, Outgoing, Packet, Task, At, CancellationToken, ConcurrentDictionary, List (+15 more)

### Community 13 - "GameSession"
Cohesion: 0.10
Nodes (16): GameSession, Api, Client, HasSave, Host, IsAuthority, Kind, LanPort (+8 more)

### Community 14 - "OnlineApi"
Cohesion: 0.13
Nodes (15): ApiResult, OnlineApi, BaseUrl, Token, HttpClient, JsonElement, Task, OnlineScreen (+7 more)

### Community 15 - "PostgresStore"
Cohesion: 0.13
Nodes (13): NpgsqlDataReader, NpgsqlDataSource, NpgsqlParameter, SkippableFact, IEnumerable, IReadOnlyList, List, Task (+5 more)

### Community 16 - "Harness"
Cohesion: 0.15
Nodes (23): Action, Func, List, Autopilot, Enabled, Failures, Sent, Harness (+15 more)

### Community 17 - "Board2DView"
Cohesion: 0.07
Nodes (31): BoardLayout, CornerSize, Count, TileWidth, List, Rect2, Vector2, Board2DView (+23 more)

### Community 18 - "PlayerState"
Cohesion: 0.08
Nodes (20): Ability, ObjectiveProgress, Completed, Id, PlayerState, AbilityArmed, AbilityUsed, Bankrupt (+12 more)

### Community 19 - "BoardScreen"
Cohesion: 0.10
Nodes (10): BoxContainer, ViewMode, Currency, D, IsReplay, Control, Label, VBoxContainer (+2 more)

### Community 20 - "RoomHost"
Cohesion: 0.06
Nodes (28): IReadOnlyList, Deadline, Key, SeatState, Random, Action, Dictionary, HashSet (+20 more)

### Community 21 - ".Button"
Cohesion: 0.18
Nodes (8): HBoxContainer, LineEdit, VBoxContainer, Control, HBoxContainer, PanelContainer, ScrollContainer, Color

### Community 22 - "MemoryPresence"
Cohesion: 0.16
Nodes (5): ConcurrentDictionary, List, Invite, IPresence, MemoryPresence

### Community 23 - "ActiveEffect"
Cohesion: 0.07
Nodes (30): Event, CityEventOccurred, EconomyChanged, ProjectCompleted, MarketRuleModule, Id, ActiveEffect, BuildCostPercent (+22 more)

### Community 24 - "CityAmbience"
Cohesion: 0.11
Nodes (25): Quality, High, Low, Medium, Ultra, CityAmbience, Night, Progress (+17 more)

### Community 25 - "Server.Tests"
Cohesion: 0.11
Nodes (30): BoardEmpire, net8.0, net9.0, Microsoft.AspNetCore.Mvc.Testing (8.0.11), Npgsql (8.0.5), StackExchange.Redis (2.8.16), Xunit.SkippableFact (1.4.13), Godot.NET.Sdk/4.7.2 (+22 more)

### Community 26 - ".OpenTradeComposer"
Cohesion: 0.18
Nodes (7): Tokens, Color, Label, PanelContainer, Button, Contract, SpinBox

### Community 27 - "TradeOffer"
Cohesion: 0.16
Nodes (11): Contract, Valuation, TileType, ObjectiveCatalog, ObjectiveDef, TradeOffer, From, Give (+3 more)

### Community 28 - "Settings"
Cohesion: 0.07
Nodes (27): Settings, AccountIsGuest, AmbientVolume, AnimationSpeed, AuthToken, CameraShake, ColorBlindPatterns, Haptics (+19 more)

### Community 29 - "NearbyBridge"
Cohesion: 0.09
Nodes (9): NearbyBridge, Available, NearbyHostTransport, CancellationToken, ConcurrentDictionary, Dictionary, Task, ValueTask (+1 more)

### Community 30 - "LanHostTransport"
Cohesion: 0.05
Nodes (48): Ad, Connection, IPAddress, NetworkStream, Seen, CancellationToken, CancellationTokenSource, ConcurrentDictionary (+40 more)

### Community 31 - "BoardPresenter"
Cohesion: 0.17
Nodes (9): BoardPresenter, Backlog, Display, IsIdle, Paused, SpeedOverride, View, Queue (+1 more)

### Community 32 - "GameRules"
Cohesion: 0.06
Nodes (33): GameRules, AbilitiesEnabled, AdvancedDevelopment, AuctionMinIncrement, AuctionMode, AuctionsEnabled, CollectRentInJail, ContractsEnabled (+25 more)

### Community 33 - "CameraRig"
Cohesion: 0.09
Nodes (19): Camera3D, InputEvent, CameraRig, AllowShake, Aspect, BoardSize, Camera, ReduceMotion (+11 more)

### Community 34 - "Game.Protocol"
Cohesion: 0.13
Nodes (14): Game.Server.Rooms, Game.Net, Net.Tests, Game.Protocol, Game.Net.Transport, Game.Server.Auth, CreateRoomRequest, FriendRequest (+6 more)

### Community 35 - "Game.Core.Rules"
Cohesion: 0.23
Nodes (9): EventText, Game.Core.Events, Game.Core.Rules.Modules, Game.Core.State, Core.Tests, BoardEmpire.Presentation, Game.Core.Board, Game.Core.Commands (+1 more)

### Community 36 - "BoardLibrary"
Cohesion: 0.12
Nodes (18): Glyph, House, Id, IReadOnlyCollection, Lazy, Name, Price, Rents (+10 more)

### Community 37 - "App"
Cohesion: 0.10
Nodes (19): AnimationSpeed, App, App, Audio, Current, I, Profile, Session (+11 more)

### Community 38 - "LocalProfile"
Cohesion: 0.07
Nodes (27): LocalProfile, Achievements, AuctionsWon, AveragePosition, Bankruptcies, BuildingsBuilt, GamesPlayed, LargestPayment (+19 more)

### Community 39 - "TradeSide"
Cohesion: 0.08
Nodes (22): TransferMoneyCommand, UseAbilityCommand, ContractTerm, Amount, Count, Kind, Percent, Tile (+14 more)

### Community 40 - "Messages.cs"
Cohesion: 0.13
Nodes (28): BotLevel, AddBot, AddLocalPlayer, ChatMessage, Heartbeat, Hello, KickPlayer, LeaveRoom (+20 more)

### Community 41 - ".Ok"
Cohesion: 0.20
Nodes (8): AcceptTradeCommand, ContributeToProjectCommand, CreateTradeCommand, DeclareBankruptcyCommand, DeclinePropertyCommand, DebtAndTradeTests, Fact, TestGameExtensions

### Community 42 - ".Create"
Cohesion: 0.28
Nodes (7): BotLevel, InlineData, MemberData, SimulationTests, GameState, List, Theory

### Community 43 - "BoardEmpire.Core"
Cohesion: 0.17
Nodes (7): BoardEmpire.UI, BoardEmpire, BoardEmpire.Core, BoardEmpire.View3D, BoardEmpire.View2D, Game.Core.Replay, BoardEmpire.Net

### Community 44 - "ProjectState"
Cohesion: 0.06
Nodes (33): ProjectContribution, ProjectProposed, GameRules, List, Name, AbilityRuleModule, Id, ClassicRules (+25 more)

### Community 45 - "NetworkEnvelope"
Cohesion: 0.13
Nodes (16): Exception, ReadOnlySpan, Dictionary, JsonElement, JsonSerializerOptions, NetworkEnvelope, MatchId, MessageId (+8 more)

### Community 46 - "AuctionState"
Cohesion: 0.09
Nodes (16): SealedBidsRevealed, Func, IReadOnlyCollection, List, AuctionState, HighBid, HighBidder, Mode (+8 more)

### Community 47 - "PropertyState"
Cohesion: 0.08
Nodes (23): MoneyReason, Card, Contract, Gift, Jackpot, JailFine, ProjectBonus, Tax (+15 more)

### Community 48 - "RoomManager"
Cohesion: 0.08
Nodes (25): BackgroundService, Channel, ILogger, CancellationToken, ConcurrentDictionary, Func, IEnumerable, Random (+17 more)

### Community 49 - ".OnHello"
Cohesion: 0.21
Nodes (4): Peer, SeatState, CommandRejected, PeerStatus

### Community 50 - "UserRecord"
Cohesion: 0.12
Nodes (11): UserRecord, Avatar, CreatedAt, DisplayName, Id, IsGuest, PasswordHash, Stats (+3 more)

### Community 51 - ".Play"
Cohesion: 0.21
Nodes (5): AnimContext, Color, GameEvent, Task, Vector3

### Community 52 - "NearbyPlugin"
Cohesion: 0.13
Nodes (5): NearbyPlugin, ByteArray, ConnectionsClient, GodotPlugin, SignalInfo

### Community 53 - "Screen"
Cohesion: 0.10
Nodes (14): MainMenuScreen, PlayMenuScreen, ProfileScreen, ReplayListScreen, ResultsScreen, RulesScreen, SettingsScreen, Action (+6 more)

### Community 54 - "SetupScreen"
Cohesion: 0.15
Nodes (12): LocalSeat, BotLevel, IsBot, Name, BotLevel, SetupScreen, Label, List (+4 more)

### Community 55 - "MatchRecord"
Cohesion: 0.10
Nodes (19): DateTime, MatchPlayerRecord, IsBot, Name, NetWorth, Rank, RatingChange, Seat (+11 more)

### Community 56 - ".RunToEnd"
Cohesion: 0.20
Nodes (6): Action, Dictionary, IReadOnlyList, BotDriver, MonteCarlo, BotLevel

### Community 57 - "BoardDefinition"
Cohesion: 0.11
Nodes (16): Dictionary, List, BoardDefinition, BoardId, CivicCards, Count, Currency, Districts (+8 more)

### Community 58 - "Node3D"
Cohesion: 0.23
Nodes (10): TileVisual, GameState, TileDef, DevelopmentType, Mesh, MeshInstance3D, Node3D, PropertyState (+2 more)

### Community 60 - "GameState.cs"
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
Cohesion: 0.10
Nodes (16): Data, IAsyncDisposable, Kind, CancellationToken, ConcurrentQueue, List, Task, ValueTask (+8 more)

### Community 65 - "CardEffect"
Cohesion: 0.17
Nodes (12): CardEffect, Collect, CollectFromEach, GoToJail, JailCard, MoveRelative, MoveTo, MoveToNearestTransit (+4 more)

### Community 66 - ".OpenMenu"
Cohesion: 0.29
Nodes (4): Action, Control, Modal, Action

### Community 67 - "DebtState"
Cohesion: 0.12
Nodes (15): DebtIncurred, PhaseChanged, DebtState, Amount, Creditor, Debtor, Reason, Tile (+7 more)

### Community 68 - "UserStats"
Cohesion: 0.09
Nodes (21): Rank, Rating, UserStats, AuctionsWon, Bankruptcies, BuildingsBuilt, GamesPlayed, LargestPayment (+13 more)

### Community 69 - "SeatInfo"
Cohesion: 0.12
Nodes (14): Seconds, Ability, SeatInfo, Ability, BotLevel, Connected, IsBot, Name (+6 more)

### Community 70 - "ServerTests.cs"
Cohesion: 0.33
Nodes (6): Game.Server.Data, Game.Core.Serialization, Game.Core.Rng, Server.Tests, Game.Core.AI, Game.Core.Engine

### Community 71 - "WebSocketRoomTransport"
Cohesion: 0.19
Nodes (10): CancellationToken, CancellationTokenSource, ConcurrentDictionary, SemaphoreSlim, Task, ValueTask, WebSocket, Connection (+2 more)

### Community 72 - "MatchConfig"
Cohesion: 0.18
Nodes (10): List, MatchConfig, BoardId, MatchId, Players, Rules, Seed, IEnumerable (+2 more)

### Community 73 - "IGameView"
Cohesion: 0.17
Nodes (5): AnimContext, IGameView, Node, Control, Task

### Community 75 - "Dice3D"
Cohesion: 0.33
Nodes (4): Dice3D, Random, Task, Vector3

### Community 76 - "CommandResult"
Cohesion: 0.36
Nodes (5): IReadOnlyList, CommandResult, Error, Events, Ok

### Community 77 - "RoomSave"
Cohesion: 0.17
Nodes (8): RoomSave, Board, Config, Events, Initial, Lobby, State, IReadOnlyList

### Community 78 - "Loc"
Cohesion: 0.18
Nodes (8): Loc, Language, Dictionary, SavedPreset, Name, Rules, Code, GameRules

### Community 79 - "TileType"
Cohesion: 0.17
Nodes (11): IEnumerable, TileType, Civic, Fortune, GoToJail, Jail, Plaza, Start (+3 more)

### Community 80 - "GameEndReason"
Cohesion: 0.14
Nodes (11): GameEnded, List, GameEndReason, FirstBankruptcy, LastStanding, None, RoundLimit, Standing (+3 more)

### Community 81 - "Settings.cs"
Cohesion: 0.25
Nodes (7): AnimationSpeed, Fast, Minimal, Normal, ViewMode, City3D, Flat2D

### Community 82 - "LobbyInfo"
Cohesion: 0.18
Nodes (11): LobbyInfo, BoardId, HostPeerId, InMatch, Ranked, Revision, RoomCode, RoomName (+3 more)

### Community 83 - "Fact"
Cohesion: 0.18
Nodes (9): ArgumentException, BotDriver, CreateTradeCommand, Fact, GameCommand, InvalidOperationException, MatchConfig, InfrastructureTests (+1 more)

### Community 84 - "ObjectiveKind"
Cohesion: 0.18
Nodes (11): ObjectiveKind, BuildHouses, CollectSalary, CompleteTrades, EarnRent, HoldCash, OwnAnyDistrict, OwnOnEverySide (+3 more)

### Community 85 - "Reject"
Cohesion: 0.15
Nodes (10): Reject, RejectCode, BadToken, Kicked, MatchInProgress, RoomClosed, RoomFull, VersionTooNew (+2 more)

### Community 86 - "ServerOptions"
Cohesion: 0.18
Nodes (11): ServerOptions, AbandonedMatchSeconds, BotDelaySeconds, IdleRoomSeconds, InstanceId, MatchmakingFillSeconds, MatchmakingTargetSeats, MaxRooms (+3 more)

### Community 87 - "Ui"
Cohesion: 0.07
Nodes (20): CheckButton, ButtonKind, Danger, Ghost, Primary, Secondary, Ui, Action (+12 more)

### Community 88 - "SessionKind"
Cohesion: 0.25
Nodes (8): SessionKind, LanClient, LanHost, Local, NearbyClient, NearbyHost, None, Online

### Community 89 - "CoreJson"
Cohesion: 0.25
Nodes (4): JsonTypeInfo, JsonSerializerOptions, CoreJson, StateHasher

### Community 90 - "SetupMode"
Cohesion: 0.33
Nodes (6): SetupMode, HostLan, HostNearby, OnlinePrivate, PassAndPlay, Solo

### Community 91 - "BankState"
Cohesion: 0.40
Nodes (4): BankState, HotelsLeft, HousesLeft, Jackpot

### Community 92 - "ClientStatus"
Cohesion: 0.40
Nodes (5): ClientStatus, Connecting, Disconnected, InMatch, Lobby

### Community 94 - "Task"
Cohesion: 0.19
Nodes (9): ConnectionMultiplexer, IDatabase, IDisposable, Dictionary, IEnumerable, Task, TimeSpan, RedisPresence (+1 more)

### Community 95 - "HANDOFF — BoardEmpire"
Cohesion: 0.18
Nodes (10): Android signing (one key for every build), Backend deployment, Deviations from the original plan, Environment notes (this machine), HANDOFF — BoardEmpire, Known issues / ideas, Not implemented, Not verified (+2 more)

### Community 101 - ".ShouldBecomeHost"
Cohesion: 0.33
Nodes (3): IEnumerable, IReadOnlyCollection, HostMigration

### Community 102 - "Pcg32"
Cohesion: 0.19
Nodes (6): Queue, Pcg32, RngState, Inc, S, ScriptedRandom

### Community 103 - "TileDef"
Cohesion: 0.22
Nodes (9): TileDef, District, HouseCost, IsOwnable, Name, Price, Rents, TaxAmount (+1 more)

### Community 104 - "PlayerSetup"
Cohesion: 0.33
Nodes (6): PlayerSetup, Ability, IsBot, Name, Team, Token

### Community 105 - "build-android.sh"
Cohesion: 0.25
Nodes (7): GODOT_ANDROID_KEYSTORE_DEBUG_PASSWORD, GODOT_ANDROID_KEYSTORE_DEBUG_PATH, GODOT_ANDROID_KEYSTORE_DEBUG_USER, GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD, GODOT_ANDROID_KEYSTORE_RELEASE_PATH, GODOT_ANDROID_KEYSTORE_RELEASE_USER, build-android.sh script

### Community 106 - "CardDef"
Cohesion: 0.17
Nodes (11): CardDef, Amount, Amount2, Effect, Id, Target, Text, DistrictDef (+3 more)

### Community 107 - ".Finished"
Cohesion: 0.50
Nodes (3): Anim, Node, Tween

### Community 108 - "BoardEmpire"
Cohesion: 0.33
Nodes (5): BoardEmpire, Build, test, run, Game modes, How it fits together, Known limits

### Community 110 - "JailExit"
Cohesion: 0.21
Nodes (8): JailExit, Card, Doubles, Fine, MoveKind, Backward, Jump, Walk

## Knowledge Gaps
- **742 isolated node(s):** `Autoplay`, `Node`, `BoardPixels`, `Node`, `BoardIds` (+737 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **29 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `GameState` connect `GameState` to `ReplayFile`, `GameEvent`, `TestGame`, `GameCommand`, `GameEngine`, `RoomClient`, `Calc`, `LobbyScreen`, `Harness`, `PlayerState`, `BoardScreen`, `RoomHost`, `ActiveEffect`, `CityAmbience`, `.OpenTradeComposer`, `TradeOffer`, `BoardPresenter`, `GameRules`, `LocalProfile`, `TradeSide`, `Messages.cs`, `.Create`, `ProjectState`, `AuctionState`, `PropertyState`, `Screen`, `.RunToEnd`, `BoardDefinition`, `GameState.cs`, `DebtState`, `UserStats`, `MatchConfig`, `IGameView`, `.OnEventStarting`, `RoomSave`, `GameEndReason`, `Fact`, `CoreJson`, `BankState`, `Pcg32`, `TurnInfo.cs`?**
  _High betweenness centrality (0.276) - this node is a cross-community bridge._
- **Why does `RoomClient` connect `RoomClient` to `HostSession`, `GameEvent`, `GameState`, `Game.Core.Rules`, `GameCommand`, `ReplayFile`, `Messages.cs`, `LobbyScreen`, `ChaosTransport`, `GameSession`, `Harness`, `LobbyInfo`, `BoardScreen`, `BoardDefinition`, `ClientStatus`?**
  _High betweenness centrality (0.150) - this node is a cross-community bridge._
- **Why does `GameSession` connect `GameSession` to `ReplayFile`, `HostSession`, `Game.Protocol`, `App`, `RoomClient`, `.Finished`, `ChaosTransport`, `OnlineApi`, `RoomHost`, `SessionKind`, `NearbyBridge`, `LanHostTransport`?**
  _High betweenness centrality (0.098) - this node is a cross-community bridge._
- **Are the 51 inferred relationships involving `TestGame` (e.g. with `.Advance_card_moves_and_pays_salary()` and `.Bankrupt_player_cannot_act()`) actually correct?**
  _`TestGame` has 51 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Autoplay`, `Node`, `BoardPixels` to the rest of the system?**
  _742 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `ReplayFile` be split into smaller, more focused modules?**
  _Cohesion score 0.05348101265822785 - nodes in this community are weakly interconnected._
- **Should `GameEvent` be split into smaller, more focused modules?**
  _Cohesion score 0.0379746835443038 - nodes in this community are weakly interconnected._