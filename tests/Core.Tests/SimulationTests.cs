using Game.Core.AI;
using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Engine;
using Game.Core.Events;
using Game.Core.Replay;
using Game.Core.Rng;
using Game.Core.Rules;
using Game.Core.Serialization;
using Game.Core.State;

namespace Core.Tests;

public class SimulationTests
{
    private static MatchConfig Config(string preset, string board, ulong seed, int players, BotLevel level = BotLevel.Medium, int maxRounds = 60)
    {
        var rules = RulePresets.Get(preset);
        if (rules.MaximumRounds == 0 || rules.MaximumRounds > maxRounds) rules.MaximumRounds = maxRounds;
        var cfg = new MatchConfig { MatchId = $"{preset}-{seed}", Seed = seed, BoardId = board, Rules = rules };
        for (int i = 0; i < players; i++)
            cfg.Players.Add(new PlayerSetup { Name = $"Bot{i}", Token = i, IsBot = true, BotLevel = level });
        return cfg;
    }

    private static BotDriver Driver(MatchConfig cfg, int rollouts = 2)
    {
        var d = new BotDriver(cfg.Seed);
        for (int i = 0; i < cfg.Players.Count; i++) d.SetBot(i, cfg.Players[i].BotLevel, rollouts);
        return d;
    }

    /// <summary>Structural invariants that must hold after every single command.</summary>
    internal static void AssertInvariants(GameState s)
    {
        foreach (var p in s.Players)
        {
            Assert.True(p.Money >= 0, $"{p.Name} has negative money {p.Money}");
            Assert.True(p.JailCards >= 0);
            if (p.Bankrupt) Assert.Empty(s.OwnedBy(p.Id));
        }
        int houses = 0, hotels = 0;
        foreach (var prop in s.Properties)
        {
            if (prop == null) continue;
            Assert.InRange(prop.Level, 0, 5);
            if (prop.Level == 5) hotels++;
            else houses += prop.Level;
            if (prop.Owner < 0)
            {
                Assert.Equal(0, prop.Level);
                Assert.False(prop.Mortgaged);
                Assert.Empty(prop.Shares);
            }
            else
            {
                Assert.False(s.Players[prop.Owner].Bankrupt);
                if (prop.Mortgaged) Assert.Equal(0, prop.Level);
                if (s.Rules.PropertySharesEnabled)
                {
                    Assert.Equal(100, prop.Shares.Sum(x => x.Percent));
                    Assert.All(prop.Shares, x => Assert.False(s.Players[x.Player].Bankrupt));
                }
            }
        }
        Assert.Equal(s.Rules.HouseSupply, s.Bank.HousesLeft + houses);
        Assert.Equal(s.Rules.HotelSupply, s.Bank.HotelsLeft + hotels);
        Assert.True(s.Bank.HousesLeft >= 0 && s.Bank.HotelsLeft >= 0);
        Assert.Equal(s.Phase == TurnPhase.Auction, s.Auction != null);
        if (!s.IsOver) Assert.Equal(s.Phase == TurnPhase.DebtResolution, s.Debts.Count > 0);
        Assert.All(s.Debts, d => Assert.False(s.Players[d.Debtor].Bankrupt));
        Assert.All(s.Contracts, c => Assert.False(s.Players[c.Grantor].Bankrupt || s.Players[c.Beneficiary].Bankrupt));
        if (!s.IsOver) Assert.False(s.Players[s.CurrentPlayer].Bankrupt && s.Phase is TurnPhase.PreRoll or TurnPhase.PostRoll);
    }

    public static IEnumerable<object[]> PresetBoardMatrix()
    {
        foreach (string preset in RulePresets.Ids)
            foreach (string board in new[] { "neo_city", "pocket_city" })
                yield return new object[] { preset, board };
    }

