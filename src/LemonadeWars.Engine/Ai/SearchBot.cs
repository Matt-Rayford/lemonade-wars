using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Hard difficulty: determinized Monte-Carlo search (flat PIMC). For each candidate
    /// move (greedy-pruned to a shortlist), play it in several sampled hidden-info
    /// worlds and roll each out to the end of the game with the greedy policy in every
    /// seat; pick the move with the best average outcome. Time-budgeted so it works
    /// both on the Unity main thread and in the server bot pump.
    /// </summary>
    public sealed class SearchBot : IBot, IHeavyBot
    {
        private readonly DeterministicRng _rng;
        private readonly GreedyBot _rolloutPolicy = new GreedyBot();
        private readonly int _budgetMs;
        private readonly int _maxCandidates;
        private readonly int _maxWorlds;
        private const int RolloutActionCap = 1200;
        private const int MinWorlds = 2;

        public SearchBot(ulong seed, int budgetMs = 300, int maxCandidates = 14, int maxWorlds = 14)
        {
            _rng = new DeterministicRng(seed);
            _budgetMs = budgetMs;
            _maxCandidates = maxCandidates;
            _maxWorlds = maxWorlds;
        }

        public GameAction Choose(Game game, int playerId)
        {
            var moves = game.LegalMovesFor(playerId);
            if (moves.Count == 0)
            {
                throw new InvalidOperationException(
                    $"P{playerId} has no legal moves — engine deadlock. {GameRunner.Describe(game)}");
            }
            if (moves.Count == 1)
            {
                return moves[0];
            }
            // Strictly dominated placements never reach the rollouts: on narrow turns
            // every move becomes a candidate, and noise can rank a wasted one first.
            var considered = MoveFilters.DropWasted(game, playerId, moves);
            if (considered.Count == 1)
            {
                return considered[0];
            }

            var candidates = Prune(game, playerId, considered);
            var totals = new double[candidates.Count];
            var counts = new int[candidates.Count];
            var stopwatch = Stopwatch.StartNew();

            for (int world = 0; world < _maxWorlds; world++)
            {
                if (world >= MinWorlds && stopwatch.ElapsedMilliseconds > _budgetMs)
                {
                    break;
                }
                var sampled = SearchTools.CloneState(game.State);
                SearchTools.Determinize(sampled, playerId, _rng);

                for (int i = 0; i < candidates.Count; i++)
                {
                    var trial = Game.FromState(game.Db, SearchTools.CloneState(sampled));
                    double score;
                    try
                    {
                        trial.Apply(candidates[i]);
                        score = Rollout(trial, playerId);
                    }
                    catch (InvalidActionException)
                    {
                        // Legal in the real state but not in this sampled world (rare,
                        // hidden-info dependent): skip the sample, not the move.
                        continue;
                    }
                    catch (InvalidOperationException)
                    {
                        continue; // rollout hit a pathological world; don't poison the vote
                    }
                    totals[i] += score;
                    counts[i] += 1;
                }
            }

            // Average score per candidate; unsampled candidates lose to sampled ones.
            int best = 0;
            double bestAverage = double.MinValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                double average = counts[i] > 0 ? totals[i] / counts[i] : double.MinValue;
                if (average > bestAverage)
                {
                    bestAverage = average;
                    best = i;
                }
            }
            return bestAverage == double.MinValue
                ? _rolloutPolicy.Choose(game, playerId) // every sample failed: fall back
                : candidates[best];
        }

        /// <summary>Greedy-ranked shortlist: search breadth stays bounded on wide turns.</summary>
        private List<GameAction> Prune(Game game, int playerId, IReadOnlyList<GameAction> moves)
        {
            if (moves.Count <= _maxCandidates)
            {
                return moves.ToList();
            }
            var greedyPick = _rolloutPolicy.Choose(game, playerId);
            var shortlist = new List<GameAction> { greedyPick };
            // Spread the remaining slots across the move list (it arrives grouped by
            // type, so striding samples the breadth instead of one combo family).
            double stride = (double)moves.Count / (_maxCandidates - 1);
            for (double at = 0; at < moves.Count && shortlist.Count < _maxCandidates; at += stride)
            {
                var move = moves[(int)at];
                if (!shortlist.Contains(move))
                {
                    shortlist.Add(move);
                }
            }
            return shortlist;
        }

        /// <summary>
        /// Greedy self-play to the end. Wins are discounted by rollout length —
        /// impatience, so eternal blocking never scores as well as actually winning —
        /// and a small standing term keeps a gradient on lost or capped games.
        /// </summary>
        private double Rollout(Game game, int playerId)
        {
            int steps = 0;
            while (game.State.Stage != GameStage.Finished && steps < RolloutActionCap)
            {
                var acting = game.ActingPlayers();
                if (acting.Count == 0)
                {
                    break;
                }
                steps++;
                int actor = acting[0];
                game.Apply(_rolloutPolicy.Choose(game, actor));
            }

            var state = game.State;
            double win = state.Stage == GameStage.Finished && state.Winners.Contains(playerId)
                ? 1.0 / state.Winners.Count
                : 0.0;
            return win * Math.Pow(0.999, steps) + Standing(game, playerId) * 0.05;
        }

        /// <summary>
        /// Relative FULL-score position in [0,1] — in-game VP plus met Lemon Lords,
        /// with money as the gradient. Ignoring lords here taught the search to rush
        /// VP races it was actually losing on the final count.
        /// </summary>
        private static double Standing(Game game, int playerId)
        {
            var state = game.State;
            double Score(PlayerState p) =>
                (p.InGameVictoryPoints + p.LemonLordKept.Count(id => game.MeetsLemonLord(p, id))) * 10
                + p.Money * 0.5;
            double mine = Score(state.Players[playerId]);
            double bestOther = state.Players.Where(p => p.PlayerId != playerId).Max(Score);
            return Math.Max(0.0, Math.Min(1.0, 0.5 + (mine - bestOther) * 0.02));
        }
    }
}
