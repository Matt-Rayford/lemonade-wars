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

        private void ApplySubmitTimeoutPayment(SubmitTimeoutPayment action, List<GameEvent> events)
        {
            var decision = RequireDecision(action.PlayerId, DecisionKind.TimeoutFine);
            var player = Player(action.PlayerId);

            // Sell listed assets at full base price. Black Market cards go FIRST: selling a
            // stand discards its remaining equips uncompensated, so a payment listing both
            // an equip and its stand must cash the equip in before the stand goes.
            foreach (int bmId in action.SellBmInstanceIds.Distinct())
            {
                bool fromStand = player.Stands.Any(s => s.Equipped.Remove(bmId));
                bool fromTurf = !fromStand && player.Turf.Equipped.Remove(bmId);
                if (!fromStand && !fromTurf)
                {
                    throw new InvalidActionException($"No equipped Black Market card {bmId}.");
                }
                var def = Db.BlackMarket(State.BlackMarketInstances[bmId].DefId);
                State.BlackMarketDiscard.Add(bmId);
                player.Money += def.Cost;
                events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = def.Cost, Reason = "sold card" });
            }
            foreach (int standId in action.SellStandInstanceIds.Distinct())
            {
                var stand = player.Stands.FirstOrDefault(s => s.InstanceId == standId)
                    ?? throw new InvalidActionException($"No owned stand {standId}.");
                var type = Db.StandType(stand.StandTypeId);
                // Equipped cards still on a sold stand are discarded with it.
                foreach (int equipped in stand.Equipped)
                {
                    State.BlackMarketDiscard.Add(equipped);
                }
                player.Stands.Remove(stand);
                State.StandSupply[stand.StandTypeId].Add(stand.Shape); // bottom of the stack
                player.Money += type.BaseCost;
                events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = type.BaseCost, Reason = "sold stand" });
            }

            int owed = decision.RequiredMoney;
            bool bankrupt = player.Money < owed &&
                player.Stands.Count == 0 &&
                player.Turf.Equipped.Count == 0;
            if (player.Money < owed && !bankrupt)
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

        private void ApplySkipFreePlay(SkipFreePlay action, List<GameEvent> events)
        {
            var decision = State.PendingDecisions.FirstOrDefault(d =>
                d.PlayerId == action.PlayerId &&
                (d.Kind == DecisionKind.FreePlayOffer || d.Kind == DecisionKind.BouncerAttack ||
                 d.Kind == DecisionKind.ForcedPlay))
                ?? throw new InvalidActionException($"P{action.PlayerId} has no optional play to skip.");

            if (decision.Kind == DecisionKind.ForcedPlay)
            {
                // "Play it immediately" is mandatory — skippable only when the recovered
                // card has no legal parameterization (e.g. That's Not Fair! with no
                // opposing equips). The card then stays in hand.
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