    [Theory]
    [MemberData(nameof(PresetBoardMatrix))]
    public void Bots_complete_matches_and_keep_invariants(string preset, string board)
    {
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var cfg = Config(preset, board, seed, 2 + (int)(seed % 5));
            var engine = GameEngine.Create(cfg);
            engine.Start();
            AssertInvariants(engine.State);
            BotDriver.RunToEnd(engine, Driver(cfg), 200_000, (_, _) => AssertInvariants(engine.State));
            Assert.True(engine.State.IsOver);
            Assert.NotEmpty(engine.State.Winners);
            Assert.Equal(cfg.Players.Count, engine.State.Standings.Count);
        }
    }

    [Theory]
    [InlineData(BotLevel.Easy)]
    [InlineData(BotLevel.Hard)]
    [InlineData(BotLevel.Expert)]
    public void Every_bot_level_plays_legal_games(BotLevel level)
    {
        for (ulong seed = 1; seed <= 3; seed++)
        {
            var cfg = Config("tycoon", "neo_city", seed, 4, level, level == BotLevel.Expert ? 25 : 60);
            var engine = GameEngine.Create(cfg);
            engine.Start();
            BotDriver.RunToEnd(engine, Driver(cfg), 200_000, (_, _) => AssertInvariants(engine.State));
            Assert.True(engine.State.IsOver);
        }
    }

    [Theory]
    [InlineData("classic")]
    [InlineData("tycoon")]
    [InlineData("market_mayhem")]
    [InlineData("classic_plus")]
    public void Same_seed_and_commands_give_identical_event_streams(string preset)
    {
        (string Events, string Hash) Run()
        {
            var cfg = Config(preset, "neo_city", 927361, 4);
            var engine = GameEngine.Create(cfg);
            var log = new List<GameEvent>(engine.Start());
            BotDriver.RunToEnd(engine, Driver(cfg), 200_000, (_, ev) => log.AddRange(ev));
            return (CoreJson.Serialize(log), StateHasher.FullHash(engine.State));
        }

        var first = Run();
        for (int i = 0; i < 3; i++)
        {
            var again = Run();
            Assert.Equal(first.Hash, again.Hash);
            Assert.Equal(first.Events, again.Events);
        }
    }

    [Fact]
    public void Recorded_commands_replay_to_the_same_state_on_a_fresh_engine()
    {
        var cfg = Config("tycoon", "neo_city", 77, 4);
        var engine = GameEngine.Create(cfg);
        engine.Start();
        var commands = new List<GameCommand>();
        BotDriver.RunToEnd(engine, Driver(cfg), 200_000, (c, _) => commands.Add(c));

        var replay = GameEngine.Create(Config("tycoon", "neo_city", 77, 4));
        replay.Start();
        foreach (var c in CoreJson.Deserialize<List<GameCommand>>(CoreJson.Serialize(commands)))
            Assert.True(replay.Execute(c).Ok);
        Assert.Equal(StateHasher.FullHash(engine.State), StateHasher.FullHash(replay.State));
    }

    [Theory]
    [InlineData("classic_plus", 3)]
    [InlineData("tycoon", 4)]
    [InlineData("market_mayhem", 5)]
    [InlineData("blitz", 6)]
    [InlineData("team_empire", 4)]
    public void Replicas_stay_in_sync_using_only_redacted_serialized_events(string preset, int players)
    {
        var cfg = Config(preset, "neo_city", 4242, players, maxRounds: 30);
        var engine = GameEngine.Create(cfg);

        // One replica per player plus a spectator, each built from its own redacted snapshot.
        var viewers = Enumerable.Range(-1, players + 1).ToList();
        var replicas = viewers.ToDictionary(v => v, v =>
        {
            var copy = CoreJson.Deserialize<GameState>(CoreJson.Serialize(engine.State.RedactedFor(v)));
            copy.Board = engine.Board;
            return copy;
        });

        void Replicate(IReadOnlyList<GameEvent> events)
        {
            foreach (int v in viewers)
            {
                string wire = CoreJson.Serialize(events.Select(e => e.RedactFor(v)).ToList());
                foreach (var e in CoreJson.Deserialize<List<GameEvent>>(wire))
                {
                    e.Apply(replicas[v]);
                    replicas[v].Version = e.Version;
                }
            }
        }

        Replicate(engine.Start());
        int n = 0;
        BotDriver.RunToEnd(engine, Driver(cfg), 200_000, (_, events) =>
        {
            Replicate(events);
            if (++n % 25 != 0 && !engine.State.IsOver) return;
            string expected = StateHasher.Hash(engine.State);
            foreach (int v in viewers) Assert.Equal(expected, StateHasher.Hash(replicas[v]));
        });
        Assert.True(engine.State.IsOver);
        foreach (int v in viewers) Assert.Equal(engine.State.Version, replicas[v].Version);
    }

    [Fact]
    public void Replica_never_learns_the_rng_or_other_players_secrets()
    {
        var cfg = Config("classic_plus", "neo_city", 5, 3);
        var engine = GameEngine.Create(cfg);
        engine.Start();
        var view = engine.State.RedactedFor(1);
        Assert.Equal(0UL, view.Rng.S);
        Assert.Equal(0UL, view.Rng.Inc);
        Assert.All(view.Players[0].Objectives, o => Assert.Equal(-1, o.Id));
        Assert.NotEqual(0UL, engine.State.Rng.Inc);
    }

    [Fact]
    public void State_survives_clone_and_json_roundtrip_mid_game()
    {
        var cfg = Config("tycoon", "neo_city", 9, 4);
        cfg.Rules.SecretObjectivesEnabled = true;
        cfg.Rules.MarketEventsEnabled = true;
        cfg.Rules.PublicProjectsEnabled = true;
        var engine = GameEngine.Create(cfg);
        engine.Start();
        var driver = Driver(cfg);
        for (int i = 0; i < 1500 && !engine.State.IsOver; i++)
        {
            var c = driver.Next(engine.State) ?? BotBrain.TimeoutAction(engine.State, engine.State.CurrentPlayer);
            if (c == null) break;
            engine.Execute(c);
        }

        string json = CoreJson.Serialize(engine.State);
        Assert.Equal(json, CoreJson.Serialize(engine.State.Clone()));
        var back = CoreJson.Deserialize<GameState>(json);
        Assert.Equal(json, CoreJson.Serialize(back));

        // A resumed engine continues exactly like the original.
        var resumed = GameEngine.Resume(back, engine.Board);
        var a = Driver(cfg);
        var b = Driver(cfg);
        for (int i = 0; i < 200 && !engine.State.IsOver; i++)
        {
            var ca = a.Next(engine.State);
            var cb = b.Next(resumed.State);
            if (ca == null || cb == null) break;
            engine.Execute(ca);
            resumed.Execute(cb with { CommandId = ca.CommandId });
        }
        Assert.Equal(StateHasher.FullHash(engine.State), StateHasher.FullHash(resumed.State));
    }

    [Fact]
    public void Replay_reconstructs_the_match_and_tells_its_story()
    {
        var cfg = Config("market_mayhem", "neo_city", 31, 4);
        var engine = GameEngine.Create(cfg);
        var initial = engine.State.RedactedFor(-1);
        var log = new List<GameEvent>(engine.Start());
        BotDriver.RunToEnd(engine, Driver(cfg), 200_000, (_, ev) => log.AddRange(ev));

        var file = ReplayFile.FromJson(new ReplayFile { Board = engine.Board, Initial = initial, Events = log }.ToJson());
        var player = new ReplayPlayer(file);
        player.Seek(player.Length);
        Assert.Equal(StateHasher.Hash(engine.State), StateHasher.Hash(player.State));

        Assert.True(player.JumpToTurn(5));
        Assert.Equal(5, player.State.TurnNumber);
        int at = player.Position;
        Assert.True(player.SkipTurn());
        Assert.Equal(6, player.State.TurnNumber);
        player.Seek(at);
        Assert.Equal(5, player.State.TurnNumber);

        var story = MatchStory.Build(file);
        Assert.NotEmpty(story);
        Assert.Contains("win", story[^1].Text);
        Assert.NotEmpty(MatchStory.Awards(engine.State));
    }

    [Fact]
    public void Timeout_action_always_makes_progress()
    {
        var cfg = Config("classic_plus", "neo_city", 12, 4, maxRounds: 15);
        var engine = GameEngine.Create(cfg);
        engine.Start();
        int guard = 0;
        while (!engine.State.IsOver && guard++ < 20_000)
        {
            GameCommand? c = null;
            foreach (int actor in TurnInfo.PendingActors(engine.State))
            {
                c = BotBrain.TimeoutAction(engine.State, actor);
                if (c != null) break;
            }
            Assert.NotNull(c);
            Assert.True(engine.Execute(c!).Ok);
        }
        Assert.True(engine.State.IsOver);
    }
}

