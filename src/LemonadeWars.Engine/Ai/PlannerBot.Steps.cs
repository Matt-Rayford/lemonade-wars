using System;
using System.Collections.Generic;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>One move applied to a copy of the world and settled, with its cancel risk.</summary>
    public sealed partial class PlannerBot
    {
        private const int SettleActionCap = 80;
        private const double GameWinningPlayCancelChance = 0.3;
        private const double OrdinaryPlayCancelChance = 0.05;

        /// <summary>
        /// One move applied and settled, paired with the world where a rival cancelled
        /// it. Rivals' hands are hidden, so the risk is an expectation over hand size
        /// rather than trust in one sampled hand; both branches are real positions the
        /// planner can keep planning from.
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

        private StepOutcome Step(Game parent, GameAction move, int playerId, bool lookahead)
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
            // Read before settling: settling resolves the stack the odds are judged from.
            double cancelChance = CancelChance(success, playerId);
            if (!Settle(success, playerId))
            {
                return StepOutcome.Illegal;
            }
            double successValue = PositionValue(success, playerId, lookahead);
            if (cancelChance <= 0)
            {
                return new StepOutcome(success, null, successValue, successValue, 0);
            }
            var cancelled = CancelledWorld(parent, move, playerId);
            return new StepOutcome(success, cancelled, successValue,
                PositionValue(cancelled, playerId, lookahead), cancelChance);
        }

        /// <summary>The same step, its worlds re-read through the round lookahead.</summary>
        private StepOutcome WithLookahead(StepOutcome step, int playerId)
        {
            if (step.Success == null)
            {
                return step;
            }
            double successValue = LeafValue(step.Success, playerId);
            double cancelledValue = step.Cancelled == null ? successValue : LeafValue(step.Cancelled, playerId);
            return new StepOutcome(step.Success, step.Cancelled, successValue, cancelledValue, step.CancelChance);
        }

        private double PositionValue(Game world, int playerId, bool lookahead) =>
            lookahead ? LeafValue(world, playerId) : _evaluator.Evaluate(world, playerId);

        /// <summary>
        /// The position a Tantrum leaves behind: the card is in the discard, the action
        /// is spent, nothing else happened. A cancelled purchase is refunded, so it only
        /// costs the action.
        /// </summary>
        private static Game CancelledWorld(Game parent, GameAction move, int playerId)
        {
            var world = Game.FromState(parent.Db, SearchTools.CloneState(parent.State));
            var state = world.State;
            if (move is PlayLemonCard play && state.Players[playerId].Hand.Remove(play.CardInstanceId))
            {
                state.LemonDiscard.Add(play.CardInstanceId);
            }
            if (state.ActionsRemaining > 0)
            {
                state.ActionsRemaining--;
            }
            else if (state.BmOnlyActionsRemaining > 0)
            {
                state.BmOnlyActionsRemaining--;
            }
            return world;
        }

        /// <summary>
        /// Odds a rival answers the play just made, judged from what a human could see
        /// (hand sizes), never from the sampled hands. Attacks are answered by their
        /// victim roughly in proportion to hand size; anything else is only worth a
        /// rival's Tantrum when it would hand the planner the game.
        /// </summary>
        private double CancelChance(Game world, int playerId)
        {
            var state = world.State;
            if (_config.SampledReactions || state.ResponseStack.Count == 0 || state.Players.Count < 2)
            {
                return 0; // sampled rivals answer for themselves; the worlds carry the risk
            }
            var top = state.ResponseStack[state.ResponseStack.Count - 1];
            if (top.OwnerId != playerId || top.LemonDefId == "apologize")
            {
                return 0; // not ours, or untantrummable by card text
            }
            if (top.AttackTargetId is int victimId)
            {
                return 0.5 - 0.5 * ThreatAssessment.UnansweredChance(state.Players[victimId]);
            }
            return ThreatAssessment.IsAboutToTriggerEnd(world, state.Players[playerId])
                ? GameWinningPlayCancelChance
                : OrdinaryPlayCancelChance;
        }

        /// <summary>
        /// Runs the interaction to quiescence. Rivals pass every window (their risk is
        /// priced by <see cref="CancelChance"/>) unless reactions are sampled, and answer
        /// decisions greedily; the planner passes windows and answers its own decisions
        /// greedily, since re-planning inside a plan is unaffordable. False if the world wedged.
        /// </summary>
        private bool Settle(Game world, int playerId)
        {
            try
            {
                for (int step = 0; step < SettleActionCap; step++)
                {
                    var state = world.State;
                    if (state.Stage == GameStage.Finished || !IsInteractionPending(state))
                    {
                        return true;
                    }
                    var acting = world.ActingPlayers();
                    if (acting.Count == 0)
                    {
                        return false;
                    }
                    int actor = FirstRivalOrSelf(acting, playerId);
                    bool passes = IsAnsweringWindow(state, actor) &&
                                  (actor == playerId || !_config.SampledReactions);
                    world.Apply(passes
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

        /// <summary>Rivals settle first; the planner only acts when it is the last one awaited.</summary>
        private static int FirstRivalOrSelf(IReadOnlyList<int> acting, int playerId)
        {
            foreach (int awaited in acting)
            {
                if (awaited != playerId)
                {
                    return awaited;
                }
            }
            return acting[0];
        }
    }
}
