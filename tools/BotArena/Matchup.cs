using System.Collections.Concurrent;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.BotArena;

/// <summary>
/// One hero bot against a table of baseline bots, hero seat rotating, seeds fixed per
/// game index so two series over the same games are a paired comparison.
/// </summary>
internal static class Matchup
{
    public delegate IBot BotFactory(int seat, ulong seed);

    public const ulong FirstGameSeed = 100_000UL;

    public static ulong BotSeed(ulong gameIndex, int seat) => 555UL * (gameIndex + 1) + (ulong)seat;

    public static string[] SeatNames(int players, int heroSeat) =>
        Enumerable.Range(0, players).Select(seat => seat == heroSeat ? "Hero" : $"Base{seat}").ToArray();

    public static MatchupResult Play(CardDatabase db, BotFactory hero, BotFactory baseline,
        int games, int players, Action<int>? progress = null)
    {
        var wins = new ConcurrentBag<double>();
        var actionCounts = new ConcurrentBag<int>();
        var heroLords = new ConcurrentBag<int>();
        int finished = 0;
        int stalled = 0;

        Parallel.For(0, games, ArenaTools.AllCoresButOne, index =>
        {
            int heroSeat = index % players;
            ulong gameSeed = FirstGameSeed + (ulong)index;
            var game = Game.Create(db, SeatNames(players, heroSeat), gameSeed);
            var bots = new Dictionary<int, IBot>();
            for (int seat = 0; seat < players; seat++)
            {
                ulong botSeed = BotSeed((ulong)index, seat);
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
                Console.WriteLine($"  !! game {index} (seed {gameSeed}, hero seat {heroSeat}): {e.Message}");
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
