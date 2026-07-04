using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Per-card behavior for plan and attack Lemon cards: submit-time validation and
    /// resolution handlers. Instants live in Game.Responses.cs (they are window responses).
    /// </summary>
    public sealed partial class Game
    {
        // ------------------------------------------------------------ entry

        private void ApplyPlayLemonCard(PlayLemonCard action, List<GameEvent> events)
        {
            var player = Player(action.PlayerId);

            // Free-play contexts (Smear Campaign follow-up, Reverse Engineer forced play,
            // Bouncer counter-attack) bypass the phase/action checks.
            var freePlay = State.PendingDecisions.FirstOrDefault(d =>
                d.PlayerId == action.PlayerId &&
                (d.Kind == DecisionKind.FreePlayOffer || d.Kind == DecisionKind.ForcedPlay ||
                 d.Kind == DecisionKind.BouncerAttack));

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

            PushStackItem(new StackItem
            {
                Kind = StackItemKind.LemonPlay,
                OwnerId = player.PlayerId,
                LemonInstanceId = action.CardInstanceId,
                LemonDefId = def.Id,
                FreePlay = freePlay != null,
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
            }, events);

            Pump(events);
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

        private PlayerState FindEquipOwner(int equippedInstanceId)
        {
            foreach (var p in State.Players)
            {
                if (p.Turf.Equipped.Contains(equippedInstanceId) ||
                    p.Stands.Any(s => s.Equipped.Contains(equippedInstanceId)))
                {
                    return p;
                }
            }
            throw new InvalidActionException($"Black Market card {equippedInstanceId} is not equipped anywhere.");
        }

        // ------------------------------------------------------- validation

        private void ValidatePlayParams(LemonCardDef def, PlayLemonCard action, PlayerState player)
        {
            switch (def.Id)
            {
                case "apologize":
                case "blame-changer":
                    if (!(action.TantrumInstanceId is int tid) ||
                        player.TantrumPile.All(t => t.InstanceId != tid))
                    {
                        throw new InvalidActionException($"{def.Name}: choose a tantrum from your own pile.");
                    }
                    break;

                case "connections":
                {
                    if (!(action.MarketIndex is int mi) || mi < 0 || mi >= State.Market.Count)
                    {
                        throw new InvalidActionException("Connections: choose a face-up market card.");
                    }
                    var bmDef = Db.BlackMarket(State.BlackMarketInstances[State.Market[mi]].DefId);
                    ValidateEquipDestination(player, bmDef, action.EquipStandInstanceId, action.EquipReplaceInstanceId);
                    break;
                }

                case "doorbuster-sale":
                case "rebrand":
                {
                    var stand = player.Stands.FirstOrDefault(s => s.InstanceId == action.TargetStandInstanceId)
                        ?? throw new InvalidActionException($"{def.Name}: choose one of your own Stands.");
                    if (def.Id == "rebrand")
                    {
                        ValidateRebrand(action, stand);
                    }
                    break;
                }

                case "finders-keepers":
                {
                    var victim = RequireOtherPlayer(action.TargetPlayerId, player.PlayerId);
                    if (!(action.TargetEquippedInstanceId is int eq) ||
                        FindEquipOwner(eq).PlayerId != victim.PlayerId)
                    {
                        throw new InvalidActionException("Finders Keepers: choose a card equipped by the target.");
                    }
                    var stolenDef = Db.BlackMarket(State.BlackMarketInstances[eq].DefId);
                    ValidateEquipDestination(player, stolenDef, action.EquipStandInstanceId, action.EquipReplaceInstanceId);
                    break;
                }

                case "reduce-and-reuse":
                {
                    if (!(action.DiscardedBmInstanceId is int dbm) || !State.BlackMarketDiscard.Contains(dbm))
                    {
                        throw new InvalidActionException("Reduce and Reuse: choose a card from the Black Market discard.");
                    }
                    var bmDef = Db.BlackMarket(State.BlackMarketInstances[dbm].DefId);
                    ValidateEquipDestination(player, bmDef, action.EquipStandInstanceId, action.EquipReplaceInstanceId);
                    break;
                }

                case "reverse-engineer":
                    if (!action.DrawInstead)
                    {
                        if (!(action.DiscardedLemonInstanceId is int dl) || !State.LemonDiscard.Contains(dl))
                        {
                            throw new InvalidActionException("Reverse Engineer: choose a card from the Lemon discard (or draw 2).");
                        }
                        var recovered = Db.Lemon(State.LemonInstances[dl].DefId);
                        if (recovered.Type != LemonCardType.Plan && recovered.Type != LemonCardType.Attack)
                        {
                            throw new InvalidActionException(
                                "Reverse Engineer: only plans and attacks can be played immediately.");
                        }
                    }
                    break;

                case "rummage-sale":
                {
                    var ids = action.SelectedInstanceIds;
                    if (ids.Count < 1 || ids.Count > 3 || ids.Distinct().Count() != ids.Count)
                    {
                        throw new InvalidActionException("Rummage Sale: choose 1-3 of your equipped cards.");
                    }
                    foreach (int id in ids)
                    {
                        if (FindEquipOwner(id).PlayerId != player.PlayerId)
                        {
                            throw new InvalidActionException("Rummage Sale: you can only sell your own cards.");
                        }
                    }
                    break;
                }

                case "steal-the-cashbox":
                {
                    var victim = RequireOtherPlayer(action.TargetPlayerId, player.PlayerId);
                    if (victim.Turf.TrapInstanceId != null)
                    {
                        throw new InvalidActionException("That Turf already has a trap on it.");
                    }
                    break;
                }

                case "thats-not-fair":
                {
                    if (!(action.TargetEquippedInstanceId is int eq2))
                    {
                        throw new InvalidActionException("That's Not Fair!: choose an equipped Black Market card.");
                    }
                    if (FindEquipOwner(eq2).PlayerId == player.PlayerId)
                    {
                        throw new InvalidActionException("That's Not Fair!: target another player's card.");
                    }
                    break;
                }

                case "hoa-violation":
                case "sharing-is-caring":
                case "smear-campaign":
                case "taxes":
                case "trash-pandas":
                    RequireOtherPlayer(action.TargetPlayerId, player.PlayerId);
                    break;

                case "automation":
                case "market-forecasting":
                case "night-shifts":
                    break;

                default:
                    throw new InvalidActionException($"No handler for lemon card '{def.Id}'.");
            }
        }

        private PlayerState RequireOtherPlayer(int? targetId, int selfId)
        {
            if (!(targetId is int t) || t < 0 || t >= State.Players.Count || t == selfId)
            {
                throw new InvalidActionException("Choose another player as the target.");
            }
            return State.Players[t];
        }

        private void ValidateRebrand(PlayLemonCard action, StandInstance stand)
        {
            if (string.IsNullOrEmpty(action.NewStandTypeId) || action.NewStandTypeId == stand.StandTypeId)
            {
                throw new InvalidActionException("Rebrand: choose a different Stand type.");
            }
            var newType = Db.StandType(action.NewStandTypeId); // throws on unknown id
            if (State.StandSupply[newType.Id].Count == 0)
            {
                throw new InvalidActionException($"Rebrand: no {newType.Name}s left in the supply.");
            }
            int overflow = Math.Max(0, stand.Equipped.Count - newType.UpgradeSlots);
            var discards = action.SelectedInstanceIds;
            if (discards.Count != overflow || discards.Distinct().Count() != discards.Count ||
                discards.Any(id => !stand.Equipped.Contains(id)))
            {
                throw new InvalidActionException(
                    $"Rebrand: discard exactly {overflow} card(s) from that Stand to fit the new limit.");
            }
        }

        /// <summary>Check a Black Market card can be equipped on this player's board.</summary>
        private void ValidateEquipDestination(
            PlayerState player, BlackMarketCardDef def, int? standInstanceId, int? replaceId)
        {
            if (def.Target == EquipTarget.Stand)
            {
                var stand = player.Stands.FirstOrDefault(s => s.InstanceId == standInstanceId)
                    ?? throw new InvalidActionException($"{def.Name} equips to one of your Stands.");
                int limit = Db.StandType(stand.StandTypeId).UpgradeSlots;
                if (stand.Equipped.Count >= limit &&
                    (!(replaceId is int r) || !stand.Equipped.Contains(r)))
                {
                    throw new InvalidActionException(
                        "That Stand is at its limit; choose an equipped card to discard.");
                }
            }
            else
            {
                if (standInstanceId != null)
                {
                    throw new InvalidActionException($"{def.Name} is a Turf upgrade.");
                }
                if (player.Turf.Equipped.Count >= Db.Turf.UpgradeSlots &&
                    (!(replaceId is int r2) || !player.Turf.Equipped.Contains(r2)))
                {
                    throw new InvalidActionException(
                        "Your Turf is at its limit; choose an equipped card to discard.");
                }
            }
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
                case "apologize":
                {
                    var record = owner.TantrumPile.FirstOrDefault(t => t.InstanceId == item.TantrumInstanceId);
                    if (record != null)
                    {
                        owner.TantrumPile.Remove(record);
                        State.LemonDiscard.Add(record.InstanceId);
                    }
                    return true;
                }

                case "automation":
                    State.ActionsRemaining += 2;
                    return true;

                case "blame-changer":
                {
                    var victim = Player(item.AttackTargetId!.Value);
                    var record = owner.TantrumPile.FirstOrDefault(t => t.InstanceId == item.TantrumInstanceId);
                    if (record == null)
                    {
                        return Fizzle(item, events);
                    }
                    owner.TantrumPile.Remove(record);
                    GainTantrum(victim, record.InstanceId, events); // fresh gain: baby check applies
                    return true;
                }

                case "connections":
                {
                    int mi = item.MarketIndex!.Value;
                    if (mi >= State.Market.Count)
                    {
                        return Fizzle(item, events);
                    }
                    int bmId = State.Market[mi];
                    State.Market.RemoveAt(mi);
                    if (!TryEquip(owner, bmId, item.EquipStandInstanceId, item.EquipReplaceInstanceId, events))
                    {
                        State.BlackMarketDiscard.Add(bmId);
                    }
                    RefillMarket();
                    events.Add(new MarketRefilled { Market = State.Market.ToList() });
                    return true;
                }

                case "doorbuster-sale":
                {
                    var stand = owner.Stands.FirstOrDefault(s => s.InstanceId == item.TargetStandInstanceId);
                    if (stand == null)
                    {
                        return Fizzle(item, events);
                    }
                    // Full sale: earning bonuses and On Sale triggers included.
                    SellStand(owner, stand, events);
                    return true;
                }

                case "finders-keepers":
                {
                    var victim = Player(item.AttackTargetId!.Value);
                    if (!EquipStillValid(item, victim, owner))
                    {
                        return RequestRetargetOrFizzle(item, victim, events);
                    }
                    int stolenId = item.TargetEquippedInstanceId!.Value;
                    RemoveEquipped(victim, stolenId);
                    events.Add(new CardsStolen { FromPlayerId = victim.PlayerId, ToPlayerId = owner.PlayerId, Count = 1 });
                    if (!TryEquip(owner, stolenId, item.EquipStandInstanceId, item.EquipReplaceInstanceId, events))
                    {
                        State.BlackMarketDiscard.Add(stolenId);
                    }
                    return true;
                }

                case "hoa-violation":
                    StealMoney(Player(item.AttackTargetId!.Value), owner, 5, "hoa violation", events);
                    return true;

                case "market-forecasting":
                    QueueDraws(owner.PlayerId, 3);
                    return true;

                case "night-shifts":
                    OpenRollWindow(RollPurpose.NightShifts, owner.PlayerId, events);
                    return true;

                case "rebrand":
                {
                    var stand = owner.Stands.FirstOrDefault(s => s.InstanceId == item.TargetStandInstanceId);
                    if (stand == null || State.StandSupply[item.NewStandTypeId].Count == 0)
                    {
                        return Fizzle(item, events);
                    }
                    foreach (int id in item.SelectedInstanceIds)
                    {
                        if (stand.Equipped.Remove(id))
                        {
                            State.BlackMarketDiscard.Add(id);
                        }
                    }
                    State.StandSupply[stand.StandTypeId].Add(stand.Shape); // old shape to the bottom
                    var supply = State.StandSupply[item.NewStandTypeId];
                    stand.StandTypeId = item.NewStandTypeId;
                    stand.Shape = supply[0];
                    supply.RemoveAt(0);
                    events.Add(new StandPurchased
                    {
                        PlayerId = owner.PlayerId,
                        StandInstanceId = stand.InstanceId,
                        StandTypeId = stand.StandTypeId,
                    });
                    return true;
                }

                case "reduce-and-reuse":
                {
                    int bmId = item.DiscardedBmInstanceId!.Value;
                    if (!State.BlackMarketDiscard.Remove(bmId))
                    {
                        return Fizzle(item, events);
                    }
                    if (!TryEquip(owner, bmId, item.EquipStandInstanceId, item.EquipReplaceInstanceId, events))
                    {
                        State.BlackMarketDiscard.Add(bmId);
                    }
                    return true;
                }

                case "reverse-engineer":
                    if (item.DrawInstead)
                    {
                        QueueDraws(owner.PlayerId, 2);
                    }
                    else
                    {
                        int cardId = item.DiscardedLemonInstanceId!.Value;
                        if (!State.LemonDiscard.Remove(cardId))
                        {
                            return Fizzle(item, events);
                        }
                        owner.Hand.Add(cardId);
                        State.PendingDecisions.Add(new PendingDecision
                        {
                            PlayerId = owner.PlayerId,
                            Kind = DecisionKind.ForcedPlay,
                            CardInstanceId = cardId,
                        });
                        events.Add(new DecisionRequired { PlayerId = owner.PlayerId, Kind = DecisionKind.ForcedPlay });
                    }
                    return true;

                case "rummage-sale":
                {
                    foreach (int id in item.SelectedInstanceIds)
                    {
                        var equipOwner = State.Players.FirstOrDefault(p =>
                            p.PlayerId == owner.PlayerId &&
                            (p.Turf.Equipped.Contains(id) || p.Stands.Any(s => s.Equipped.Contains(id))));
                        if (equipOwner == null)
                        {
                            continue;
                        }
                        RemoveEquipped(owner, id);
                        State.BlackMarketDiscard.Add(id);
                        int gain = (Db.BlackMarket(State.BlackMarketInstances[id].DefId).Cost + 1) / 2;
                        owner.Money += gain;
                        events.Add(new MoneyChanged { PlayerId = owner.PlayerId, Amount = gain, Reason = "rummage sale" });
                    }
                    return true;
                }

                case "sharing-is-caring":
                {
                    var victim = Player(item.AttackTargetId!.Value);
                    StealMoney(victim, owner, Math.Min((victim.Money + 1) / 2, 10), "sharing is caring", events);
                    return true;
                }

                case "smear-campaign":
                {
                    var victim = Player(item.AttackTargetId!.Value);
                    int steals = Math.Min(2, victim.Hand.Count);
                    for (int i = 0; i < steals; i++)
                    {
                        int idx = _rng.Next(victim.Hand.Count);
                        int cardId = victim.Hand[idx];
                        victim.Hand.RemoveAt(idx);
                        owner.Hand.Add(cardId);
                    }
                    if (steals > 0)
                    {
                        events.Add(new CardsStolen { FromPlayerId = victim.PlayerId, ToPlayerId = owner.PlayerId, Count = steals });
                    }
                    // "Then you may play any card" — one free plan/attack (designer ruling).
                    if (owner.Hand.Any(id =>
                    {
                        var t = Db.Lemon(State.LemonInstances[id].DefId).Type;
                        return t == LemonCardType.Plan || t == LemonCardType.Attack;
                    }))
                    {
                        State.PendingDecisions.Add(new PendingDecision
                        {
                            PlayerId = owner.PlayerId,
                            Kind = DecisionKind.FreePlayOffer,
                        });
                        events.Add(new DecisionRequired { PlayerId = owner.PlayerId, Kind = DecisionKind.FreePlayOffer });
                    }
                    return true;
                }

                case "steal-the-cashbox":
                {
                    var victim = Player(item.AttackTargetId!.Value);
                    if (victim.Turf.TrapInstanceId != null)
                    {
                        return Fizzle(item, events);
                    }
                    victim.Turf.TrapInstanceId = item.LemonInstanceId;
                    victim.Turf.TrapOwnerId = owner.PlayerId;
                    events.Add(new TrapPlaced { OwnerId = owner.PlayerId, OnPlayerId = victim.PlayerId });
                    return true;
                }

                case "taxes":
                {
                    var victim = Player(item.AttackTargetId!.Value);
                    StealMoney(victim, owner, Math.Min(2 * victim.Stands.Count, 10), "taxes", events);
                    return true;
                }

                case "thats-not-fair":
                {
                    var victim = Player(item.AttackTargetId!.Value);
                    int eq = item.TargetEquippedInstanceId ?? -1;
                    bool valid = eq >= 0 && OwnsEquipped(victim, eq);
                    if (!valid)
                    {
                        return RequestRetargetOrFizzle(item, victim, events);
                    }
                    RemoveEquipped(victim, eq);
                    State.BlackMarketDiscard.Add(eq);
                    events.Add(new CardsDiscarded { PlayerId = victim.PlayerId, InstanceIds = new List<int> { eq } });
                    return true;
                }

                case "trash-pandas":
                {
                    var victim = Player(item.AttackTargetId!.Value);
                    var ownerHand = owner.Hand.ToList();
                    owner.Hand.Clear();
                    owner.Hand.AddRange(victim.Hand);
                    victim.Hand.Clear();
                    victim.Hand.AddRange(ownerHand);
                    events.Add(new HandsTraded { PlayerA = owner.PlayerId, PlayerB = victim.PlayerId });
                    return true;
                }

                default:
                    throw new InvalidActionException($"No resolution handler for '{item.LemonDefId}'.");
            }
        }

        // ------------------------------------------------ attack retargeting

        private bool OwnsEquipped(PlayerState player, int instanceId) =>
            player.Turf.Equipped.Contains(instanceId) ||
            player.Stands.Any(s => s.Equipped.Contains(instanceId));

        private bool HasAnyEquipped(PlayerState player) =>
            player.Turf.Equipped.Count > 0 || player.Stands.Any(s => s.Equipped.Count > 0);

        /// <summary>Finders Keepers: are both the steal target and the equip destination still valid?</summary>
        private bool EquipStillValid(StackItem item, PlayerState victim, PlayerState owner)
        {
            if (!(item.TargetEquippedInstanceId is int eq) || !OwnsEquipped(victim, eq))
            {
                return false;
            }
            var def = Db.BlackMarket(State.BlackMarketInstances[eq].DefId);
            try
            {
                ValidateEquipDestination(owner, def, item.EquipStandInstanceId, item.EquipReplaceInstanceId);
                return true;
            }
            catch (InvalidActionException)
            {
                return false;
            }
        }

        /// <summary>
        /// The attack's specifics are stale (it was Tagged/reflected). If the new victim has
        /// nothing valid, the attack fizzles; otherwise the attacker re-picks (designer ruling).
        /// </summary>
        private bool RequestRetargetOrFizzle(StackItem item, PlayerState victim, List<GameEvent> events)
        {
            if (!HasAnyEquipped(victim))
            {
                return Fizzle(item, events);
            }
            State.PendingDecisions.Add(new PendingDecision
            {
                PlayerId = item.OwnerId,
                Kind = DecisionKind.AttackRetarget,
                StackItemId = item.ItemId,
            });
            events.Add(new DecisionRequired { PlayerId = item.OwnerId, Kind = DecisionKind.AttackRetarget });
            return false; // stays on the stack until SubmitRetarget
        }

        private void ApplySubmitRetarget(SubmitRetarget action, List<GameEvent> events)
        {
            var decision = RequireDecision(action.PlayerId, DecisionKind.AttackRetarget);
            var item = FindStackItem(decision.StackItemId)
                ?? throw new InvalidActionException("The attack is no longer pending.");
            var victim = Player(item.AttackTargetId!.Value);

            if (!(action.TargetEquippedInstanceId is int eq) || !OwnsEquipped(victim, eq))
            {
                throw new InvalidActionException("Choose a card equipped by the attack's current target.");
            }
            item.TargetEquippedInstanceId = eq;
            item.EquipStandInstanceId = action.EquipStandInstanceId;
            item.EquipReplaceInstanceId = action.EquipReplaceInstanceId;

            if (item.LemonDefId == "finders-keepers")
            {
                var def = Db.BlackMarket(State.BlackMarketInstances[eq].DefId);
                ValidateEquipDestination(Player(item.OwnerId), def,
                    action.EquipStandInstanceId, action.EquipReplaceInstanceId);
            }

            State.PendingDecisions.Remove(decision);
            Pump(events);
        }

        private bool Fizzle(StackItem item, List<GameEvent> events)
        {
            events.Add(new AttackFizzled { OwnerId = item.OwnerId, DefId = item.LemonDefId });
            return true; // pops normally; card goes to the discard
        }

        // ----------------------------------------------------------- helpers

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

        /// <summary>Equip an in-hand Black Market instance, discarding a replaced card if needed.</summary>
        private bool TryEquip(PlayerState player, int bmInstanceId, int? standInstanceId, int? replaceId, List<GameEvent> events)
        {
            var def = Db.BlackMarket(State.BlackMarketInstances[bmInstanceId].DefId);
            List<int> equipped;
            int limit;
            if (def.Target == EquipTarget.Stand)
            {
                var stand = player.Stands.FirstOrDefault(s => s.InstanceId == standInstanceId);
                if (stand == null)
                {
                    return false;
                }
                equipped = stand.Equipped;
                limit = Db.StandType(stand.StandTypeId).UpgradeSlots;
            }
            else
            {
                equipped = player.Turf.Equipped;
                limit = Db.Turf.UpgradeSlots;
            }

            if (equipped.Count >= limit)
            {
                if (!(replaceId is int r) || !equipped.Remove(r))
                {
                    return false;
                }
                State.BlackMarketDiscard.Add(r);
                events.Add(new CardsDiscarded { PlayerId = player.PlayerId, InstanceIds = new List<int> { r } });
            }

            equipped.Add(bmInstanceId);
            events.Add(new BlackMarketPurchased
            {
                PlayerId = player.PlayerId,
                InstanceId = bmInstanceId,
                DefId = def.Id,
                TargetStandInstanceId = def.Target == EquipTarget.Stand ? standInstanceId : null,
            });
            return true;
        }
    }
}
