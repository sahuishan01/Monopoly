using System.Diagnostics;
using Game.Core.AI;
using Game.Core.Board;
using Game.Core.Engine;
using Game.Core.Events;
using Game.Core.Replay;
using Game.Core.Rules;
using Game.Core.Serialization;
using Game.Core.State;

// BoardEmpire.Simulator
//   simulate   --games 10000 --players 4 [--preset classic] [--board neo_city] [--level medium] [--seed 1] [--max-rounds 300]
//   tournament --games 2000 --players 4 [--preset classic]          (one seat per bot level, rotated)
//   record     --out match.json [--players 4] [--preset classic] [--seed 1]
//   replay     match.json [--story]
//   boards     --out boards/                                        (export built-in boards as JSON)

string mode = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "simulate";
var opt = new Dictionary<string, string>();
var positional = new List<string>();
for (int i = mode == "simulate" && (args.Length == 0 || args[0].StartsWith("--")) ? 0 : 1; i < args.Length; i++)
{
    if (args[i].StartsWith("--"))
    {
        bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
        opt[args[i][2..]] = hasValue ? args[++i] : "true";
    }
    else positional.Add(args[i]);
}
string Opt(string key, string fallback) => opt.TryGetValue(key, out var v) ? v : fallback;
int OptInt(string key, int fallback) => int.Parse(Opt(key, fallback.ToString()));

switch (mode)
{
    case "simulate": return Simulate();
    case "tournament": return Tournament();
    case "record": return Record();
    case "replay": return Replay();
    case "boards": return Boards();
    default:
        Console.Error.WriteLine($"Unknown mode '{mode}'. Use simulate | tournament | record | replay | boards.");
        return 2;
}

MatchConfig Config(ulong seed, int players, BotLevel[]? levels = null)
{
    var rules = RulePresets.Get(Opt("preset", "classic"));
    int cap = OptInt("max-rounds", 300);
    if (rules.MaximumRounds == 0 || rules.MaximumRounds > cap) rules.MaximumRounds = cap;
    var cfg = new MatchConfig { MatchId = $"sim-{seed}", Seed = seed, BoardId = Opt("board", BoardLibrary.DefaultBoardId), Rules = rules };
    for (int i = 0; i < players; i++)
        cfg.Players.Add(new PlayerSetup { Name = $"P{i + 1}", Token = i, IsBot = true, BotLevel = levels?[i] ?? Enum.Parse<BotLevel>(Opt("level", "Medium"), true) });
    return cfg;
}

(GameEngine Engine, List<GameEvent> Log, GameState Initial) Play(MatchConfig cfg, bool keepLog, Action<GameEvent>? observe = null)
{
    var engine = GameEngine.Create(cfg);
    var initial = keepLog ? engine.State.RedactedFor(-1) : engine.State;
    var driver = new BotDriver(cfg.Seed);
    for (int i = 0; i < cfg.Players.Count; i++) driver.SetBot(i, cfg.Players[i].BotLevel, OptInt("rollouts", 4));
    var log = new List<GameEvent>();
    void Sink(IReadOnlyList<GameEvent> events)
    {
        if (keepLog) log.AddRange(events);
        if (observe != null)
            foreach (var e in events) observe(e);
    }
    Sink(engine.Start());
    BotDriver.RunToEnd(engine, driver, 500_000, keepLog || observe != null ? (_, ev) => Sink(ev) : null);
    return (engine, log, initial);
}

int Simulate()
{
    int games = OptInt("games", 1000), players = OptInt("players", 4);
    ulong seed = ulong.Parse(Opt("seed", "1"));
    var sw = Stopwatch.StartNew();
    var wins = new int[players];
    long turns = 0, rounds = 0, bankruptTurnSum = 0, bankruptcies = 0, finalMoney = 0, startMoney = 0;
    int byRoundLimit = 0;
    var board = BoardLibrary.Get(Opt("board", BoardLibrary.DefaultBoardId));
    var districtWins = new long[board.Districts.Count];
    var districtOwned = new long[board.Districts.Count];
    var districtRent = new long[board.Districts.Count];
    var gate = new object();

    Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, g =>
    {
        var cfg = Config(seed + (ulong)g, players);
        // First player to complete each district; later hand-overs (bankruptcies) do not count.
        var firstHolder = new int[board.Districts.Count];
        Array.Fill(firstHolder, -1);
        var (engine, _, _) = Play(cfg, false, e =>
        {
            if (e is DistrictCompleted dc && firstHolder[board.DistrictIndex(dc.District)] < 0)
                firstHolder[board.DistrictIndex(dc.District)] = dc.Player;
        });
        var s = engine.State;
        lock (gate)
        {
            foreach (int w in s.Winners) wins[w]++;
            turns += s.TurnNumber;
            rounds += s.Round;
            if (s.EndReason == GameEndReason.RoundLimit) byRoundLimit++;
            foreach (var p in s.Players)
            {
                if (p.Bankrupt)
                {
                    bankruptcies++;
                    bankruptTurnSum += p.Stats.EliminatedOnTurn;
                }
                finalMoney += p.Money;
            }
            startMoney += (long)s.Rules.StartingMoney * players;
            for (int d = 0; d < board.Districts.Count; d++)
            {
                foreach (int t in board.DistrictTiles(board.Districts[d].Id)) districtRent[d] += s.Properties[t]!.RentCollected;
                if (firstHolder[d] < 0) continue;
                districtOwned[d]++;
                if (s.Winners.Contains(firstHolder[d])) districtWins[d]++;
            }
        }
    });
    sw.Stop();

    double avgTurns = (double)turns / games;
    Console.WriteLine($"Games: {games}   Players: {players}   Preset: {Opt("preset", "classic")}   Board: {board.BoardId}   Bots: {Opt("level", "Medium")}");
    Console.WriteLine($"Elapsed: {sw.Elapsed.TotalSeconds:0.0}s ({games / sw.Elapsed.TotalSeconds:0} games/s)");
    Console.WriteLine();
    Console.WriteLine($"Average turns: {avgTurns:0}   Average rounds: {(double)rounds / games:0.0}");
    Console.WriteLine($"Average duration equivalent: {avgTurns * 45 / 60:0} min (45 s per turn)");
    Console.WriteLine($"Ended by round limit: {100.0 * byRoundLimit / games:0.0}%");
    Console.WriteLine(bankruptcies > 0
        ? $"Bankruptcy: turn {(double)bankruptTurnSum / bankruptcies:0} on average ({(double)bankruptcies / games:0.00} per game)"
        : "Bankruptcy: none");
    Console.WriteLine($"Economy inflation: final cash is {100.0 * finalMoney / startMoney:0}% of starting cash");
    Console.WriteLine();
    Console.WriteLine("Winner position:");
    int totalWins = wins.Sum();
    for (int i = 0; i < players; i++) Console.WriteLine($"  P{i + 1}: {100.0 * wins[i] / Math.Max(1, totalWins):0.0}%");
    Console.WriteLine();
    Console.WriteLine("District profitability (how often completed, win rate of the first player to complete it, rent per game):");
    for (int d = 0; d < board.Districts.Count; d++)
        Console.WriteLine($"  {board.Districts[d].Name,-16} completed {100.0 * districtOwned[d] / games,5:0.0}%   wins {(districtOwned[d] > 0 ? 100.0 * districtWins[d] / districtOwned[d] : 0),5:0.0}%   rent {(double)districtRent[d] / games,7:0}");
    return 0;
}

