using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Plans its whole turn instead of scoring one move at a time: every candidate is
    /// played on a determinized copy, the table settles with the greedy policy, and
    /// <see cref="PositionEvaluator"/> judges the result; the best few lines are followed
    /// up to three actions deep. Windows and decisions get the same one-ply read, which
    /// is how it sees that a Tantrum on a reflection keeps its own attack alive.
    /// Deterministic for a given seed.
    /// </summary>
    public sealed partial class PlannerBot : IBot, IHeavyBot
    {
        private const int MaxDepth = 3;
        private const int InnerBeam = 2;
        private const double OrderingTieBreak = 1e-4;

        private readonly DeterministicRng _rng;
        private readonly PositionEvaluator _evaluator;
        private readonly GreedyBot _rivalPolicy = new GreedyBot();
        private readonly PlannerConfig _config;

        public PlannerBot(ulong seed, PlannerConfig? config = null, EvaluationWeights? weights = null)
        {
            _rng = new DeterministicRng(seed);
            _evaluator = new PositionEvaluator(weights);
            _config = config ?? PlannerConfig.Medium;
        }

        public PlannerBot(ulong seed, int budgetMs, int worlds = 2, EvaluationWeights? weights = null)
            : this(seed, new PlannerConfig { BudgetMs = budgetMs, Worlds = Math.Max(1, worlds) }, weights)
        {
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
            if (DelegatesToGreedy(game))
            {
                return _rivalPolicy.Choose(game, playerId);
            }
            var candidates = Candidates(game, playerId, moves);
            if (candidates.Count == 1)
            {
                return candidates[0];
            }
            return BestByPlanning(game, playerId, candidates);
        }

        /// <summary>
        /// Lord picks happen before there is a board to evaluate, and die re-rolls are
        /// pure expectation math the greedy rules already get right.
        /// </summary>
        private static bool DelegatesToGreedy(Game game) =>
            game.State.Stage == GameStage.ChoosingLemonLords ||
            (game.State.PendingRoll != null && game.State.ResponseStack.Count == 0);

        /// <summary>Averages every candidate over the sampled worlds; each world gets an equal slice of the budget.</summary>
        private GameAction BestByPlanning(Game game, int playerId, List<GameAction> candidates)
        {
            var stopwatch = Stopwatch.StartNew();
            var totals = new double[candidates.Count];
            var samples = new int[candidates.Count];
            int worlds = Math.Max(1, _config.Worlds);
            for (int world = 0; world < worlds; world++)
            {
                var sampled = SearchTools.CloneState(game.State);
                SearchTools.Determinize(sampled, playerId, _rng);
                long deadlineMs = _config.BudgetMs * (world + 1) / worlds;
                var values = ValueRootCandidates(game.Db, sampled, playerId, candidates, stopwatch, deadlineMs);
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!double.IsNaN(values[i]))
                    {
                        totals[i] += values[i];
                        samples[i]++;
                    }
                }
            }

            int best = IndexOfBestAverage(totals, samples);
            return best < 0 ? _rivalPolicy.Choose(game, playerId) : candidates[best];
        }

        /// <summary>-1 when no candidate was legal in any world.</summary>
        private static int IndexOfBestAverage(double[] totals, int[] samples)
        {
            int best = -1;
            double bestAverage = double.NegativeInfinity;
            for (int i = 0; i < totals.Length; i++)
            {
                if (samples[i] == 0)
                {
                    continue;
                }
                double average = totals[i] / samples[i];
                if (average > bestAverage)
                {
                    bestAverage = average;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// One world: every root candidate gets a value, then the most promising few are
        /// followed deeper. NaN marks a move that was illegal in this world.
        /// </summary>
        private double[] ValueRootCandidates(Data.CardDatabase db, GameState sampled, int playerId,
            List<GameAction> candidates, Stopwatch stopwatch, long deadlineMs)
        {
            var parent = Game.FromState(db, sampled);
            var (steps, values) = ValueCandidates(parent, candidates, playerId, stopwatch, deadlineMs);

            foreach (int i in TopIndices(values, Math.Max(1, _config.RootBeam)))
            {
                if (stopwatch.ElapsedMilliseconds > deadlineMs)
                {
                    break;
                }
                values[i] = Math.Max(values[i], Deepen(steps[i], playerId, 2, stopwatch, deadlineMs));
            }
            // Two orderings of the same actions tie exactly; take the strong move first,
            // so the rest of the turn can still adapt to how it lands.
            for (int i = 0; i < candidates.Count; i++)
            {
                values[i] += steps[i].Value * OrderingTieBreak;
            }
            return values;
        }

        /// <summary>
        /// The best of stopping now, any single next action, or (within the beam) the
        /// best action followed by more.
        /// </summary>
        private double BestLineValue(Game world, int playerId, int depth, Stopwatch stopwatch, long deadlineMs)
        {
            double best = LeafValue(world, playerId);
            if (depth > MaxDepth || !CanKeepActing(world, playerId))
            {
                return best;
            }
            var children = Candidates(world, playerId, world.LegalMovesFor(playerId))
                .Where(move => !IsStopMove(move)).ToList();
            var (steps, values) = ValueCandidates(world, children, playerId, stopwatch, deadlineMs);
            for (int i = 0; i < children.Count; i++)
            {
                if (!double.IsNaN(values[i]))
                {
                    best = Math.Max(best, values[i]);
                }
            }

            if (depth < MaxDepth)
            {
                foreach (int i in TopIndices(values, InnerBeam))
                {
                    if (stopwatch.ElapsedMilliseconds > deadlineMs)
                    {
                        break;
                    }
                    best = Math.Max(best, Deepen(steps[i], playerId, depth + 1, stopwatch, deadlineMs));
                }
            }
            return best;
        }

        /// <summary>
        /// Every candidate gets the cheap one-ply read (apply, settle, evaluate). With
        /// round lookahead on, the most promising are then re-read through it, best
        /// first, until the deadline; the rest keep their one-ply read. A huge hand
        /// therefore costs accuracy, never more than the budget.
        /// </summary>
        private (StepOutcome[] Steps, double[] Values) ValueCandidates(Game parent, List<GameAction> candidates,
            int playerId, Stopwatch stopwatch, long deadlineMs)
        {
            var steps = new StepOutcome[candidates.Count];
            var values = new double[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                steps[i] = Step(parent, candidates[i], playerId, lookahead: false);
                values[i] = steps[i].Value;
            }
            if (!_config.RoundLookahead)
            {
                return (steps, values);
            }
            foreach (int i in TopIndices(values, candidates.Count))
            {
                if (stopwatch.ElapsedMilliseconds > deadlineMs)
                {
                    break;
                }
                steps[i] = WithLookahead(steps[i], playerId);
                values[i] = steps[i].Value;
            }
            return (steps, values);
        }

        /// <summary>Best continuation from both branches of a step, blended by the cancel odds.</summary>
        private double Deepen(StepOutcome step, int playerId, int depth, Stopwatch stopwatch, long deadlineMs)
        {
            double onSuccess = BestLineValue(step.Success!, playerId, depth, stopwatch, deadlineMs);
            double onCancel = step.Cancelled == null
                ? step.CancelledValue
                : BestLineValue(step.Cancelled, playerId, depth, stopwatch, deadlineMs);
            return step.Blend(onSuccess, onCancel);
        }

        /// <summary>
        /// Legal moves worth planning over: wasted ones dropped, and stand placements
        /// collapsed to the two ends of the row (only adjacency upgrades care where a
        /// stand sits, and both ends cover them).
        /// </summary>
        private static List<GameAction> Candidates(Game game, int playerId, IReadOnlyList<GameAction> moves)
        {
            int standCount = game.State.Players[playerId].Stands.Count;
            var kept = new List<GameAction>();
            foreach (var move in MoveFilters.DropWasted(game, playerId, moves))
            {
                int? insertIndex = move is BuyStand buy ? buy.InsertIndex
                    : move is InitialBuyStand draft ? draft.InsertIndex
                    : null;
                if (insertIndex is int index && index != 0 && index != standCount)
                {
                    continue;
                }
                kept.Add(move);
            }
            return kept.Count > 0 ? kept : moves.ToList();
        }

        /// <summary>Best first; NaN entries never qualify.</summary>
        private static IEnumerable<int> TopIndices(double[] values, int count) =>
            Enumerable.Range(0, values.Length)
                .Where(i => !double.IsNaN(values[i]))
                .OrderByDescending(i => values[i])
                .ThenBy(i => i)
                .Take(count)
                .ToList();

        /// <summary>The seat still has the initiative: its own draft pick or Play phase, nothing pending.</summary>
        private static bool CanKeepActing(Game world, int playerId)
        {
            var state = world.State;
            if (state.Stage == GameStage.Finished || IsInteractionPending(state))
            {
                return false;
            }
            if (state.Stage == GameStage.InitialBuys)
            {
                return state.InitialBuyQueue.Count > 0 && state.InitialBuyQueue[0] == playerId;
            }
            return state.ActivePlayer == playerId && state.Phase == TurnPhase.Play;
        }

        private static bool IsInteractionPending(GameState state) =>
            state.PendingDecisions.Count > 0 || state.AwaitingResponse.Count > 0 ||
            state.ResponseStack.Count > 0 || state.PendingRoll != null || state.TheftQueue.Count > 0;

        /// <summary>Awaited on a response window, with no decision of its own to answer first.</summary>
        private static bool IsAnsweringWindow(GameState state, int actor) =>
            state.AwaitingResponse.Contains(actor) &&
            state.PendingDecisions.All(decision => decision.PlayerId != actor);

        /// <summary>Ending the turn (or draft visit) is the baseline every line is measured against, not a step in one.</summary>
        private static bool IsStopMove(GameAction move) => move is EndTurn || move is InitialBuyEnd;
    }
}
