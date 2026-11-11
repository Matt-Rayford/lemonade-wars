using System.Collections.Concurrent;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.BotArena;

/// <summary>
/// Self-play data for the evaluator: mixed tables of fast planners (carrying the
/// weights under study) and greedy bots play to the end; positions are sampled
/// along the way and labelled with the final result, once per viewer seat so the
/// hidden-lord treatment matches what a bot sees at the table.
/// </summary>
internal static class FeatureSampler
{
    private const int PlannerBudgetMs = 25;
    private const double PlannerSeatShare = 0.65;
    private const double SamplePerAction = 0.08;
    private const int MaxActions = 6000;

    public static List<PositionSample> Generate(CardDatabase db, EvaluationWeights weights, int games,
        ulong seedBase, Action<int>? progress = null)
    {
        var samples = new ConcurrentBag<PositionSample>();
        int finished = 0;
        Parallel.For(0, games, ArenaTools.AllCoresButOne, index =>
        {
            var rng = new DeterministicRng(seedBase + (ulong)index * 7919UL);
            int players = 3 + rng.Next(3); // 3..5 seats, so weights generalise across table sizes
            var names = Enumerable.Range(0, players).Select(seat => $"P{seat}").ToArray();
            var game = Game.Create(db, names, seedBase + (ulong)index);
            var bots = new Dictionary<int, IBot>();
            for (int seat = 0; seat < players; seat++)
            {
                bots[seat] = rng.Next(100) < PlannerSeatShare * 100
                    ? new PlannerBot(seedBase ^ (ulong)(index * 31 + seat),
                        new PlannerConfig { BudgetMs = PlannerBudgetMs, Worlds = 1 }, weights)
                    : new GreedyBot();
            }

            var positions = new List<double[][][]>(); // [viewer][player][feature]
            try
            {
                PlayAndSample(game, bots, rng, positions);
            }
            catch (InvalidOperationException)
            {
                return; // a stalled game has no result to learn from
            }
            var winShares = WinShares(game.State);
            foreach (var position in positions)
            {
                foreach (var rowsForViewer in position)
                {
                    samples.Add(new PositionSample(rowsForViewer, winShares));
                }
            }
            progress?.Invoke(Interlocked.Increment(ref finished));
        });
        return samples.ToList();
    }

    private static double[] WinShares(GameState state) =>
        state.Players
            .Select(player => state.Winners.Contains(player.PlayerId) ? 1.0 / state.Winners.Count : 0.0)
            .ToArray();

    private static void PlayAndSample(Game game, Dictionary<int, IBot> bots, DeterministicRng rng,
        List<double[][][]> positions)
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
            game.Apply(bots[actor].Choose(game, actor));

            bool isInPlay = game.State.Stage == GameStage.Playing || game.State.Stage == GameStage.FinalRound;
            if (isInPlay && rng.Next(10_000) < SamplePerAction * 10_000)
            {
                positions.Add(Snapshot(game));
            }
        }
    }

    /// <summary>Feature rows for every (viewer, player) pair of the current position.</summary>
    private static double[][][] Snapshot(Game game)
    {
        double remainingRounds = PositionEvaluator.RemainingRounds(game);
        int playerCount = game.State.Players.Count;
        var rowsByViewer = new double[playerCount][][];
        for (int viewer = 0; viewer < playerCount; viewer++)
        {
            rowsByViewer[viewer] = new double[playerCount][];
            for (int player = 0; player < playerCount; player++)
            {
                rowsByViewer[viewer][player] =
                    PositionFeatures.Extract(game, game.State.Players[player], viewer, remainingRounds);
            }
        }
        return rowsByViewer;
    }
}
