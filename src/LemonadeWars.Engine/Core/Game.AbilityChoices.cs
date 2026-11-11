using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    public sealed partial class Game
    {
        private static bool IsAbilityDecision(DecisionKind kind) =>
            kind == DecisionKind.AbilityVictim ||
            kind == DecisionKind.AbilityPickCard ||
            kind == DecisionKind.AbilityGiveBack ||
            kind == DecisionKind.AbilityDiscard ||
            kind == DecisionKind.InnovationCopy ||
            kind == DecisionKind.WordOfMouthStand;

        private void ApplySubmitAbilityChoice(SubmitAbilityChoice action, List<GameEvent> events)
        {
            var decision = State.PendingDecisions.FirstOrDefault(d =>
                    d.PlayerId == action.PlayerId && IsAbilityDecision(d.Kind))
                ?? throw new InvalidActionException($"P{action.PlayerId} has no pending ability decision.");

            var owner = Player(action.PlayerId);
            switch (decision.Kind)
            {
                case DecisionKind.AbilityVictim:
                    ResolveVictimChoice(decision, owner, action, events);
                    break;
                case DecisionKind.AbilityPickCard:
                    TakePickedCard(decision, owner, action, events);
                    break;
                case DecisionKind.AbilityGiveBack:
                    GiveCardBack(decision, owner, action);
                    break;
                case DecisionKind.AbilityDiscard:
                    DiscardDrawnCards(decision, owner, action, events);
                    break;
                case DecisionKind.InnovationCopy:
                    CopyPourCard(decision, owner, action, events);
                    break;
                case DecisionKind.WordOfMouthStand:
                    SellChosenStand(decision, owner, action, events);
                    break;
            }

            Pump(events);
        }

        // ----------------------------------------------------- victim picks

        private void ResolveVictimChoice(PendingDecision decision, PlayerState owner, SubmitAbilityChoice action, List<GameEvent> events)
        {
            var def = EquippedDef(decision.SourceInstanceId!.Value);
            if (action.Decline)
            {
                State.PendingDecisions.Remove(decision);
                events.Add(new AbilityDeclined { PlayerId = owner.PlayerId, DefId = def.Id });
                return;
            }
            var victim = RequireOtherPlayer(action.TargetPlayerId, owner.PlayerId);
            State.PendingDecisions.Remove(decision);

            switch (def.Id)
            {
                case BlackMarketCardIds.JuiceBoxJoey:
                case BlackMarketCardIds.GoneFishin:
                    TriggerSteal(victim, owner, 1, def.Name, events);
                    break;
                case BlackMarketCardIds.TwoBitTimmy:
                    TriggerSteal(victim, owner, 2, def.Name, events);
                    break;
                case BlackMarketCardIds.NapTimeNino:
                case BlackMarketCardIds.HalfPintHarry:
                    StealRandomCardThenGiveBack(owner, victim, decision.SourceInstanceId, events);
                    break;
                case BlackMarketCardIds.FamilyBusiness:
                    TriggerSteal(victim, owner, 1, def.Name, events);
                    StealRandomCardThenGiveBack(owner, victim, decision.SourceInstanceId, events);
                    break;
                case BlackMarketCardIds.DonsBlessings:
                    if (victim.Hand.Count > 0)
                    {
                        AddDecision(new PendingDecision
                        {
                            PlayerId = owner.PlayerId,
                            Kind = DecisionKind.AbilityPickCard,
                            SourceInstanceId = decision.SourceInstanceId,
                            ChosenPlayerId = victim.PlayerId,
                        }, events);
                    }
                    break;
                default:
                    throw new InvalidActionException($"No victim handler for '{def.Id}'.");
            }
        }

        private void StealRandomCardThenGiveBack(PlayerState owner, PlayerState victim, int? sourceInstanceId, List<GameEvent> events)
        {
            if (StealRandomCard(victim, owner, events) is int stolenId)
            {
                MaybeGiveBack(owner, victim, stolenId, sourceInstanceId, events);
            }
        }

        /// <summary>"...then give that player a different Lemon card" — only if the owner has one.</summary>
        private void MaybeGiveBack(PlayerState owner, PlayerState victim, int stolenCardId, int? sourceInstanceId, List<GameEvent> events)
        {
            if (owner.Hand.Any(id => id != stolenCardId))
            {
                AddDecision(new PendingDecision
                {
                    PlayerId = owner.PlayerId,
                    Kind = DecisionKind.AbilityGiveBack,
                    SourceInstanceId = sourceInstanceId,
                    ChosenPlayerId = victim.PlayerId,
                    StolenCardId = stolenCardId,
                }, events);
            }
        }

        // ------------------------------------------------------- card picks

        /// <summary>Don's Blessings: the owner picks from the victim's revealed hand.</summary>
        private void TakePickedCard(PendingDecision decision, PlayerState owner, SubmitAbilityChoice action, List<GameEvent> events)
        {
            var victim = Player(decision.ChosenPlayerId!.Value);
            if (action.CardInstanceIds.Count != 1 || !victim.Hand.Contains(action.CardInstanceIds[0]))
            {
                throw new InvalidActionException("Pick exactly one card from the victim's hand.");
            }
            int stolenCardId = action.CardInstanceIds[0];
            victim.Hand.Remove(stolenCardId);
            owner.Hand.Add(stolenCardId);
            StatsFor(owner.PlayerId).CardsStolen++;
            events.Add(new CardsStolen { FromPlayerId = victim.PlayerId, ToPlayerId = owner.PlayerId, Count = 1 });
            State.PendingDecisions.Remove(decision);
            MaybeGiveBack(owner, victim, stolenCardId, decision.SourceInstanceId, events);
        }

        private void GiveCardBack(PendingDecision decision, PlayerState owner, SubmitAbilityChoice action)
        {
            var victim = Player(decision.ChosenPlayerId!.Value);
            if (action.CardInstanceIds.Count != 1 ||
                !owner.Hand.Contains(action.CardInstanceIds[0]) ||
                action.CardInstanceIds[0] == decision.StolenCardId)
            {
                throw new InvalidActionException("Give back a different card from your hand.");
            }
            int givenCardId = action.CardInstanceIds[0];
            owner.Hand.Remove(givenCardId);
            victim.Hand.Add(givenCardId);
            State.PendingDecisions.Remove(decision);
        }

        /// <summary>Whispers of Fate: discard from the cards just drawn.</summary>
        private void DiscardDrawnCards(PendingDecision decision, PlayerState owner, SubmitAbilityChoice action, List<GameEvent> events)
        {
            var discardIds = action.CardInstanceIds;
            var eligible = decision.EligibleCardIds ?? owner.Hand;
            if (discardIds.Count != decision.RequiredCount ||
                discardIds.Distinct().Count() != discardIds.Count ||
                discardIds.Any(id => !owner.Hand.Contains(id) || !eligible.Contains(id)))
            {
                throw new InvalidActionException(
                    $"Discard exactly {decision.RequiredCount} of the cards you just drew.");
            }
            foreach (int id in discardIds)
            {
                owner.Hand.Remove(id);
                State.LemonDiscard.Add(id);
            }
            StatsFor(owner.PlayerId).CardsKept -= discardIds.Count;
            events.Add(new CardsDiscarded { PlayerId = owner.PlayerId, InstanceIds = discardIds.ToList() });
            State.PendingDecisions.Remove(decision);
        }

        // ------------------------------------------- innovation, word of mouth

        private void CopyPourCard(PendingDecision decision, PlayerState owner, SubmitAbilityChoice action, List<GameEvent> events)
        {
            if (!(action.EquippedInstanceId is int copiedId) ||
                !owner.Turf.Equipped.Contains(copiedId) ||
                copiedId == decision.SourceInstanceId)
            {
                throw new InvalidActionException("Innovation: choose another of your Power Pour cards.");
            }
            var copiedDef = EquippedDef(copiedId);
            if (!IsCopyablePourCard(copiedDef))
            {
                throw new InvalidActionException("Innovation: that card has no Power Pour ability to copy.");
            }
            State.PendingDecisions.Remove(decision);
            TriggerPourCard(owner, copiedId, copiedDef, events);
        }

        private void SellChosenStand(PendingDecision decision, PlayerState owner, SubmitAbilityChoice action, List<GameEvent> events)
        {
            var stand = owner.Stands.FirstOrDefault(candidate => candidate.InstanceId == action.StandInstanceId)
                ?? throw new InvalidActionException("Word of Mouth: choose one of your own Stands.");
            State.PendingDecisions.Remove(decision);
            SellStand(owner, stand, events);
        }
    }
}
