using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.BotArena;

/// <summary>Hunts engine deadlocks: an awaited player with zero legal moves.</summary>
internal static class FuzzMode
{
    public static void Run(int seeds, int players)
    {
        var db = ArenaTools.LoadDatabase();
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
}
