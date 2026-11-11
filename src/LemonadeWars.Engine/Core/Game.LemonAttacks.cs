using System;
using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Resolution handlers for attack Lemon cards, dispatched from ResolveLemonPlay.
    /// Each returns true once the play is finished, or false while it waits on the
    /// attacker to re-pick a stale target (see RequestRetargetOrFizzle). HOA Violation
    /// resolves in the dispatcher itself.
    /// </summary>
    public sealed partial class Game
    {
        private bool ResolveBlameChanger(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            var victim = AttackVictim(item);
            var record = PickTantrum(owner, item.TantrumInstanceId);
            if (record == null)
            {
                return Fizzle(item, events);
            }
            owner.TantrumPile.Remove(record);
            GainTantrum(victim, record.InstanceId, events); // fresh gain: baby check applies
            return true;
        }

        private bool ResolveFindersKeepers(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            var victim = AttackVictim(item);
            if (!EquipStillValid(item, victim, owner))
            {
                return RequestRetargetOrFizzle(item, victim, events);
            }
            int stolenId = item.TargetEquippedInstanceId!.Value;
            RemoveEquipped(victim, stolenId);
            events.Add(new CardsStolen { FromPlayerId = victim.PlayerId, ToPlayerId = owner.PlayerId, Count = 1 });
            EquipOrDiscard(owner, stolenId, item, events);
            return true;
        }

        /// <summary>Finders Keepers: are both the steal target and the equip destination still valid?</summary>
        private bool EquipStillValid(StackItem item, PlayerState victim, PlayerState owner)
        {
            if (!(item.TargetEquippedInstanceId is int equippedId) || !OwnsEquipped(victim, equippedId))
            {
                return false;
            }
            try
            {
                ValidateEquipDestination(owner, EquippedDef(equippedId),
                    item.EquipStandInstanceId, item.EquipReplaceInstanceId);
                return true;
            }
            catch (InvalidActionException)
            {
                return false;
            }
        }

        private bool ResolveSharingIsCaring(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            var victim = AttackVictim(item);
            int halfRoundedUp = (victim.Money + 1) / 2;
            StealMoney(victim, owner, Math.Min(halfRoundedUp, 10), "sharing is caring", events);
            return true;
        }

        private bool ResolveSmearCampaign(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            var victim = AttackVictim(item);
            int steals = Math.Min(2, victim.Hand.Count);
            for (int i = 0; i < steals; i++)
            {
                int handIndex = _rng.Next(victim.Hand.Count);
                int cardId = victim.Hand[handIndex];
                victim.Hand.RemoveAt(handIndex);
                owner.Hand.Add(cardId);
            }
            if (steals > 0)
            {
                events.Add(new CardsStolen { FromPlayerId = victim.PlayerId, ToPlayerId = owner.PlayerId, Count = steals });
            }
            // "Then you may play any card" (designer ruling): the smear gives its action
            // back instead of opening a free-play prompt. A free-played smear (Bouncer)
            // never spent one, so it gets nothing back.
            if (!item.FreePlay)
            {
                State.ActionsRemaining += 1;
            }
            return true;
        }

        private bool ResolveStealTheCashbox(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            var victim = AttackVictim(item);
            if (victim.Turf.TrapInstanceId != null)
            {
                return Fizzle(item, events);
            }
            victim.Turf.TrapInstanceId = item.LemonInstanceId;
            victim.Turf.TrapOwnerId = owner.PlayerId;
            events.Add(new TrapPlaced { OwnerId = owner.PlayerId, OnPlayerId = victim.PlayerId });
            return true;
        }

        private bool ResolveTaxes(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            var victim = AttackVictim(item);
            StealMoney(victim, owner, Math.Min(2 * victim.Stands.Count, 10), "taxes", events);
            return true;
        }

        private bool ResolveThatsNotFair(StackItem item, List<GameEvent> events)
        {
            var victim = AttackVictim(item);
            if (!(item.TargetEquippedInstanceId is int equippedId) || !OwnsEquipped(victim, equippedId))
            {
                return RequestRetargetOrFizzle(item, victim, events);
            }
            RemoveEquipped(victim, equippedId);
            State.BlackMarketDiscard.Add(equippedId);
            events.Add(new CardsDiscarded { PlayerId = victim.PlayerId, InstanceIds = new List<int> { equippedId } });
            return true;
        }

        private bool ResolveTrashPandas(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            var victim = AttackVictim(item);
            var ownerHand = owner.Hand.ToList();
            owner.Hand.Clear();
            owner.Hand.AddRange(victim.Hand);
            victim.Hand.Clear();
            victim.Hand.AddRange(ownerHand);
            events.Add(new HandsTraded { PlayerA = owner.PlayerId, PlayerB = victim.PlayerId });
            return true;
        }
    }
}