int Tournament()
{
    int games = OptInt("games", 400), players = OptInt("players", 4);
    ulong seed = ulong.Parse(Opt("seed", "1"));
    var all = new[] { BotLevel.Easy, BotLevel.Medium, BotLevel.Hard, BotLevel.Expert };
    var levels = opt.ContainsKey("levels")
        ? Opt("levels", "").Split(',').Select(x => Enum.Parse<BotLevel>(x, true)).ToArray()
        : all;
    var wins = new Dictionary<BotLevel, int>();
    var seats = new Dictionary<BotLevel, int>();
    var gate = new object();
    var sw = Stopwatch.StartNew();
    Parallel.For(0, games, g =>
    {
        var lineup = Enumerable.Range(0, players).Select(i => levels[(i + g) % levels.Length]).ToArray();
        var (engine, _, _) = Play(Config(seed + (ulong)g, players, lineup), false);
        lock (gate)
        {
            foreach (var l in lineup) seats[l] = seats.GetValueOrDefault(l) + 1;
            foreach (int w in engine.State.Winners) wins[lineup[w]] = wins.GetValueOrDefault(lineup[w]) + 1;
        }
    });
    Console.WriteLine($"Bot tournament: {games} games, {players} players, {sw.Elapsed.TotalSeconds:0.0}s");
    foreach (var l in levels.Distinct())
        Console.WriteLine($"  {l,-7} win rate per seat: {100.0 * wins.GetValueOrDefault(l) / Math.Max(1, seats.GetValueOrDefault(l)):0.0}%  ({wins.GetValueOrDefault(l)}/{seats.GetValueOrDefault(l)})");
    return 0;
}

int Record()
{
    var cfg = Config(ulong.Parse(Opt("seed", "1")), OptInt("players", 4));
    var (engine, log, initial) = Play(cfg, true);
    var file = new ReplayFile { Board = engine.Board, Initial = initial, Events = log };
    string path = Opt("out", "match.json");
    File.WriteAllText(path, file.ToJson());
    Console.WriteLine($"Recorded {log.Count} events over {engine.State.TurnNumber} turns to {path}");
    Console.WriteLine($"Final state hash: {StateHasher.Hash(engine.State)}");
    return 0;
}

int Replay()
{
    if (positional.Count == 0)
    {
        Console.Error.WriteLine("Usage: replay <file> [--story] [--turn N]");
        return 2;
    }
    var file = ReplayFile.FromJson(File.ReadAllText(positional[0]));
    var player = new ReplayPlayer(file);
    if (opt.ContainsKey("turn")) player.JumpToTurn(OptInt("turn", 1));
    else player.Seek(player.Length);
    var s = player.State;
    Console.WriteLine($"Match {s.MatchId} on {file.Board.Name}: {file.Events.Count} events, at turn {s.TurnNumber}, round {s.Round}");
    Console.WriteLine($"State hash: {StateHasher.Hash(s)}");
    foreach (var st in Calc.Standings(s))
        Console.WriteLine($"  #{st.Rank} {s.Players[st.Player].Name,-10} net worth {st.NetWorth,6}{(s.Players[st.Player].Bankrupt ? "  (bankrupt)" : "")}");
    if (opt.ContainsKey("story"))
    {
        Console.WriteLine();
        foreach (var e in MatchStory.Build(file)) Console.WriteLine($"  Turn {e.Turn,3}  {e.Text}");
        Console.WriteLine();
        foreach (var a in MatchStory.Awards(s)) Console.WriteLine($"  {a.Title}: {s.Players[a.Player].Name} ({a.Detail})");
    }
    return 0;
}

int Boards()
{
    string dir = Opt("out", "boards");
    Directory.CreateDirectory(dir);
    foreach (var b in BoardLibrary.All)
    {
        File.WriteAllText(Path.Combine(dir, b.BoardId + ".json"), BoardLibrary.ToJson(b));
        Console.WriteLine($"Wrote {b.BoardId}.json ({b.Count} tiles)");
    }
    return 0;
}
