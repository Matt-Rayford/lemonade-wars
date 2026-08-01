using System.Collections.Concurrent;
using System.Diagnostics;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

// Bot tournament: one HERO bot vs a table of BASELINE bots, hero seat rotating,
// seeds fixed per game index so runs are reproducible. Reports the hero's win
// share against the fair baseline of 1/players.
//
//   dotnet run -c Release --project tools/BotArena -- hard medium 100 4 [budgetMs]

// Probe mode: replay one stalled game and print the action cycle.
//   dotnet run -c Release --project tools/BotArena -- probe <seed> <heroSeat> [budgetMs]
if (args.Length > 0 && args[0] == "probe")
{
    Probe(ulong.Parse(args[1]), int.Parse(args[2]), args.Length > 3 ? int.Parse(args[3]) : 250);
    return;
}

// Fuzz mode: hunt engine deadlocks (an awaited player with zero legal moves).
//   dotnet run -c Release --project tools/BotArena -- fuzz <seeds> [players]
if (args.Length > 0 && args[0] == "fuzz")
{
    Fuzz(int.Parse(args[1]), args.Length > 2 ? int.Parse(args[2]) : 4);
    return;
}

// Strategy telemetry: all seats the SAME level, per-player behavior + final-state
// metrics dumped as JSONL for offline analysis (win-rate by strategy).
//   dotnet run -c Release --project tools/BotArena -- strategy <games> <players> <level> <budgetMs> <out.jsonl>
if (args.Length > 0 && args[0] == "strategy")
{
    RunStrategy(int.Parse(args[1]), int.Parse(args[2]), args[3], int.Parse(args[4]), args[5]);
    return;
}

string hero = args.Length > 0 ? args[0] : "hard";
string baseline = args.Length > 1 ? args[1] : "medium";
int games = args.Length > 2 ? int.Parse(args[2]) : 100;
int players = args.Length > 3 ? int.Parse(args[3]) : 4;
int budgetMs = args.Length > 4 ? int.Parse(args[4]) : 120;

var db = LoadDatabase();
var wins = new ConcurrentBag<double>();
var actionCounts = new ConcurrentBag<int>();
var heroLords = new ConcurrentBag<int>();
int finished = 0;
var stopwatch = Stopwatch.StartNew();

Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount - 1 }, index =>
{
    int heroSeat = index % players;
    var names = Enumerable.Range(0, players)
        .Select(seat => seat == heroSeat ? "Hero" : $"Base{seat}").ToArray();
    var game = Game.Create(db, names, seed: 100_000UL + (ulong)index);

    var bots = new Dictionary<int, IBot>();
    for (int seat = 0; seat < players; seat++)
    {
        ulong botSeed = 555UL * (ulong)(index + 1) + (ulong)seat;
        bots[seat] = seat == heroSeat
            ? MakeBot(hero, botSeed, budgetMs)
            : MakeBot(baseline, botSeed, budgetMs);
    }

    try
    {
        int actions = GameRunner.PlayOut(game, bots);
        actionCounts.Add(actions);
        var winners = game.State.Winners;
        wins.Add(winners.Contains(heroSeat) ? 1.0 / winners.Count : 0.0);
        var heroPlayer = game.State.Players[heroSeat];
        heroLords.Add(heroPlayer.LemonLordKept.Count(id => game.MeetsLemonLord(heroPlayer, id)));
    }
    catch (InvalidOperationException e)
    {
        // A stalled game counts as a hero loss and gets reported for probing.
        Console.WriteLine($"  !! game {index} (seed {100_000 + index}, hero seat {heroSeat}): {e.Message}");
        wins.Add(0.0);
    }

    int done = Interlocked.Increment(ref finished);
    if (done % 10 == 0)
    {
        Console.WriteLine($"  {done}/{games} games ({stopwatch.Elapsed.TotalSeconds:F0}s)");
    }
});

double winRate = wins.Average();
double expected = 1.0 / players;
// Wilson 95% interval on the win share.
double n = games;
double z = 1.96;
double center = (winRate + z * z / (2 * n)) / (1 + z * z / n);
double margin = z * Math.Sqrt(winRate * (1 - winRate) / n + z * z / (4 * n * n)) / (1 + z * z / n);

