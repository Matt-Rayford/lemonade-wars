using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Submit actions that answer blocking <see cref="PendingDecision"/>s shared across
    /// subsystems: forced discards, the Timeout fine, and skipping optional free plays.
    /// Card-specific decision answers live with their cards (SubmitRetarget in
    /// Game.LemonEffects.cs, SubmitAbilityChoice in Game.BlackMarket.cs).
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

            // Black Market cards go FIRST: selling a stand discards its remaining equips
            // uncompensated, so an equip listed with its stand must be cashed in before
            // the stand goes.
            foreach (int bmId in action.SellBmInstanceIds.Distinct())
            {
                SellEquippedCardAtCost(player, bmId, events);
            }
            foreach (int standId in action.SellStandInstanceIds.Distinct())
            {
                SellStandAtBaseCost(player, standId, events);
            }

            int owed = decision.RequiredMoney;
            bool isBankrupt = player.Money < owed &&
                player.Stands.Count == 0 &&
                player.Turf.Equipped.Count == 0;
            if (player.Money < owed && !isBankrupt)
            {
                throw new InvalidActionException(
                    $"Fine is ${owed}; sell enough assets to cover it (have ${player.Money}).");
            }

            int paid = System.Math.Min(owed, player.Money);
            player.Money -= paid;
            events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = -paid, Reason = "timeout fine" });

            State.PendingDecisions.Remove(decision);
            Pump(events);
        }

        private void SellEquippedCardAtCost(PlayerState player, int bmInstanceId, List<GameEvent> events)
        {
            bool wasOnStand = player.Stands.Any(s => s.Equipped.Remove(bmInstanceId));
            bool wasOnTurf = !wasOnStand && player.Turf.Equipped.Remove(bmInstanceId);
            if (!wasOnStand && !wasOnTurf)
            {
                throw new InvalidActionException($"No equipped Black Market card {bmInstanceId}.");
            }
            var def = Db.BlackMarket(State.BlackMarketInstances[bmInstanceId].DefId);
            State.BlackMarketDiscard.Add(bmInstanceId);
            player.Money += def.Cost;
            events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = def.Cost, Reason = "sold card" });
        }

        private void SellStandAtBaseCost(PlayerState player, int standInstanceId, List<GameEvent> events)
        {
            var stand = player.Stands.FirstOrDefault(s => s.InstanceId == standInstanceId)
                ?? throw new InvalidActionException($"No owned stand {standInstanceId}.");
            var standType = Db.StandType(stand.StandTypeId);
            // Cards still equipped on a sold stand are discarded with it.
            foreach (int equipped in stand.Equipped)
            {
                State.BlackMarketDiscard.Add(equipped);
            }
            player.Stands.Remove(stand);
            State.StandSupply[stand.StandTypeId].Add(stand.Shape); // bottom of the stack
            player.Money += standType.BaseCost;
            events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = standType.BaseCost, Reason = "sold stand" });
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
                // "Play it immediately" is mandatory: skippable only when the recovered card
                // has no legal parameterization (e.g. That's Not Fair! with no opposing
                // equips). The card then stays in hand.
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
