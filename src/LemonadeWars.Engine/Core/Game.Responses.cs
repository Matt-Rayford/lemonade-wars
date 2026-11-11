using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Response stack, windows, and pending-decision machinery.
    ///
    /// The model is a simplified Magic-style stack: plays and Black Market purchases go on
    /// <see cref="GameState.ResponseStack"/>; a window invites eligible players to respond
    /// (Tantrum / Tag / Rubber-Glue against the top item, Out of Stock against a pending
    /// roll, Profit Share against a resolved theft). Responses push new items. When everyone
    /// passes, the top item resolves; Tantrums mark their target cancelled, and cancelled
    /// items pop without effect (purchases get their money back — not the action).
    ///
    /// <see cref="Pump"/> is the single driver that advances everything until player input
    /// is required. Players who cannot legally respond are auto-passed, so uncontested plays
    /// resolve synchronously inside one Apply call.
    ///
    /// This file holds the driver; windows are in Game.ResponseWindows.cs, the stack in
    /// Game.ResponseStack.cs, the response cards in Game.ResponseCards.cs.
    /// </summary>
    public sealed partial class Game
    {
        private const string TantrumId = "tantrum";
        private const string TagId = "tag-youre-it";
        private const string RubberGlueId = "im-rubber-youre-glue";
        private const string OutOfStockId = "out-of-stock";
        private const string ProfitShareId = "profit-share";

        /// <summary>Advance all pending machinery until the game needs player input.</summary>
        private void Pump(List<GameEvent> events)
        {
            while (PumpOnce(events))
            {
            }
        }

        /// <summary>
        /// Takes the single most urgent step. Returns false when the game is waiting on a
        /// player or is quiescent (normal actions are legal again).
        /// </summary>
        private bool PumpOnce(List<GameEvent> events)
        {
            PruneStaleDecisions();

            bool isTimeoutSettled = State.TimeoutDrawerId != null && State.PendingDecisions.Count == 0;
            if (isTimeoutSettled)
            {
                FinishTimeout(events);
                return true;
            }
            if (State.PendingDecisions.Count > 0)
            {
                return false;
            }
            if (State.ResponseStack.Count > 0)
            {
                return AdvanceStack(events);
            }
            // The stack has fully unwound: Whiniest Baby settles before anything else proceeds.
            if (State.EpisodeHadTantrums)
            {
                State.EpisodeHadTantrums = false;
                ReassignWhiniestBaby(events);
                return true;
            }
            if (State.PendingDraws.Count > 0)
            {
                ProcessPendingDraws(events);
                return true;
            }
            if (State.TheftQueue.Count > 0)
            {
                return CloseUnsplitTheft(events);
            }
            if (State.PendingRoll != null)
            {
                return FinalizeRollOnceUnanswered(events);
            }
            if (State.PostRollContinuation is RollPurpose purpose)
            {
                ContinueAfterRoll(purpose, events);
                return true;
            }
            if (State.TurnStartInProgress)
            {
                AdvanceTurnStart(events);
                return true;
            }
            return false;
        }

        /// <summary>Returns false while the top item waits on a window or a retarget decision.</summary>
        private bool AdvanceStack(List<GameEvent> events)
        {
            var top = TopStackItem();
            if (top.Cancelled)
            {
                PopCancelled(top, events);
                return true;
            }
            return EnsureWindow(events) && ResolveTopItem(events);
        }

        /// <summary>The robbed player's Profit Share window closed without a split.</summary>
        private bool CloseUnsplitTheft(List<GameEvent> events)
        {
            if (!EnsureWindow(events))
            {
                return false;
            }
            State.TheftQueue.RemoveAt(0);
            BumpRevision();
            return true;
        }

        private bool FinalizeRollOnceUnanswered(List<GameEvent> events)
        {
            if (!EnsureWindow(events))
            {
                return false;
            }
            FinalizePendingRoll(events);
            return true;
        }

        /// <summary>Only a turn's sale roll and Trade Winds carry on into ending the turn.</summary>
        private void ContinueAfterRoll(RollPurpose purpose, List<GameEvent> events)
        {
            State.PostRollContinuation = null;
            if (purpose == RollPurpose.TurnSale || purpose == RollPurpose.TradeWinds)
            {
                FinishEndTurn(events);
            }
        }

        private void BumpRevision() => State.InteractionRevision++;

        // --------------------------------------------------- stale decisions

        /// <summary>
        /// Concurrent decisions can invalidate each other (two steal abilities racing over
        /// the same near-empty hand): drop the ones that can no longer be answered.
        /// </summary>
        private void PruneStaleDecisions()
        {
            for (int i = State.PendingDecisions.Count - 1; i >= 0; i--)
            {
                if (!TrimToAnswerable(State.PendingDecisions[i]))
                {
                    State.PendingDecisions.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Shrinks a decision to what its player can still give. Returns false when nothing
        /// is left to answer.
        /// </summary>
        private bool TrimToAnswerable(PendingDecision decision)
        {
            var player = Player(decision.PlayerId);
            switch (decision.Kind)
            {
                case DecisionKind.AbilityPickCard:
                    return Player(decision.ChosenPlayerId!.Value).Hand.Count > 0;
                case DecisionKind.AbilityGiveBack:
                    return player.Hand.Any(id => id != decision.StolenCardId);
                case DecisionKind.DiscardToHandLimit:
                    return ClampRequiredCount(decision, player.Hand.Count);
                case DecisionKind.AbilityDiscard:
                case DecisionKind.WhiniestBabyDiscard:
                    // Only the drawn cards qualify. Forget any that left the hand since (a
                    // Timeout discard answered first, a steal), or the decision would
                    // offer cards that are gone and reject its own moves.
                    decision.EligibleCardIds = decision.EligibleCardIds?
                        .Where(player.Hand.Contains).ToList();
                    return ClampRequiredCount(decision, decision.EligibleCardIds?.Count ?? player.Hand.Count);
                case DecisionKind.WordOfMouthStand:
                    return player.Stands.Count > 0;
                case DecisionKind.InnovationCopy:
                    return HasPourCardToCopy(player, decision.SourceInstanceId);
                default:
                    return true;
            }
        }

        /// <summary>Returns false when the player has nothing left to give.</summary>
        private static bool ClampRequiredCount(PendingDecision decision, int availableCount)
        {
            if (availableCount >= decision.RequiredCount)
            {
                return true;
            }
            decision.RequiredCount = availableCount;
            return availableCount > 0;
        }
    }
}
