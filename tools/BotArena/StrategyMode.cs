using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.BotArena;

/// <summary>
/// All seats play the SAME level; every game dumps one JSON line of per-player
/// behavior counters (from the action stream) + final-state metrics, so strategy
/// questions ("does stand-blitzing win?") can be answered offline with real data.
/// </summary>
internal static partial class StrategyMode
{
    private const int FirstGameSeed = 500_000;
    private const int MaxActions = 20000;

    public static void Run(int games, int players, string level, int budgetMs, string outPath)
    {
        var db = ArenaTools.LoadDatabase();
        var lines = new ConcurrentBag<string>();
        int finished = 0;
        int stalled = 0;
        var stopwatch = Stopwatch.StartNew();

        Parallel.For(0, games, ArenaTools.AllCoresButOne, index =>
        {
            var names = Enumerable.Range(0, players).Select(seat => $"P{seat}").ToArray();
            var game = Game.Create(db, names, (ulong)(FirstGameSeed + index));
            var bots = new Dictionary<int, IBot>();
            for (int seat = 0; seat < players; seat++)
            {
                bots[seat] = ArenaTools.MakeBot(level, 777UL * (ulong)(index + 1) + (ulong)seat, budgetMs);
            }

            var counters = new BehaviorCounters(players);
            try
            {
                PlayOut(game, bots, counters);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref stalled);
                return; // drop stalled games from the sample
            }
            lines.Add(GameJson(FirstGameSeed + index, game, counters));

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

    private static void PlayOut(Game game, Dictionary<int, IBot> bots, BehaviorCounters counters)
    {
        int actions = 0;
        while (game.State.Stage != GameStage.Finished)
        {
            if (actions++ > MaxActions)
            {
                throw new InvalidOperationException("runaway game");
            }
            var acting = game.ActingPlayers();
            if (acting.Count == 0)
            {
                throw new InvalidOperationException("no acting players");
            }
            int actor = acting[0];
            var action = bots[actor].Choose(game, actor);
            counters.Count(game, actor, action);
            game.Apply(action);
        }
    }

    private static string GameJson(int seed, Game game, BehaviorCounters counters)
    {
        var players = game.State.Players.Select(player => PlayerJson(game, player, counters));
        return $"{{\"seed\":{seed},\"players\":[{string.Join(",", players)}]}}";
    }

    private static string PlayerJson(Game game, PlayerState player, BehaviorCounters counters)
    {
        int seat = player.PlayerId;
        var winners = game.State.Winners;
        double winShare = winners.Contains(seat) ? 1.0 / winners.Count : 0.0;
        string standsBought = string.Join(",", counters.StandsBought[seat]
            .Select(bought => $"\"{bought.Key}\":{bought.Value}"));
        string blackMarketBought = string.Join(",", counters.BlackMarketBought[seat].Select(defId => $"\"{defId}\""));
        int lordsMet = player.LemonLordKept.Count(id => game.MeetsLemonLord(player, id));
        return "{" +
            $"\"seat\":{seat}," +
            $"\"won\":{winShare.ToString("F3", CultureInfo.InvariantCulture)}," +
            $"\"attacks\":{counters.Attacks[seat]}," +
            $"\"draws\":{counters.Draws[seat]}," +
            $"\"standsBought\":{{{standsBought}}}," +
            $"\"bm\":[{blackMarketBought}]," +
            $"\"bragging\":{player.BraggingRights}," +
            $"\"dibs\":{player.FirstDibsClaimed.Count}," +
            $"\"lordsMet\":{lordsMet}," +
            $"\"vp\":{player.InGameVictoryPoints + lordsMet}," +
            $"\"money\":{player.Money}," +
            $"\"standsFinal\":{player.Stands.Count}," +
            $"\"standUpgrades\":{player.Stands.Sum(stand => stand.Equipped.Count)}," +
            $"\"turfUpgrades\":{player.Turf.Equipped.Count}," +
            $"\"tantrums\":{player.TantrumPile.Count}" +
            "}";
    }
}
