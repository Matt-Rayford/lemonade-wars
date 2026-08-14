using System.Collections.Concurrent;
using System.Diagnostics;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.BotArena;

/// <summary>
/// One HERO bot vs a table of BASELINE bots, hero seat rotating, seeds fixed per
/// game index so runs are reproducible. Reports the hero's win share against the
/// fair baseline of 1/players.
/// </summary>
internal static class TournamentMode
{
    public static void Run(string hero, string baseline, int games, int players, int budgetMs)
    {
        var db = ArenaTools.LoadDatabase();
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
                    ? ArenaTools.MakeBot(hero, botSeed, budgetMs)
                    : ArenaTools.MakeBot(baseline, botSeed, budgetMs);
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
    }
}