public class InfrastructureTests
{
    [Fact]
    public void All_built_in_boards_are_valid()
    {
        Assert.Equal(7, BoardLibrary.BoardIds.Count);
        foreach (var b in BoardLibrary.All) b.Validate();
        Assert.Equal(40, BoardLibrary.Get("neo_city").Count);
        Assert.Equal(24, BoardLibrary.Get("pocket_city").Count);
    }

    [Fact]
    public void Boards_roundtrip_through_json_and_stay_playable()
    {
        var board = BoardLibrary.FromJson(BoardLibrary.ToJson(BoardLibrary.Get("pirate_isles")));
        Assert.Equal("pirate_isles", board.BoardId);
        var cfg = new MatchConfig { MatchId = "custom", Seed = 3, BoardId = board.BoardId, Rules = new GameRules { MaximumRounds = 20 } };
        for (int i = 0; i < 3; i++) cfg.Players.Add(new PlayerSetup { Name = $"B{i}", IsBot = true });
        var engine = GameEngine.Create(cfg, board);
        engine.Start();
        var driver = new BotDriver(3);
        for (int i = 0; i < 3; i++) driver.SetBot(i, BotLevel.Medium);
        BotDriver.RunToEnd(engine, driver);
        Assert.True(engine.State.IsOver);
    }

