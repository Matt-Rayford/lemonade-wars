using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.BotArena;

/// <summary>Hunts engine deadlocks: an awaited player with zero legal moves.</summary>
internal static class FuzzMode
{
    private const int FirstGameSeed = 500_000;

    public static void Run(int seeds, int players)
    {
        var db = ArenaTools.LoadDatabase();
        var names = Enumerable.Range(0, players).Select(seat => $"P{seat}").ToArray();
        int deadlocks = 0;
        Parallel.For(0, seeds, index =>
        {
            int gameSeed = FirstGameSeed + index;
            var game = Game.Create(db, names, (ulong)gameSeed);
            var bots = new Dictionary<int, IBot>();
            for (int seat = 0; seat < players; seat++)
            {
                // Mixed tables reach different states than pure-random ones: seat 0 greedy.
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
                Console.WriteLine($"DEADLOCK seed {gameSeed}: {e.Message}");
            }
        });
        Console.WriteLine(deadlocks == 0
            ? $"clean: {seeds} games, no deadlocks"
            : $"{deadlocks}/{seeds} games deadlocked");
    }
}