Console.WriteLine();
Console.WriteLine($"{hero} vs {players - 1}x {baseline} — {games} games, budget {budgetMs}ms");
Console.WriteLine($"  hero win share : {winRate:P1}  (95% CI {center - margin:P1} .. {center + margin:P1})");
Console.WriteLine($"  fair baseline  : {expected:P1}");
Console.WriteLine($"  lift           : {winRate / expected:F2}x");
Console.WriteLine($"  avg actions    : {actionCounts.Average():F0}  |  wall time {stopwatch.Elapsed.TotalSeconds:F0}s");
Console.WriteLine($"  hero lords met : {(heroLords.Count > 0 ? heroLords.Average() : 0):F2} of 2 at game end");

static void Probe(ulong seed, int heroSeat, int budgetMs)
{
    var db = LoadDatabase();
    var names = Enumerable.Range(0, 4).Select(s => s == heroSeat ? "Hero" : $"Base{s}").ToArray();
    var game = Game.Create(db, names, seed);
    var bots = new Dictionary<int, IBot>();
    ulong index = seed - 100_000UL;
    for (int seat = 0; seat < 4; seat++)
    {
        ulong botSeed = 555UL * (index + 1) + (ulong)seat;
        bots[seat] = seat == heroSeat
            ? new SearchBot(botSeed, budgetMs)
            : (IBot)new GreedyBot();
    }
    for (int step = 0; step < 6000; step++)
    {
        var acting = game.ActingPlayers();
        int actor = acting[0];
        var action = bots[actor].Choose(game, actor);
        if (step >= 5000)
        {
            Console.WriteLine($"{step}: P{actor} {MoveDescriber.Describe(game, action)} " +
                $"| VP {string.Join("/", game.State.Players.Select(p => p.InGameVictoryPoints))}" +
                $" $ {string.Join("/", game.State.Players.Select(p => p.Money))}");
        }
        game.Apply(action);
        if (game.State.Stage == GameStage.Finished)
        {
            Console.WriteLine($"finished at {step}");
            return;
        }
    }
}

static void Fuzz(int seeds, int players)
{
    var db = LoadDatabase();
    var names = Enumerable.Range(0, players).Select(s => $"P{s}").ToArray();
    int deadlocks = 0;
    Parallel.For(0, seeds, index =>
    {
        // Mixed tables reach different states than pure-random ones: seat 0 greedy.
        var game = Game.Create(db, names, seed: 500_000UL + (ulong)index);
        var bots = new Dictionary<int, IBot>();
        for (int seat = 0; seat < players; seat++)
        {
            bots[seat] = seat == 0
                ? new GreedyBot()
                : (IBot)new RandomBot(999UL * (ulong)index + (ulong)seat);
        }
        try
        {
            GameRunner.PlayOut(game, bots);
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref deadlocks);
            Console.WriteLine($"DEADLOCK seed {500_000 + index}: {e.Message}");
        }
    });
    Console.WriteLine(deadlocks == 0
        ? $"clean: {seeds} games, no deadlocks"
        : $"{deadlocks}/{seeds} games deadlocked");
}

static IBot MakeBot(string level, ulong seed, int budgetMs) =>
    level == "random"
        ? new RandomBot(seed)
        : level == "ismcts"
            ? new IsmctsBot(seed, budgetMs)
            : level == "wam"
                ? new IsmctsBot(seed, budgetMs, rolloutDepth: 80)
                : level == "pimc+"
                    ? new SearchBot(seed, budgetMs, maxCandidates: 18, maxWorlds: 30)
                    : level == "pimc++"
                        ? new SearchBot(seed, budgetMs, maxCandidates: 24, maxWorlds: 50)
                        : level == "pimc"
                            ? new SearchBot(seed, budgetMs)
                            // "hard"/"wambulance" etc: exactly as shipped, own budgets.
                            : (IBot)BotFactory.Create(level, seed);

