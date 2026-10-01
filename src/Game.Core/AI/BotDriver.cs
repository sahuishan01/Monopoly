using Game.Core.Commands;
using Game.Core.Engine;
using Game.Core.Events;
using Game.Core.Rng;
using Game.Core.Rules;
using Game.Core.State;

namespace Game.Core.AI;

/// <summary>Runs the bot-controlled seats of a match by feeding their commands to the engine.</summary>
public sealed class BotDriver
{
    private readonly Dictionary<int, BotBrain> _brains = new();
    private readonly ulong _seed;

    public BotDriver(ulong seed) => _seed = seed;

    public bool Controls(int player) => _brains.ContainsKey(player);

    public void SetBot(int player, BotLevel level, int rollouts = 6) =>
        _brains[player] = new BotBrain(level, new Pcg32(_seed + (ulong)player * 7919UL, 17)) { Rollouts = rollouts };

    public void Remove(int player) => _brains.Remove(player);

    /// <summary>Next command from any bot that has to act, or null when the match waits on a human.</summary>
    public GameCommand? Next(GameState s)
    {
        foreach (int actor in TurnInfo.PendingActors(s))
        {
            if (!_brains.TryGetValue(actor, out var brain)) continue;
            var command = brain.Decide(s, actor);
            if (command != null) return command;
        }
        // Team rescue: a teammate bot may bail out a debtor even though it is not a pending actor.
        if (s.Rules.TeamsEnabled && s.Phase == TurnPhase.DebtResolution)
        {
            foreach (var (id, brain) in _brains.OrderBy(kv => kv.Key))
            {
                var command = brain.Decide(s, id);
                if (command is TransferMoneyCommand) return command;
            }
        }
        return null;
    }

    /// <summary>
    /// Plays an all-bot match to completion. Returns the number of commands executed.
    /// </summary>
    public static int RunToEnd(GameEngine engine, BotDriver driver, int maxCommands = 200_000,
        Action<GameCommand, IReadOnlyList<GameEvent>>? onCommand = null)
    {
        int executed = 0;
        int rejected = 0;
        while (!engine.State.IsOver && executed < maxCommands)
        {
            var command = driver.Next(engine.State) ?? Unstick(engine.State);
            if (command == null) throw new InvalidOperationException($"Match stalled in phase {engine.State.Phase}");
            var result = engine.Execute(command);
            if (!result.Ok)
            {
                if (++rejected > 50)
                    throw new InvalidOperationException($"Bot keeps sending illegal {command.GetType().Name}: {result.Error}");
                var fallback = BotBrain.TimeoutAction(engine.State, command.PlayerId);
                if (fallback == null) continue;
                result = engine.Execute(fallback);
                if (!result.Ok) continue;
                command = fallback;
            }
            executed++;
            onCommand?.Invoke(command, result.Events);
        }
        return executed;
    }

    private static GameCommand? Unstick(GameState s)
    {
        foreach (int actor in TurnInfo.PendingActors(s))
        {
            var command = BotBrain.TimeoutAction(s, actor);
            if (command != null) return command;
        }
        return null;
    }
}

/// <summary>Short random playouts used by the expert bot to compare two candidate commands.</summary>
public static class MonteCarlo
{
    public static bool Prefers(GameState s, int me, GameCommand a, GameCommand b, int rollouts, int horizonRounds, IRandomSource rng)
    {
        ulong seed = (ulong)rng.Next(int.MaxValue);
        double scoreA = Score(s, me, a, rollouts, horizonRounds, seed);
        double scoreB = Score(s, me, b, rollouts, horizonRounds, seed);
        return scoreA >= scoreB;
    }

    public static double Score(GameState s, int me, GameCommand first, int rollouts, int horizonRounds, ulong seed)
    {
        double total = 0;
        for (int i = 0; i < rollouts; i++)
        {
            var copy = s.RedactedFor(me);
            copy.Rng = Pcg32.Seed(seed + (ulong)i * 104729UL);
            copy.Board = s.Board;
            var engine = GameEngine.Resume(copy, s.Board);
            if (!engine.Execute(first with { ExpectedVersion = -1 }).Ok) return double.MinValue;

            var driver = new BotDriver(seed + (ulong)i);
            foreach (var p in copy.Players) driver.SetBot(p.Id, BotLevel.Medium, 0);
            int endRound = copy.Round + horizonRounds;
            int guard = 0;
            while (!copy.IsOver && copy.Round < endRound && guard++ < 4000)
            {
                var command = driver.Next(copy);
                if (command == null) break;
                if (!engine.Execute(command).Ok)
                {
                    var fallback = BotBrain.TimeoutAction(copy, command.PlayerId);
                    if (fallback == null || !engine.Execute(fallback).Ok) break;
                }
            }
            int mine = Valuation.Strength(copy, me);
            int all = copy.Players.Sum(p => Math.Max(0, Valuation.Strength(copy, p.Id)));
            total += all > 0 ? (double)mine / all : 0;
            if (copy.IsOver && copy.Winners.Contains(me)) total += 1;
        }
        return total / rollouts;
    }
}
