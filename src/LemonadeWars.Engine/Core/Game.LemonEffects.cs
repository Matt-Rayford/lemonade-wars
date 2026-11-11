using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Playing a plan or attack Lemon card: the submit path, the per-card resolution
    /// dispatch, attack retargeting, and the equip/theft helpers the cards share.
    /// Submit-time validation is in Game.LemonValidation.cs; the card handlers are in
    /// Game.LemonPlans.cs and Game.LemonAttacks.cs. Instants are window responses
    /// (Game.ResponseCards.cs).
    /// </summary>
    public sealed partial class Game
    {
        // ------------------------------------------------------------ entry

        private void ApplyPlayLemonCard(PlayLemonCard action, List<GameEvent> events)
        {
            var player = Player(action.PlayerId);
            var freePlay = FreePlayDecisionFor(action.PlayerId);

            if (freePlay == null)
            {
                RequireTurnAction(action);
            }
            else if (freePlay.Kind == DecisionKind.ForcedPlay &&
                     freePlay.CardInstanceId != action.CardInstanceId)
            {
                throw new InvalidActionException("Reverse Engineer: you must play the recovered card.");
            }

            if (!player.Hand.Contains(action.CardInstanceId))
            {
                throw new InvalidActionException("That card is not in your hand.");
            }
            var def = Db.Lemon(State.LemonInstances[action.CardInstanceId].DefId);
            if (def.Type != LemonCardType.Plan && def.Type != LemonCardType.Attack)
            {
                throw new InvalidActionException(
                    $"{def.Name} is an {def.Type}; instants respond through windows.");
            }
            if (freePlay != null && freePlay.Kind == DecisionKind.BouncerAttack &&
                def.Type != LemonCardType.Attack)
            {
                throw new InvalidActionException("Bouncer: the free play must be an attack.");
            }

            ValidatePlayParams(def, action, player);

            if (freePlay == null)
            {
                SpendAction();
            }
            else
            {
                State.PendingDecisions.Remove(freePlay);
            }
            player.Hand.Remove(action.CardInstanceId);

            int? attackTarget = def.Type == LemonCardType.Attack
                ? ResolveDeclaredTarget(def.Id, action)
                : (int?)null;

            PushStackItem(
                LemonPlayStackItem(player, action, def, isFreePlay: freePlay != null, attackTarget),
                events);
            Pump(events);
        }

        /// <summary>
        /// The free play this player is answering, if any: a free-play offer, Reverse
        /// Engineer's forced play, or Bouncer's counter-attack. Free plays skip the phase
        /// and action checks.
        /// </summary>
        private PendingDecision? FreePlayDecisionFor(int playerId) =>
            State.PendingDecisions.FirstOrDefault(d =>
                d.PlayerId == playerId &&
                (d.Kind == DecisionKind.FreePlayOffer || d.Kind == DecisionKind.ForcedPlay ||
                 d.Kind == DecisionKind.BouncerAttack));

        private static StackItem LemonPlayStackItem(
            PlayerState owner, PlayLemonCard action, LemonCardDef def, bool isFreePlay, int? attackTarget)
        {
            return new StackItem
            {
                Kind = StackItemKind.LemonPlay,
                OwnerId = owner.PlayerId,
                LemonInstanceId = action.CardInstanceId,
                LemonDefId = def.Id,
                FreePlay = isFreePlay,
                AttackTargetId = attackTarget,
                OriginalTargetId = attackTarget,
                TargetStandInstanceId = action.TargetStandInstanceId,
                TargetEquippedInstanceId = action.TargetEquippedInstanceId,
                TantrumInstanceId = action.TantrumInstanceId,
                MarketIndex = action.MarketIndex,
                DiscardedBmInstanceId = action.DiscardedBmInstanceId,
                DiscardedLemonInstanceId = action.DiscardedLemonInstanceId,
                DrawInstead = action.DrawInstead,
                NewStandTypeId = action.NewStandTypeId,
                SelectedInstanceIds = action.SelectedInstanceIds.ToList(),
                EquipStandInstanceId = action.EquipStandInstanceId,
                EquipReplaceInstanceId = action.EquipReplaceInstanceId,
            };
        }

        /// <summary>Attacks name a victim; That's Not Fair! derives it from the chosen card.</summary>
        private int ResolveDeclaredTarget(string defId, PlayLemonCard action)
        {
            if (defId == "thats-not-fair")
            {
                return FindEquipOwner(action.TargetEquippedInstanceId!.Value).PlayerId;
            }
            if (!(action.TargetPlayerId is int target) || target < 0 || target >= State.Players.Count)
            {
                throw new InvalidActionException("This attack needs a target player.");
            }
            if (target == action.PlayerId)
            {
                throw new InvalidActionException("You cannot attack yourself.");
            }
            return target;
        }

        // ------------------------------------------------------- resolution

        /// <summary>
        /// Resolve a plan/attack. Returns false when paused for an AttackRetarget decision
        /// (attack was Tagged/reflected and its specifics went stale — the attacker re-picks).
        /// </summary>
        private bool ResolveLemonPlay(StackItem item, List<GameEvent> events)
        {
            var owner = Player(item.OwnerId);
            switch (item.LemonDefId)
            {
                case "apologize": return ResolveApologize(item, owner);
                case "automation":
                    State.ActionsRemaining += 2;
                    return true;
                case "blame-changer": return ResolveBlameChanger(item, owner, events);
                case "connections": return ResolveConnections(item, owner, events);
                case "doorbuster-sale": return ResolveDoorbusterSale(item, owner, events);
                case "finders-keepers": return ResolveFindersKeepers(item, owner, events);
                case "hoa-violation":
                    StealMoney(AttackVictim(item), owner, 5, "hoa violation", events);
                    return true;
                case "market-forecasting":
                    QueueDraws(owner.PlayerId, 3);
                    return true;
                case "night-shifts":
                    OpenRollWindow(RollPurpose.NightShifts, owner.PlayerId, events);
                    return true;
                case "rebrand": return ResolveRebrand(item, owner, events);
                case "reduce-and-reuse": return ResolveReduceAndReuse(item, owner, events);
                case "reverse-engineer": return ResolveReverseEngineer(item, owner, events);
                case "rummage-sale": return ResolveRummageSale(item, owner, events);
                case "sharing-is-caring": return ResolveSharingIsCaring(item, owner, events);
                case "smear-campaign": return ResolveSmearCampaign(item, owner, events);
                case "steal-the-cashbox": return ResolveStealTheCashbox(item, owner, events);
                case "taxes": return ResolveTaxes(item, owner, events);
                case "thats-not-fair": return ResolveThatsNotFair(item, events);
                case "trash-pandas": return ResolveTrashPandas(item, owner, events);
                default:
                    throw new InvalidActionException($"No resolution handler for '{item.LemonDefId}'.");
            }
        }

        private PlayerState AttackVictim(StackItem item) => Player(item.AttackTargetId!.Value);

        /// <summary>The play resolves with no effect; its card still goes to the discard.</summary>
        private bool Fizzle(StackItem item, List<GameEvent> events)
        {
            events.Add(new AttackFizzled { OwnerId = item.OwnerId, DefId = item.LemonDefId });
            return true;
        }

        // ------------------------------------------------ attack retargeting

        /// <summary>
        /// The attack's specifics are stale (it was Tagged/reflected). If the new victim has
        /// nothing valid, the attack fizzles; otherwise the attacker re-picks (designer ruling).
        /// Returns false while the re-pick is pending: the item stays on the stack until
        /// SubmitRetarget.
        /// </summary>
        private bool RequestRetargetOrFizzle(StackItem item, PlayerState victim, List<GameEvent> events)
        {
            if (!HasAnyEquipped(victim))
            {
                return Fizzle(item, events);
            }
            // Finders Keepers also needs somewhere to PUT a stolen card — with no valid
            // destination for any of the victim's cards, the retarget would deadlock.
            if (item.LemonDefId == "finders-keepers")
            {
                var owner = Player(item.OwnerId);
                bool anyDestination = AllEquipped(victim)
                    .Any(equippedId => DestinationsFor(owner, EquippedDef(equippedId)).Any());
                if (!anyDestination)
                {
                    return Fizzle(item, events);
                }
            }
            State.PendingDecisions.Add(new PendingDecision
            {
                PlayerId = item.OwnerId,
                Kind = DecisionKind.AttackRetarget,
                StackItemId = item.ItemId,
            });
            events.Add(new DecisionRequired { PlayerId = item.OwnerId, Kind = DecisionKind.AttackRetarget });
            return false;
        }

        private void ApplySubmitRetarget(SubmitRetarget action, List<GameEvent> events)
        {
            var decision = RequireDecision(action.PlayerId, DecisionKind.AttackRetarget);
            var item = FindStackItem(decision.StackItemId)
                ?? throw new InvalidActionException("The attack is no longer pending.");
            var victim = AttackVictim(item);

            if (!(action.TargetEquippedInstanceId is int equippedId) || !OwnsEquipped(victim, equippedId))
            {
                throw new InvalidActionException("Choose a card equipped by the attack's current target.");
            }
            item.TargetEquippedInstanceId = equippedId;
            item.EquipStandInstanceId = action.EquipStandInstanceId;
            item.EquipReplaceInstanceId = action.EquipReplaceInstanceId;

            if (item.LemonDefId == "finders-keepers")
            {
                ValidateEquipDestination(Player(item.OwnerId), EquippedDef(equippedId),
                    action.EquipStandInstanceId, action.EquipReplaceInstanceId);
            }

            State.PendingDecisions.Remove(decision);
            Pump(events);
        }

        // --------------------------------------------------- shared helpers

        private PlayerState FindEquipOwner(int equippedInstanceId)
        {
            foreach (var player in State.Players)
            {
                if (OwnsEquipped(player, equippedInstanceId))
                {
                    return player;
                }
            }
            throw new InvalidActionException($"Black Market card {equippedInstanceId} is not equipped anywhere.");
        }

        private bool OwnsEquipped(PlayerState player, int instanceId) =>
            player.Turf.Equipped.Contains(instanceId) ||
            player.Stands.Any(s => s.Equipped.Contains(instanceId));

        private bool HasAnyEquipped(PlayerState player) =>
            player.Turf.Equipped.Count > 0 || player.Stands.Any(s => s.Equipped.Count > 0);

        private void RemoveEquipped(PlayerState player, int instanceId)
        {
            if (!player.Turf.Equipped.Remove(instanceId))
            {
                foreach (var stand in player.Stands)
                {
                    if (stand.Equipped.Remove(instanceId))
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Equip a Black Market instance on the player's board, discarding the replaced
        /// card when the destination is full. False, with nothing changed, when the
        /// destination is gone or is full with no valid card to replace.
        /// </summary>
        private bool TryEquip(PlayerState player, int bmInstanceId, int? standInstanceId, int? replaceId,
            List<GameEvent> events, int pricePaid = 0)
        {
            var def = EquippedDef(bmInstanceId);
            List<int> equipped;
            int slotLimit;
            if (def.Target == EquipTarget.Stand)
            {
                var stand = player.Stands.FirstOrDefault(s => s.InstanceId == standInstanceId);
                if (stand == null)
                {
                    return false;
                }
                equipped = stand.Equipped;
                slotLimit = Db.StandType(stand.StandTypeId).UpgradeSlots;
            }
            else
            {
                equipped = player.Turf.Equipped;
                slotLimit = Db.Turf.UpgradeSlots;
            }

            if (equipped.Count >= slotLimit)
            {
                if (!(replaceId is int replacedId) || !equipped.Remove(replacedId))
                {
                    return false;
                }
                State.BlackMarketDiscard.Add(replacedId);
                events.Add(new CardsDiscarded
                {
                    PlayerId = player.PlayerId,
                    InstanceIds = new List<int> { replacedId },
                });
            }

            equipped.Add(bmInstanceId);
            events.Add(new BlackMarketPurchased
            {
                PlayerId = player.PlayerId,
                InstanceId = bmInstanceId,
                DefId = def.Id,
                TargetStandInstanceId = def.Target == EquipTarget.Stand ? standInstanceId : null,
                Price = pricePaid,
            });
            return true;
        }

        /// <summary>Equips the card where the play said, or discards it if that destination went stale.</summary>
        private void EquipOrDiscard(PlayerState player, int bmInstanceId, StackItem item, List<GameEvent> events)
        {
            if (!TryEquip(player, bmInstanceId, item.EquipStandInstanceId, item.EquipReplaceInstanceId, events))
            {
                State.BlackMarketDiscard.Add(bmInstanceId);
            }
        }

        /// <summary>Transfer money capped by what the victim has; opens the Profit Share window.</summary>
        private void StealMoney(PlayerState from, PlayerState to, int amount, string reason, List<GameEvent> events)
        {
            int actual = Math.Min(amount, from.Money);
            if (actual <= 0)
            {
                return;
            }
            from.Money -= actual;
            to.Money += actual;
            events.Add(new MoneyStolen
            {
                FromPlayerId = from.PlayerId,
                ToPlayerId = to.PlayerId,
                Amount = actual,
                Reason = reason,
            });
            OpenTheftWindow(from.PlayerId, to.PlayerId, actual);
        }
    }
}
