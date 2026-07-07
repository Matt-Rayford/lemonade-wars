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

string hero = args.Length > 0 ? args[0] : "hard";
string baseline = args.Length > 1 ? args[1] : "medium";
int games = args.Length > 2 ? int.Parse(args[2]) : 100;
int players = args.Length > 3 ? int.Parse(args[3]) : 4;
int budgetMs = args.Length > 4 ? int.Parse(args[4]) : 120;

var db = LoadDatabase();
var wins = new ConcurrentBag<double>();
var actionCounts = new ConcurrentBag<int>();
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

static IBot MakeBot(string level, ulong seed, int budgetMs) =>
    level == "random"
        ? new RandomBot(seed)
        : level == "hard"
            ? new SearchBot(seed, budgetMs)
            : BotFactory.Create(level, seed);

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
