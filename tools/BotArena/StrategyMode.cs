using System.Collections.Concurrent;
using System.Diagnostics;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.BotArena;

/// <summary>
/// All seats play the SAME level; every game dumps one JSON line of per-player
/// behavior counters (from the action stream) + final-state metrics, so strategy
/// questions ("does stand-blitzing win?") can be answered offline with real data.
/// </summary>
internal static class StrategyMode
{
    public static void Run(int games, int players, string level, int budgetMs, string outPath)
    {
        var db = ArenaTools.LoadDatabase();
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
                bots[seat] = ArenaTools.MakeBot(level, 777UL * (ulong)(index + 1) + (ulong)seat, budgetMs);
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

    private static void Bump(Dictionary<string, int> map, string key) =>
        map[key] = map.TryGetValue(key, out int n) ? n + 1 : 1;
}
