using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Flat determinized Monte-Carlo search (PIMC), the Hard difficulty before the planner;
    /// kept for arena A/Bs. Each shortlisted move is played in several sampled worlds and
    /// rolled out to the end with the greedy policy in every seat; the best average wins.
    /// </summary>
    public sealed class SearchBot : IBot, IHeavyBot
    {
        private const int RolloutActionCap = 1200;
        private const int MinWorlds = 2;
        /// <summary>Per-action discount on a win, so eternal blocking never scores as well as winning.</summary>
        private const double ImpatienceDecay = 0.999;
        /// <summary>Keeps a gradient on lost or capped rollouts.</summary>
        private const double StandingWeight = 0.05;

        private readonly DeterministicRng _rng;
        private readonly GreedyBot _rolloutPolicy = new GreedyBot();
        private readonly int _budgetMs;
        private readonly int _maxCandidates;
        private readonly int _maxWorlds;

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
            // Wasted placements never reach the rollouts: on narrow turns every move
            // becomes a candidate, and noise can rank a wasted one first.
            var considered = MoveFilters.DropWasted(game, playerId, moves);
            if (considered.Count == 1)
            {
                return considered[0];
            }

            var candidates = Prune(game, playerId, considered);
            var totals = new double[candidates.Count];
            var sampleCounts = new int[candidates.Count];
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
                    if (TryScoreInWorld(game, sampled, candidates[i], playerId, out double score))
                    {
                        totals[i] += score;
                        sampleCounts[i] += 1;
                    }
                }
            }

            int best = IndexOfBestAverage(totals, sampleCounts);
            return best < 0 ? _rolloutPolicy.Choose(game, playerId) : candidates[best];
        }

        /// <summary>
        /// False skips the sample, not the move: it was illegal in this sampled world
        /// (rare, hidden-info dependent), or the rollout hit a pathological one.
        /// </summary>
        private bool TryScoreInWorld(Game game, GameState sampled, GameAction candidate, int playerId,
            out double score)
        {
            score = 0;
            var trial = Game.FromState(game.Db, SearchTools.CloneState(sampled));
            try
            {
                trial.Apply(candidate);
                score = Rollout(trial, playerId);
                return true;
            }
            catch (InvalidActionException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>Unsampled candidates lose to sampled ones; -1 when every sample failed.</summary>
        private static int IndexOfBestAverage(double[] totals, int[] sampleCounts)
        {
            int best = 0;
            double bestAverage = double.MinValue;
            for (int i = 0; i < totals.Length; i++)
            {
                double average = sampleCounts[i] > 0 ? totals[i] / sampleCounts[i] : double.MinValue;
                if (average > bestAverage)
                {
                    bestAverage = average;
                    best = i;
                }
            }
            return bestAverage == double.MinValue ? -1 : best;
        }

        /// <summary>
        /// Bounded breadth on wide turns: the greedy pick first, then the move list sampled at an
        /// even stride (it arrives grouped by type, so striding covers every family).
        /// </summary>
        private List<GameAction> Prune(Game game, int playerId, IReadOnlyList<GameAction> moves)
        {
            if (moves.Count <= _maxCandidates)
            {
                return moves.ToList();
            }
            var shortlist = new List<GameAction> { _rolloutPolicy.Choose(game, playerId) };
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
                game.Apply(_rolloutPolicy.Choose(game, acting[0]));
            }

            var state = game.State;
            double win = state.Stage == GameStage.Finished && state.Winners.Contains(playerId)
                ? 1.0 / state.Winners.Count
                : 0.0;
            return win * Math.Pow(ImpatienceDecay, steps) + Standing(game, playerId) * StandingWeight;
        }

        /// <summary>
        /// Relative FULL-score position in [0,1] (VP plus met Lemon Lords, money as the gradient):
        /// without the lords the search rushes VP races it is losing on the final count.
        /// </summary>
        private static double Standing(Game game, int playerId)
        {
            var state = game.State;
            double Score(PlayerState player) =>
                (player.InGameVictoryPoints +
                 player.LemonLordKept.Count(id => game.MeetsLemonLord(player, id))) * 10
                + player.Money * 0.5;
            double mine = Score(state.Players[playerId]);
            double bestOther = state.Players.Where(player => player.PlayerId != playerId).Max(Score);
            return Math.Max(0.0, Math.Min(1.0, 0.5 + (mine - bestOther) * 0.02));
        }
    }
}