// All seats play the SAME level; every game dumps one JSON line of per-player
// behavior counters (from the action stream) + final-state metrics, so strategy
// questions ("does stand-blitzing win?") can be answered offline with real data.
static void RunStrategy(int games, int players, string level, int budgetMs, string outPath)
{
    var db = LoadDatabase();
    var lines = new ConcurrentBag<string>();
    int finished = 0;
    int stalled = 0;
    var stopwatch = Stopwatch.StartNew();

    Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount - 1 }, index =>
    {
        var names = Enumerable.Range(0, players).Select(s => $"P{s}").ToArray();
        var game = Game.Create(db, names, seed: 500_000UL + (ulong)index);
        var bots = new Dictionary<int, IBot>();
        for (int seat = 0; seat < players; seat++)
        {
            bots[seat] = MakeBot(level, 777UL * (ulong)(index + 1) + (ulong)seat, budgetMs);
        }

        var attacks = new int[players];
        var draws = new int[players];
        var standsBought = Enumerable.Range(0, players)
            .Select(_ => new Dictionary<string, int>()).ToArray();
        var bmBought = Enumerable.Range(0, players).Select(_ => new List<string>()).ToArray();

        try
        {
            int actions = 0;
            while (game.State.Stage != GameStage.Finished)
            {
                if (actions++ > 20000)
                {
                    throw new InvalidOperationException("runaway game");
                }
                var acting = game.ActingPlayers();
                if (acting.Count == 0)
                {
                    throw new InvalidOperationException("no acting players");
                }
                int pid = acting[0];
                var action = bots[pid].Choose(game, pid);
                switch (action)
                {
                    case PlayLemonCard play when game.State.LemonInstances.ContainsKey(play.CardInstanceId):
                        if (db.Lemon(game.State.LemonInstances[play.CardInstanceId].DefId).Type
                            == LemonCardType.Attack)
                        {
                            attacks[pid]++;
                        }
                        break;
                    case DrawLemonCard _:
                        draws[pid]++;
                        break;
                    case BuyStand buyStand:
                        Bump(standsBought[pid], buyStand.StandTypeId);
                        break;
                    case InitialBuyStand initial:
                        Bump(standsBought[pid], initial.StandTypeId);
                        break;
                    case BuyBlackMarket buy when buy.MarketIndex < game.State.Market.Count:
                        bmBought[pid].Add(
                            game.State.BlackMarketInstances[game.State.Market[buy.MarketIndex]].DefId);
                        break;
                }
                game.Apply(action);
            }
        }
        catch (InvalidOperationException)
        {
            Interlocked.Increment(ref stalled);
            return; // drop stalled games from the sample
        }

        var winners = game.State.Winners;
        var playerBlobs = game.State.Players.Select(p =>
        {
            string stands = string.Join(",", standsBought[p.PlayerId]
                .Select(kv => $"\"{kv.Key}\":{kv.Value}"));
            string bm = string.Join(",", bmBought[p.PlayerId].Select(d => $"\"{d}\""));
            int lordsMet = p.LemonLordKept.Count(id => game.MeetsLemonLord(p, id));
            return "{" +
                $"\"seat\":{p.PlayerId}," +
                $"\"won\":{(winners.Contains(p.PlayerId) ? 1.0 / winners.Count : 0.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"\"attacks\":{attacks[p.PlayerId]}," +
                $"\"draws\":{draws[p.PlayerId]}," +
                $"\"standsBought\":{{{stands}}}," +
                $"\"bm\":[{bm}]," +
                $"\"bragging\":{p.BraggingRights}," +
                $"\"dibs\":{p.FirstDibsClaimed.Count}," +
                $"\"lordsMet\":{lordsMet}," +
                $"\"vp\":{p.InGameVictoryPoints + lordsMet}," +
                $"\"money\":{p.Money}," +
                $"\"standsFinal\":{p.Stands.Count}," +
                $"\"standUpgrades\":{p.Stands.Sum(s => s.Equipped.Count)}," +
                $"\"turfUpgrades\":{p.Turf.Equipped.Count}," +
                $"\"tantrums\":{p.TantrumPile.Count}" +
                "}";
        });
        lines.Add($"{{\"seed\":{500_000 + index},\"players\":[{string.Join(",", playerBlobs)}]}}");

        int done = Interlocked.Increment(ref finished);
        if (done % 25 == 0)
        {
            Console.WriteLine($"  {done}/{games} games ({stopwatch.Elapsed.TotalSeconds:F0}s)");
        }
    });

    File.WriteAllLines(outPath, lines);
    Console.WriteLine($"strategy dump: {finished} games ({stalled} stalled, dropped) " +
        $"-> {outPath} in {stopwatch.Elapsed.TotalSeconds:F0}s");
}

static void Bump(Dictionary<string, int> map, string key) =>
    map[key] = map.TryGetValue(key, out int n) ? n + 1 : 1;

static CardDatabase LoadDatabase()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        string candidate = Path.Combine(dir.FullName, "game-data");
        if (Directory.Exists(candidate))
        {
            return CardDatabase.Load(candidate);
        }
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("game-data not found above the arena binary.");
}
