using System.Collections.Concurrent;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.BotArena;

/// <summary>Outcome of one hero-versus-table series.</summary>
internal sealed record MatchupResult(double WinShare, double AverageActions, double HeroLordsMet, int Stalled)
{
    /// <summary>Half-width of the Wilson 95% interval around the win share.</summary>
    public double Margin(int games)
    {
        const double z = 1.96;
        double n = games;
        return z * Math.Sqrt(WinShare * (1 - WinShare) / n + z * z / (4 * n * n)) / (1 + z * z / n);
    }
}

/// <summary>
/// One hero bot against a table of baseline bots, hero seat rotating, seeds fixed per
/// game index so two series over the same games are a paired comparison.
/// </summary>
internal static class Matchup
{
    public delegate IBot BotFactory(int seat, ulong seed);

    public static MatchupResult Play(CardDatabase db, BotFactory hero, BotFactory baseline,
        int games, int players, Action<int>? progress = null)
    {
        var wins = new ConcurrentBag<double>();
        var actionCounts = new ConcurrentBag<int>();
        var heroLords = new ConcurrentBag<int>();
        int finished = 0;
        int stalled = 0;

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
                bots[seat] = seat == heroSeat ? hero(seat, botSeed) : baseline(seat, botSeed);
            }

            try
            {
                actionCounts.Add(GameRunner.PlayOut(game, bots));
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
                Interlocked.Increment(ref stalled);
            }
            progress?.Invoke(Interlocked.Increment(ref finished));
        });

        return new MatchupResult(
            wins.Average(),
            actionCounts.Count > 0 ? actionCounts.Average() : 0,
            heroLords.Count > 0 ? heroLords.Average() : 0,
            stalled);
    }
}
