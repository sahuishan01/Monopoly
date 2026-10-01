using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Engine;
using Game.Core.Events;
using Game.Core.Rng;
using Game.Core.Rules;
using Game.Core.State;

namespace Core.Tests;

/// <summary>Engine wrapper with scripted dice and a few arrangement shortcuts.</summary>
public sealed class TestGame
{
    public GameEngine Engine { get; }
    public ScriptedRandom Dice { get; }
    public List<GameEvent> Log { get; } = new();
    public GameState S => Engine.State;

    public TestGame(int players = 4, Action<GameRules>? configure = null, string board = "neo_city", ulong seed = 42,
        Action<MatchConfig>? setup = null)
    {
        var rules = new GameRules();
        configure?.Invoke(rules);
        var cfg = new MatchConfig { MatchId = "test", Seed = seed, BoardId = board, Rules = rules };
        for (int i = 0; i < players; i++) cfg.Players.Add(new PlayerSetup { Name = $"P{i}", Token = i });
        setup?.Invoke(cfg);
        Engine = GameEngine.Create(cfg);
        Dice = new ScriptedRandom(Array.Empty<int>(), new Pcg32(seed));
        Engine.Random = Dice;
        Log.AddRange(Engine.Start());
    }

    public CommandResult Do(GameCommand c)
    {
        var r = Engine.Execute(c);
        Log.AddRange(r.Events);
        return r;
    }

    public CommandResult Ok(GameCommand c)
    {
        var r = Do(c);
        Assert.True(r.Ok, r.Error);
        return r;
    }

    public string Fail(GameCommand c)
    {
        int version = S.Version;
        var r = Do(c);
        Assert.False(r.Ok);
        Assert.Equal(version, S.Version);
        return r.Error!;
    }

    public CommandResult Roll(int d1, int d2)
    {
        Dice.EnqueueDice(d1, d2);
        return Ok(new RollDiceCommand(S.CurrentPlayer));
    }

    public void Give(int player, params int[] tiles)
    {
        foreach (int t in tiles)
        {
            var p = S.Properties[t]!;
            p.Owner = player;
            p.Shares.Clear();
            if (S.Rules.PropertySharesEnabled) p.Shares.Add(new Share { Player = player, Percent = 100 });
        }
    }

    public void EndTurn() => Ok(new EndTurnCommand(S.CurrentPlayer));

    public PlayerState P(int id) => S.Players[id];

    public PropertyState Prop(int tile) => S.Properties[tile]!;

    public T Last<T>() where T : GameEvent => Log.OfType<T>().Last();
}
