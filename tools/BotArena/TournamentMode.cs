using System.Diagnostics;

namespace LemonadeWars.BotArena;

/// <summary>
/// One HERO level vs a table of BASELINE bots; reports the hero's win share against
/// the fair baseline of 1/players.
/// </summary>
internal static class TournamentMode
{
    public static void Run(string hero, string baseline, int games, int players, int budgetMs)
    {
        var db = ArenaTools.LoadDatabase();
        var stopwatch = Stopwatch.StartNew();
        var result = Matchup.Play(db,
            (_, seed) => ArenaTools.MakeBot(hero, seed, budgetMs),
            (_, seed) => ArenaTools.MakeBot(baseline, seed, budgetMs),
            games, players,
            done =>
            {
                if (done % 10 == 0)
                {
                    Console.WriteLine($"  {done}/{games} games ({stopwatch.Elapsed.TotalSeconds:F0}s)");
                }
            });

        double expected = 1.0 / players;
        double margin = result.Margin(games);
        Console.WriteLine();
        Console.WriteLine($"{hero} vs {players - 1}x {baseline} — {games} games, budget {budgetMs}ms");
        Console.WriteLine($"  hero win share : {result.WinShare:P1}  (95% CI {result.WinShare - margin:P1} .. {result.WinShare + margin:P1})");
        Console.WriteLine($"  fair baseline  : {expected:P1}");
        Console.WriteLine($"  lift           : {result.WinShare / expected:F2}x");
        Console.WriteLine($"  avg actions    : {result.AverageActions:F0}  |  wall time {stopwatch.Elapsed.TotalSeconds:F0}s");
        Console.WriteLine($"  hero lords met : {result.HeroLordsMet:F2} of 2 at game end");
    }
}
