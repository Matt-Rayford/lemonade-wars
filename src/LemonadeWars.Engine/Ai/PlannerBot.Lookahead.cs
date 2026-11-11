using System;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    public sealed partial class PlannerBot
    {
        private const int LookaheadActionCap = 160;
        private const int EvaluatorRivalMaxCandidates = 14;
        private const ulong RollResampleSalt = 0x9E3779B97F4A7C15UL;

        /// <summary>
        /// With round lookahead, the turn is ended on a copy and judged once the planner is back on the
        /// clock. The first sample keeps the world's own roll, so lines in a world share a die; extras re-seed it.
        /// </summary>
        private double LeafValue(Game world, int playerId)
        {
            if (!_config.RoundLookahead || world.State.Stage == GameStage.InitialBuys ||
                world.State.Stage == GameStage.Finished)
            {
                return _evaluator.Evaluate(world, playerId);
            }
            int samples = Math.Max(1, _config.RollSamples);
            double total = 0;
            for (int sample = 0; sample < samples; sample++)
            {
                var future = Game.FromState(world.Db, SearchTools.CloneState(world.State));
                if (sample > 0)
                {
                    future.State.RngState ^= RollResampleSalt * (ulong)sample;
                }
                total += PlayOutRound(future, world, playerId);
            }
            return total / samples;
        }

        /// <summary>A future that wedges is valued as <paramref name="fallback"/>, the position before the round.</summary>
        private double PlayOutRound(Game future, Game fallback, int playerId)
        {
            try
            {
                if (CanKeepActing(future, playerId))
                {
                    future.Apply(new EndTurn { PlayerId = playerId });
                }
                for (int step = 0; step < LookaheadActionCap && !IsBackOnTheClock(future, playerId); step++)
                {
                    int actor = future.ActingPlayers()[0];
                    bool plannerPasses = actor == playerId && IsAnsweringWindow(future.State, actor);
                    future.Apply(plannerPasses
                        ? new PassWindow { PlayerId = actor }
                        : RivalMove(future, actor));
                }
            }
            catch (InvalidActionException)
            {
                return _evaluator.Evaluate(fallback, playerId);
            }
            catch (InvalidOperationException)
            {
                return _evaluator.Evaluate(fallback, playerId);
            }
            return _evaluator.Evaluate(future, playerId);
        }

        /// <summary>
        /// Greedy unless evaluator rivals are on: then Play-phase actions are the one-ply best from
        /// the rival's own seat; windows, decisions and turns too wide to evaluate stay greedy.
        /// </summary>
        private GameAction RivalMove(Game world, int rival)
        {
            if (!_config.EvaluatorRivals || !CanKeepActing(world, rival))
            {
                return _rivalPolicy.Choose(world, rival);
            }
            var candidates = Candidates(world, rival, world.LegalMovesFor(rival));
            if (candidates.Count > EvaluatorRivalMaxCandidates)
            {
                return _rivalPolicy.Choose(world, rival);
            }
            GameAction best = candidates[0];
            double bestValue = double.NegativeInfinity;
            foreach (var move in candidates)
            {
                double value = OnePlyValue(world, move, rival);
                if (value > bestValue)
                {
                    bestValue = value;
                    best = move;
                }
            }
            return best;
        }

        /// <summary>NaN when the move is illegal in this world or the world wedges settling it.</summary>
        private double OnePlyValue(Game world, GameAction move, int playerId)
        {
            var trial = Game.FromState(world.Db, SearchTools.CloneState(world.State));
            try
            {
                trial.Apply(move);
            }
            catch (InvalidActionException)
            {
                return double.NaN;
            }
            return Settle(trial, playerId) ? _evaluator.Evaluate(trial, playerId) : double.NaN;
        }

        private static bool IsBackOnTheClock(Game world, int playerId) =>
            world.State.Stage == GameStage.Finished ||
            (CanKeepActing(world, playerId) && world.State.ActionsRemaining > 0) ||
            world.ActingPlayers().Count == 0;
    }
}