    [Fact]
    public void Invalid_boards_are_rejected()
    {
        var board = BoardLibrary.FromJson(BoardLibrary.ToJson(BoardLibrary.Get("pocket_city")));
        board.Tiles[1].Rents = new[] { 1, 2 };
        Assert.Throws<InvalidOperationException>(board.Validate);
        board = BoardLibrary.FromJson(BoardLibrary.ToJson(BoardLibrary.Get("pocket_city")));
        board.Tiles.RemoveAt(23);
        Assert.Throws<InvalidOperationException>(board.Validate);
        Assert.Throws<ArgumentException>(() => BoardLibrary.Get("nope"));
    }

    [Fact]
    public void Match_needs_two_to_eight_players()
    {
        var cfg = new MatchConfig { Players = { new PlayerSetup() } };
        Assert.Throws<ArgumentException>(() => GameEngine.Create(cfg));
        for (int i = 0; i < 7; i++) cfg.Players.Add(new PlayerSetup());
        GameEngine.Create(cfg);
        cfg.Players.Add(new PlayerSetup());
        Assert.Throws<ArgumentException>(() => GameEngine.Create(cfg));
    }

    [Fact]
    public void Rng_is_reproducible_and_unbiased_enough()
    {
        var a = new Pcg32(123);
        var b = new Pcg32(123);
        var counts = new int[6];
        for (int i = 0; i < 60_000; i++)
        {
            int x = a.Next(6);
            Assert.Equal(x, b.Next(6));
            counts[x]++;
        }
        Assert.All(counts, c => Assert.InRange(c, 9_500, 10_500));
        Assert.NotEqual(new Pcg32(1).Next(1_000_000), new Pcg32(2).Next(1_000_000));
    }

    [Fact]
    public void Fair_seed_combines_all_contributions()
    {
        ulong a = FairSeed.Combine(new[] { "x", "y", "z" }, "m1");
        Assert.Equal(a, FairSeed.Combine(new[] { "x", "y", "z" }, "m1"));
        Assert.NotEqual(a, FairSeed.Combine(new[] { "x", "y", "w" }, "m1"));
        Assert.NotEqual(a, FairSeed.Combine(new[] { "x", "y", "z" }, "m2"));
        Assert.True(FairSeed.Verify("secret", FairSeed.Commit("secret")));
        Assert.False(FairSeed.Verify("other", FairSeed.Commit("secret")));
    }

    [Fact]
    public void Commands_and_events_serialize_polymorphically()
    {
        GameCommand command = new CreateTradeCommand(1, 2, new TradeSide { Money = 5, Tiles = { 3 } }, new TradeSide()) { MatchId = "m", ExpectedVersion = 9 };
        var back = CoreJson.Deserialize<GameCommand>(CoreJson.Serialize(command));
        var trade = Assert.IsType<CreateTradeCommand>(back);
        Assert.Equal(command.CommandId, back.CommandId);
        Assert.Equal(9, back.ExpectedVersion);
        Assert.Equal(3, trade.Give.Tiles[0]);

        GameEvent e = new DiceRolled(1, 3, 4, 0) { Version = 12 };
        var eventBack = CoreJson.Deserialize<GameEvent>(CoreJson.Serialize(e));
        Assert.Equal(e, eventBack);
        Assert.StartsWith("{\"$t\":\"DiceRolled\"", CoreJson.Serialize(e));
    }

    [Fact]
    public void Bots_evaluate_district_completing_trades_sensibly()
    {
        var g = new TestGame(players: 3);
        g.Give(0, 37);
        g.Give(1, 39, 6);
        g.Give(2, 8);
        var generous = new TradeOffer { From = 0, To = 1, Give = new TradeSide { Money = 900 }, Receive = new TradeSide { Tiles = { 39 } } };
        var stingy = new TradeOffer { From = 0, To = 1, Give = new TradeSide { Money = 100 }, Receive = new TradeSide { Tiles = { 39 } } };
        Assert.True(Valuation.TradeDelta(g.S, generous, 1, 50) > Valuation.TradeDelta(g.S, stingy, 1, 50));
        Assert.True(Valuation.TradeDelta(g.S, stingy, 1, 50) < 0);
        Assert.True(Valuation.TradeDelta(g.S, stingy, 0, 50) > 0);
        Assert.True(Valuation.TileValue(g.S, 39, 0) > Valuation.TileValue(g.S, 39, 2));
    }
}
