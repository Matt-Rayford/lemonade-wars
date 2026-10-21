using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Medium difficulty: plans its whole turn instead of scoring one move at a time.
    /// Every candidate is applied to a determinized copy of the game, rivals answer
    /// with the greedy policy, and the resulting position is judged by
    /// <see cref="PositionEvaluator"/>; the best few lines are followed up to three
    /// actions deep. The same one-ply machinery answers response windows and
    /// decisions, which is what lets it see that a Tantrum on a reflection keeps its
    /// own attack alive. Deterministic for a given seed.
    /// </summary>
    public sealed class PlannerBot : IBot, IHeavyBot
    {
        private const int MaxDepth = 3;
        private const int DefaultRootBeam = 4;
        private const int InnerBeam = 2;
        private const int SettleActionCap = 80;
        private const int LookaheadActionCap = 160;
        private const double OrderingTieBreak = 1e-4;

        private readonly DeterministicRng _rng;
        private readonly PositionEvaluator _evaluator;
        private readonly GreedyBot _rivalPolicy = new GreedyBot();
        private readonly int _budgetMs;
        private readonly int _worlds;
        private readonly int _rootBeam;
        private readonly bool _roundLookahead;

        /// <param name="worlds">Determinizations averaged per decision; more worlds, steadier reads on hidden cards.</param>
        /// <param name="rootBeam">How many opening moves are followed to the end of the turn.</param>
        /// <param name="roundLookahead">
        /// Judge each line at the start of the planner's NEXT turn — after its sale roll
        /// and a greedy turn by every rival — instead of at the end of this one, so the
        /// race and the rivals' answers are priced in. Several times the cost.
        /// </param>
        public PlannerBot(ulong seed, int budgetMs = 150, int worlds = 2, EvaluationWeights? weights = null,
            int rootBeam = DefaultRootBeam, bool roundLookahead = false)
        {
            _rng = new DeterministicRng(seed);
            _evaluator = new PositionEvaluator(weights);
            _budgetMs = budgetMs;
            _worlds = Math.Max(1, worlds);
            _rootBeam = Math.Max(1, rootBeam);
            _roundLookahead = roundLookahead;
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

        private GameAction BestByPlanning(Game game, int playerId, List<GameAction> candidates)
        {
            var stopwatch = Stopwatch.StartNew();
            var totals = new double[candidates.Count];
            var samples = new int[candidates.Count];
            for (int world = 0; world < _worlds; world++)
            {
                var sampled = SearchTools.CloneState(game.State);
                SearchTools.Determinize(sampled, playerId, _rng);
                long deadline = _budgetMs * (world + 1) / _worlds;
                var values = RootValues(game.Db, sampled, playerId, candidates, stopwatch, deadline);
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!double.IsNaN(values[i]))
                    {
                        totals[i] += values[i];
                        samples[i]++;
                    }
                }
            }

            int best = -1;
            double bestAverage = double.NegativeInfinity;
            for (int i = 0; i < candidates.Count; i++)
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
            return best < 0 ? _rivalPolicy.Choose(game, playerId) : candidates[best];
        }

        /// <summary>
        /// One world: every root candidate gets a one-ply value, then the most promising
        /// few are followed deeper. NaN marks a move that was illegal in this world.
        /// </summary>
        private double[] RootValues(Data.CardDatabase db, GameState sampled, int playerId,
            List<GameAction> candidates, Stopwatch stopwatch, long deadlineMs)
        {
            var steps = new StepOutcome[candidates.Count];
            var values = new double[candidates.Count];
            var parent = Game.FromState(db, sampled);
            for (int i = 0; i < candidates.Count; i++)
            {
                steps[i] = Step(parent, candidates[i], playerId);
                values[i] = steps[i].Value;
            }

            foreach (int i in TopIndices(values, _rootBeam))
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
        /// Value of the best line from here: stopping now, any single next action, or
        /// (within the beam) the best action followed by more. Rivals' interjections
        /// are settled with the greedy policy before every evaluation.
        /// </summary>
        private double Continue(Game world, int playerId, int depth, Stopwatch stopwatch, long deadlineMs)
        {
            double best = Leaf(world, playerId);
            if (depth > MaxDepth || !CanKeepActing(world, playerId))
            {
                return best;
            }
            var children = Candidates(world, playerId, world.LegalMovesFor(playerId))
                .Where(move => !IsStopMove(move)).ToList();
            var steps = new StepOutcome[children.Count];
            var values = new double[children.Count];
            for (int i = 0; i < children.Count; i++)
            {
                steps[i] = Step(world, children[i], playerId);
                values[i] = steps[i].Value;
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
        /// Worth of a settled position. With round lookahead the turn is ended on a copy,
        /// every rival plays a greedy turn, and the position is judged when the planner
        /// is back on the clock — the sale die rolls once per world, so lines within a
        /// world are compared on the same roll.
        /// </summary>
        private double Leaf(Game world, int playerId)
        {
            if (!_roundLookahead || world.State.Stage == GameStage.InitialBuys ||
                world.State.Stage == GameStage.Finished)
            {
                return _evaluator.Evaluate(world, playerId);
            }
            var future = Game.FromState(world.Db, SearchTools.CloneState(world.State));
            try
            {
                if (CanKeepActing(future, playerId))
                {
                    future.Apply(new EndTurn { PlayerId = playerId });
                }
                for (int step = 0; step < LookaheadActionCap && !BackOnTheClock(future, playerId); step++)
                {
                    int actor = future.ActingPlayers()[0];
                    bool inWindow = actor == playerId && future.State.AwaitingResponse.Contains(actor) &&
                                    future.State.PendingDecisions.All(d => d.PlayerId != actor);
                    future.Apply(inWindow
                        ? new PassWindow { PlayerId = actor }
                        : _rivalPolicy.Choose(future, actor));
                }
            }
            catch (InvalidActionException)
            {
                return _evaluator.Evaluate(world, playerId);
            }
            catch (InvalidOperationException)
            {
                return _evaluator.Evaluate(world, playerId);
            }
            return _evaluator.Evaluate(future, playerId);
        }

        private static bool BackOnTheClock(Game world, int playerId) =>
            world.State.Stage == GameStage.Finished ||
            (CanKeepActing(world, playerId) && world.State.ActionsRemaining > 0) ||
            world.ActingPlayers().Count == 0;

        /// <summary>The planner's seat still has the initiative: its own draft pick or Play phase, nothing pending.</summary>
        private static bool CanKeepActing(Game world, int playerId)
        {
            var s = world.State;
            if (s.Stage == GameStage.Finished || InteractionPending(s))
            {
                return false;
            }
            if (s.Stage == GameStage.InitialBuys)
            {
                return s.InitialBuyQueue.Count > 0 && s.InitialBuyQueue[0] == playerId;
            }
            return s.ActivePlayer == playerId && s.Phase == TurnPhase.Play;
        }

        private static bool InteractionPending(GameState s) =>
            s.PendingDecisions.Count > 0 || s.AwaitingResponse.Count > 0 ||
            s.ResponseStack.Count > 0 || s.PendingRoll != null || s.TheftQueue.Count > 0;

        /// <summary>Ending the turn (or draft visit) is the baseline every line is measured against, not a step in one.</summary>
        private static bool IsStopMove(GameAction move) => move is EndTurn || move is InitialBuyEnd;

        /// <summary>
        /// One move applied and settled, paired with the world where a rival cancelled
        /// it. Rivals' hands are hidden, so the risk is an expectation over hand size
        /// rather than a trust in one sampled hand; both branches are real positions
        /// the planner can keep planning from.
        /// </summary>
        private readonly struct StepOutcome
        {
            public readonly Game? Success;
            public readonly Game? Cancelled;
            public readonly double SuccessValue;
            public readonly double CancelledValue;
            public readonly double CancelChance;

            public StepOutcome(Game? success, Game? cancelled, double successValue,
                double cancelledValue, double cancelChance)
            {
                Success = success;
                Cancelled = cancelled;
                SuccessValue = successValue;
                CancelledValue = cancelledValue;
                CancelChance = cancelChance;
            }

            public static StepOutcome Illegal => new StepOutcome(null, null, double.NaN, double.NaN, 0);

            /// <summary>Expected worth of the move alone; NaN when it was illegal in this world.</summary>
            public double Value => Blend(SuccessValue, CancelledValue);

            public double Blend(double onSuccess, double onCancel) =>
                Success == null ? double.NaN
                    : (1 - CancelChance) * onSuccess + CancelChance * onCancel;
        }

        private StepOutcome Step(Game parent, GameAction move, int playerId)
        {
            var success = Game.FromState(parent.Db, SearchTools.CloneState(parent.State));
            try
            {
                success.Apply(move);
            }
            catch (InvalidActionException)
            {
                return StepOutcome.Illegal;
            }
            double cancelChance = CancelChance(success, playerId);
            if (!Settle(success, playerId))
            {
                return StepOutcome.Illegal;
            }
            double successValue = Leaf(success, playerId);
            if (cancelChance <= 0)
            {
                return new StepOutcome(success, null, successValue, successValue, 0);
            }
            var cancelled = CancelledWorld(parent, move, playerId);
            return new StepOutcome(success, cancelled, successValue,
                Leaf(cancelled, playerId), cancelChance);
        }

        /// <summary>Best continuation from both branches of a step, blended by the cancel odds.</summary>
        private double Deepen(StepOutcome step, int playerId, int depth, Stopwatch stopwatch, long deadlineMs)
        {
            double onSuccess = Continue(step.Success!, playerId, depth, stopwatch, deadlineMs);
            double onCancel = step.Cancelled == null
                ? step.CancelledValue
                : Continue(step.Cancelled, playerId, depth, stopwatch, deadlineMs);
            return step.Blend(onSuccess, onCancel);
        }

        /// <summary>
        /// The position a Tantrum leaves behind: the card is in the discard, the action
        /// is spent, nothing else happened. A cancelled purchase is refunded, so it only
        /// costs the action.
        /// </summary>
        private static Game CancelledWorld(Game parent, GameAction move, int playerId)
        {
            var world = Game.FromState(parent.Db, SearchTools.CloneState(parent.State));
            var s = world.State;
            if (move is PlayLemonCard play && s.Players[playerId].Hand.Remove(play.CardInstanceId))
            {
                s.LemonDiscard.Add(play.CardInstanceId);
            }
            if (s.ActionsRemaining > 0)
            {
                s.ActionsRemaining--;
            }
            else if (s.BmOnlyActionsRemaining > 0)
            {
                s.BmOnlyActionsRemaining--;
            }
            return world;
        }

        /// <summary>
        /// Odds a rival answers the play just made, judged from what a human could see
        /// (hand sizes), never from the sampled hands. Attacks are answered by their
        /// victim roughly in proportion to hand size; anything else is only worth a
        /// rival's Tantrum when it would hand the planner the game.
        /// </summary>
        private static double CancelChance(Game world, int playerId)
        {
            var s = world.State;
            if (s.ResponseStack.Count == 0 || s.Players.Count < 2)
            {
                return 0;
            }
            var top = s.ResponseStack[s.ResponseStack.Count - 1];
            if (top.OwnerId != playerId || top.LemonDefId == "apologize")
            {
                return 0; // not ours, or untantrummable by card text
            }
            if (top.AttackTargetId is int victimId)
            {
                return 0.5 - 0.5 * ThreatAssessment.UnansweredChance(s.Players[victimId]);
            }
            return ThreatAssessment.IsAboutToTriggerEnd(world, s.Players[playerId]) ? 0.3 : 0.05;
        }

        /// <summary>
        /// Run the interaction to quiescence: rivals pass every window (their risk is
        /// priced separately) and answer decisions greedily; the planner itself passes
        /// windows and answers its own decisions greedily, since re-planning inside a
        /// plan would be unaffordable. Returns false if the world wedged.
        /// </summary>
        private bool Settle(Game world, int playerId)
        {
            try
            {
                for (int step = 0; step < SettleActionCap; step++)
                {
                    var s = world.State;
                    if (s.Stage == GameStage.Finished || !InteractionPending(s))
                    {
                        return true;
                    }
                    var acting = world.ActingPlayers();
                    if (acting.Count == 0)
                    {
                        return false;
                    }
                    int actor = acting[0];
                    foreach (int awaited in acting)
                    {
                        if (awaited != playerId)
                        {
                            actor = awaited; // rivals settle first; the planner only passes when alone
                            break;
                        }
                    }
                    bool inWindow = s.AwaitingResponse.Contains(actor) &&
                                    s.PendingDecisions.All(d => d.PlayerId != actor);
                    world.Apply(inWindow
                        ? new PassWindow { PlayerId = actor }
                        : _rivalPolicy.Choose(world, actor));
                }
                return false;
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

        /// <summary>Indices of the highest values, best first; NaN entries never qualify.</summary>
        private static IEnumerable<int> TopIndices(double[] values, int count) =>
            Enumerable.Range(0, values.Length)
                .Where(i => !double.IsNaN(values[i]))
                .OrderByDescending(i => values[i])
                .ThenBy(i => i)
                .Take(count)
                .ToList();
    }
}
