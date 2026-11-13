using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Answers to the decisions no single card owns: forced discards, the Timeout fine,
    /// skipping a free play. Card-specific answers live with their cards.
    /// </summary>
    public sealed partial class Game
    {
        private PendingDecision RequireDecision(int playerId, DecisionKind kind)
        {
            var decision = State.PendingDecisions.FirstOrDefault(
                d => d.PlayerId == playerId && d.Kind == kind);
            if (decision == null)
            {
                throw new InvalidActionException($"P{playerId} has no pending {kind} decision.");
            }
            return decision;
        }

        // --------------------------------------------------------- discards

        private void ApplySubmitDiscard(SubmitDiscard action, List<GameEvent> events)
        {
            var decision = State.PendingDecisions.FirstOrDefault(d =>
                d.PlayerId == action.PlayerId &&
                (d.Kind == DecisionKind.DiscardToHandLimit || d.Kind == DecisionKind.WhiniestBabyDiscard));
            if (decision == null)
            {
                throw new InvalidActionException($"P{action.PlayerId} has no pending discard.");
            }

            var player = Player(action.PlayerId);
            if (action.InstanceIds.Count != decision.RequiredCount ||
                action.InstanceIds.Distinct().Count() != action.InstanceIds.Count ||
                action.InstanceIds.Any(id => !player.Hand.Contains(id)))
            {
                throw new InvalidActionException(
                    $"Must discard exactly {decision.RequiredCount} distinct card(s) from your hand.");
            }
            if (decision.EligibleCardIds != null &&
                action.InstanceIds.Any(id => !decision.EligibleCardIds.Contains(id)))
            {
                throw new InvalidActionException(
                    "Whiniest Baby must discard one of the cards just drawn.");
            }

            foreach (int id in action.InstanceIds)
            {
                player.Hand.Remove(id);
                State.LemonDiscard.Add(id);
            }
            events.Add(new CardsDiscarded { PlayerId = player.PlayerId, InstanceIds = action.InstanceIds.ToList() });

            State.PendingDecisions.Remove(decision);
            Pump(events);
        }

        // ----------------------------------------------------- timeout fine

        private void ApplySubmitTimeoutPayment(SubmitTimeoutPayment action, List<GameEvent> events)
        {
            var decision = RequireDecision(action.PlayerId, DecisionKind.TimeoutFine);
            var player = Player(action.PlayerId);
            int owed = decision.RequiredMoney;
            int cashBeforeSales = player.Money;
            var soldCardIds = action.SellBmInstanceIds.Distinct().ToList();
            var soldStandIds = action.SellStandInstanceIds.Distinct().ToList();
            bool isSelling = soldCardIds.Count + soldStandIds.Count > 0;
            if (isSelling && cashBeforeSales >= owed)
            {
                throw new InvalidActionException($"You can pay the ${owed} fine; nothing needs selling.");
            }

            // Black Market cards go FIRST: selling a stand discards its remaining equips
            // uncompensated, so an equip listed with its stand is cashed in before it.
            int raised = cashBeforeSales;
            foreach (int bmId in soldCardIds)
            {
                raised += SellEquippedCardAtCost(player, bmId);
            }
            foreach (int standId in soldStandIds)
            {
                raised += SellStandAtBaseCost(player, standId);
            }

            bool isBankrupt = raised < owed &&
                player.Stands.Count == 0 &&
                player.Turf.Equipped.Count == 0;
            if (raised < owed && !isBankrupt)
            {
                throw new InvalidActionException(
                    $"Fine is ${owed}; sell enough assets to cover it (have ${raised}).");
            }

            // Rulebook, Timeout: "They do not get a refund if they sell more than is needed to
            // pay", so whoever sells ends on $0 and the bar only ever sees the cash they lost.
            int cashAfter = isSelling ? 0 : cashBeforeSales - owed;
            player.Money = cashAfter;
            events.Add(new MoneyChanged
            {
                PlayerId = player.PlayerId,
                Amount = cashAfter - cashBeforeSales,
                Reason = "timeout fine",
            });

            State.PendingDecisions.Remove(decision);
            Pump(events);
        }

        /// <summary>Returns the card's base price; the caller settles the money.</summary>
        private int SellEquippedCardAtCost(PlayerState player, int bmInstanceId)
        {
            bool wasOnStand = player.Stands.Any(s => s.Equipped.Remove(bmInstanceId));
            bool wasOnTurf = !wasOnStand && player.Turf.Equipped.Remove(bmInstanceId);
            if (!wasOnStand && !wasOnTurf)
            {
                throw new InvalidActionException($"No equipped Black Market card {bmInstanceId}.");
            }
            State.BlackMarketDiscard.Add(bmInstanceId);
            return Db.BlackMarket(State.BlackMarketInstances[bmInstanceId].DefId).Cost;
        }

        /// <summary>Returns the stand's base price; the caller settles the money.</summary>
        private int SellStandAtBaseCost(PlayerState player, int standInstanceId)
        {
            var stand = player.Stands.FirstOrDefault(s => s.InstanceId == standInstanceId)
                ?? throw new InvalidActionException($"No owned stand {standInstanceId}.");
            // Cards still equipped on a sold stand are discarded with it.
            foreach (int equipped in stand.Equipped)
            {
                State.BlackMarketDiscard.Add(equipped);
            }
            player.Stands.Remove(stand);
            State.StandSupply[stand.StandTypeId].Add(stand.Shape); // bottom of the stack
            return Db.StandType(stand.StandTypeId).BaseCost;
        }

        // ------------------------------------------------------- free plays

        private void ApplySkipFreePlay(SkipFreePlay action, List<GameEvent> events)
        {
            var decision = State.PendingDecisions.FirstOrDefault(d =>
                d.PlayerId == action.PlayerId &&
                (d.Kind == DecisionKind.FreePlayOffer || d.Kind == DecisionKind.BouncerAttack ||
                 d.Kind == DecisionKind.ForcedPlay))
                ?? throw new InvalidActionException($"P{action.PlayerId} has no optional play to skip.");

            if (decision.Kind == DecisionKind.ForcedPlay)
            {
                // "Play it immediately" is mandatory: skippable only when the recovered card has no
                // legal play (That's Not Fair! with no opposing equips); it then stays in hand.
                var player = Player(action.PlayerId);
                int cardId = decision.CardInstanceId!.Value;
                var def = Db.Lemon(State.LemonInstances[cardId].DefId);
                if (PlayParamCombos(player, cardId, def).Any())
                {
                    throw new InvalidActionException("Reverse Engineer: the recovered card must be played.");
                }
            }

            State.PendingDecisions.Remove(decision);
            Pump(events);
        }
    }
}
