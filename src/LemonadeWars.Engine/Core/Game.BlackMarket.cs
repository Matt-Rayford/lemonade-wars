using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Black Market card behavior: computed stats (sale numbers, pour numbers, earnings,
    /// discounts), On Sale / Power Pour trigger resolution, activated "On Your Turn"
    /// abilities, and the multi-stage ability decisions (victim picks, give-backs, copies).
    /// </summary>
    public sealed partial class Game
    {
        // -------------------------------------------------- computed stats

        private BlackMarketCardDef EquippedDef(int instanceId) =>
            Db.BlackMarket(State.BlackMarketInstances[instanceId].DefId);

        /// <summary>Die faces this stand sells on: printed numbers plus equipped Pushy Salesmen.</summary>
        public IReadOnlyList<int> SaleNumbersOf(StandInstance stand)
        {
            var numbers = Db.StandType(stand.StandTypeId).SaleNumbers.ToList();
            foreach (int id in stand.Equipped)
            {
                var def = EquippedDef(id);
                if (def.Name == "Pushy Salesman" && def.Number is int n && !numbers.Contains(n))
                {
                    numbers.Add(n);
                }
            }
            return numbers;
        }

        /// <summary>Die faces that trigger this player's power pour: printed turf number plus Spiked Lemonades.</summary>
        public IReadOnlyList<int> PourNumbersOf(PlayerState player)
        {
            var numbers = new List<int> { player.Turf.PowerPourNumber };
            foreach (int id in player.Turf.Equipped)
            {
                var def = EquippedDef(id);
                if (def.Name == "Spiked Lemonade" && def.Number is int n && !numbers.Contains(n))
                {
                    numbers.Add(n);
                }
            }
            return numbers;
        }

        /// <summary>
        /// What this stand pays when it sells: base + its own bonuses (Diluted/Pink Lemonade)
        /// + adjacency (a Lefty Loosey on the right neighbor / Righty Tighty on the left).
        /// </summary>
        public int StandEarnings(PlayerState player, StandInstance stand)
        {
            int earnings = Db.StandType(stand.StandTypeId).BaseEarnings;
            foreach (int id in stand.Equipped)
            {
                switch (EquippedDef(id).Id)
                {
                    case "diluted-lemonade":
                        earnings += 2;
                        break;
                    case "pink-lemonade":
                        earnings += 1;
                        break;
                }
            }

            int index = player.Stands.IndexOf(stand);
            if (index + 1 < player.Stands.Count)
            {
                earnings += player.Stands[index + 1].Equipped.Count(id => EquippedDef(id).Id == "lefty-loosey");
            }
            if (index - 1 >= 0)
            {
                earnings += player.Stands[index - 1].Equipped.Count(id => EquippedDef(id).Id == "righty-tighty");
            }
            return earnings;
        }

        private int CountOnTurf(PlayerState player, string defId) =>
            player.Turf.Equipped.Count(id => EquippedDef(id).Id == defId);

        /// <summary>Black Market price after Peddlin' Pete discounts (setup draft pays full price).</summary>
        public int BlackMarketPrice(int playerId, BlackMarketCardDef def)
        {
            if (State.Stage == GameStage.InitialBuys)
            {
                return def.Cost;
            }
            return Math.Max(0, def.Cost - CountOnTurf(Player(playerId), "peddlin-pete"));
        }

        // ------------------------------------------------------ roll stats

        private RollStats StatsFor(int playerId)
        {
            if (!State.RollStats.TryGetValue(playerId, out var stats))
            {
                stats = new RollStats();
                State.RollStats[playerId] = stats;
            }
            return stats;
        }

        private void GainFromRoll(PlayerState player, int amount, string reason, List<GameEvent> events)
        {
            player.Money += amount;
            StatsFor(player.PlayerId).Earned += amount;
            events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = amount, Reason = reason });
        }

        /// <summary>Ability steal: no Profit Share window (that is attacks-only), but tracked for titles.</summary>
        private void TriggerSteal(PlayerState from, PlayerState to, int amount, string reason, List<GameEvent> events)
        {
            int actual = Math.Min(amount, from.Money);
            if (actual <= 0)
            {
                return;
            }
            from.Money -= actual;
            to.Money += actual;
            StatsFor(to.PlayerId).MoneyStolen += actual;
            events.Add(new MoneyStolen
            {
                FromPlayerId = from.PlayerId,
                ToPlayerId = to.PlayerId,
                Amount = actual,
                Reason = reason,
            });
        }

        private int? StealRandomCard(PlayerState from, PlayerState to, List<GameEvent> events)
        {
            if (from.Hand.Count == 0)
            {
                return null;
            }
            int idx = _rng.Next(from.Hand.Count);
            int cardId = from.Hand[idx];
            from.Hand.RemoveAt(idx);
            to.Hand.Add(cardId);
            StatsFor(to.PlayerId).CardsStolen++;
            events.Add(new CardsStolen { FromPlayerId = from.PlayerId, ToPlayerId = to.PlayerId, Count = 1 });
            return cardId;
        }

        // ------------------------------------------------- sale resolution

        /// <summary>One stand sells: pay out its earnings, then fire its On Sale triggers.</summary>
        private void SellStand(PlayerState player, StandInstance stand, List<GameEvent> events)
        {
            int earnings = StandEarnings(player, stand);
            player.Money += earnings;
            StatsFor(player.PlayerId).Earned += earnings;
            events.Add(new StandSold
            {
                PlayerId = player.PlayerId,
                StandInstanceId = stand.InstanceId,
                Earnings = earnings,
            });

            foreach (int id in stand.Equipped.ToList())
            {
                var def = EquippedDef(id);
                switch (def.Id)
                {
                    case "meditation":
                        events.Add(new AbilityTriggered { PlayerId = player.PlayerId, DefId = def.Id });
                        QueueDraws(player.PlayerId, 1, countsForRoll: true);
                        break;
                    case "juice-box-joey":
                    case "nap-time-nino":
                        EnqueueVictimChoice(player, id, def, events);
                        break;
                        // diluted-lemonade / pink-lemonade are folded into StandEarnings.
                }
            }
        }

        /// <summary>A player's power pour fired: base $1 plus every equipped Power Pour trigger.</summary>
        private void TriggerPowerPour(PlayerState player, List<GameEvent> events)
        {
            events.Add(new PowerPourTriggered { PlayerId = player.PlayerId });
            GainFromRoll(player, Db.Turf.BasePowerPourMoney, "power pour", events);

            foreach (int id in player.Turf.Equipped.ToList())
            {
                var def = EquippedDef(id);
                if (def.Timing == EffectTiming.PowerPour)
                {
                    TriggerPourCard(player, id, def, events);
                }
            }
        }

        private void TriggerPourCard(PlayerState player, int instanceId, BlackMarketCardDef def, List<GameEvent> events)
        {
            events.Add(new AbilityTriggered { PlayerId = player.PlayerId, DefId = def.Id });
            switch (def.Id)
            {
                case "early-worm":
                    GainFromRoll(player, 1, def.Name, events);
                    break;
                case "interest":
                    GainFromRoll(player, PourNumbersOf(player).Count, def.Name, events);
                    break;
                case "revelation":
                    QueueDraws(player.PlayerId, 1, countsForRoll: true);
                    break;
                case "whispers-of-fate":
                    QueueDraws(player.PlayerId, 2, countsForRoll: true);
                    AddDecision(new PendingDecision
                    {
                        PlayerId = player.PlayerId,
                        Kind = DecisionKind.AbilityDiscard,
                        RequiredCount = 1,
                        SourceInstanceId = instanceId,
                    }, events);
                    break;
                case "gone-fishin":
                case "two-bit-timmy":
                case "half-pint-harry":
                case "dons-blessings":
                case "family-business":
                    EnqueueVictimChoice(player, instanceId, def, events);
                    break;
                case "innovation":
                {
                    bool hasOther = player.Turf.Equipped.Any(id =>
                        id != instanceId &&
                        EquippedDef(id).Timing == EffectTiming.PowerPour &&
                        EquippedDef(id).Id != "innovation");
                    if (hasOther)
                    {
                        AddDecision(new PendingDecision
                        {
                            PlayerId = player.PlayerId,
                            Kind = DecisionKind.InnovationCopy,
                            SourceInstanceId = instanceId,
                        }, events);
                    }
                    break;
                }
                case "word-of-mouth":
                    if (player.Stands.Count > 0)
                    {
                        AddDecision(new PendingDecision
                        {
                            PlayerId = player.PlayerId,
                            Kind = DecisionKind.WordOfMouthStand,
                            SourceInstanceId = instanceId,
                        }, events);
                    }
                    break;
            }
        }

        private void EnqueueVictimChoice(PlayerState owner, int instanceId, BlackMarketCardDef def, List<GameEvent> events)
        {
            // No one to rob in a game where every opponent is broke/empty-handed is still a
            // valid pick — the steal just clamps to zero. Owner always chooses (designer ruling).
            AddDecision(new PendingDecision
            {
                PlayerId = owner.PlayerId,
                Kind = DecisionKind.AbilityVictim,
                SourceInstanceId = instanceId,
            }, events);
        }

        private void AddDecision(PendingDecision decision, List<GameEvent> events)
        {
            State.PendingDecisions.Add(decision);
            events.Add(new DecisionRequired { PlayerId = decision.PlayerId, Kind = decision.Kind });
        }

        // ------------------------------------------------ ability decisions

        private void ApplySubmitAbilityChoice(SubmitAbilityChoice action, List<GameEvent> events)
        {
            var decision = State.PendingDecisions.FirstOrDefault(d =>
                d.PlayerId == action.PlayerId && (
                    d.Kind == DecisionKind.AbilityVictim ||
                    d.Kind == DecisionKind.AbilityPickCard ||
                    d.Kind == DecisionKind.AbilityGiveBack ||
                    d.Kind == DecisionKind.AbilityDiscard ||
                    d.Kind == DecisionKind.InnovationCopy ||
                    d.Kind == DecisionKind.WordOfMouthStand))
                ?? throw new InvalidActionException($"P{action.PlayerId} has no pending ability decision.");

            var owner = Player(action.PlayerId);
            switch (decision.Kind)
            {
                case DecisionKind.AbilityVictim:
                    ResolveVictimChoice(decision, owner, action, events);
                    break;

                case DecisionKind.AbilityPickCard: // Don's Blessings: pick from the revealed hand
                {
                    var victim = Player(decision.ChosenPlayerId!.Value);
                    if (action.CardInstanceIds.Count != 1 || !victim.Hand.Contains(action.CardInstanceIds[0]))
                    {
                        throw new InvalidActionException("Pick exactly one card from the victim's hand.");
                    }
                    int stolen = action.CardInstanceIds[0];
                    victim.Hand.Remove(stolen);
                    owner.Hand.Add(stolen);
                    StatsFor(owner.PlayerId).CardsStolen++;
                    events.Add(new CardsStolen { FromPlayerId = victim.PlayerId, ToPlayerId = owner.PlayerId, Count = 1 });
                    State.PendingDecisions.Remove(decision);
                    MaybeGiveBack(owner, victim, stolen, decision.SourceInstanceId, events);
                    break;
                }

                case DecisionKind.AbilityGiveBack:
                {
                    var victim = Player(decision.ChosenPlayerId!.Value);
                    if (action.CardInstanceIds.Count != 1 ||
                        !owner.Hand.Contains(action.CardInstanceIds[0]) ||
                        action.CardInstanceIds[0] == decision.StolenCardId)
                    {
                        throw new InvalidActionException("Give back a different card from your hand.");
                    }
                    int given = action.CardInstanceIds[0];
                    owner.Hand.Remove(given);
                    victim.Hand.Add(given);
                    State.PendingDecisions.Remove(decision);
                    break;
                }

                case DecisionKind.AbilityDiscard: // Whispers of Fate
                {
                    var ids = action.CardInstanceIds;
                    if (ids.Count != decision.RequiredCount ||
                        ids.Distinct().Count() != ids.Count ||
                        ids.Any(id => !owner.Hand.Contains(id)))
                    {
                        throw new InvalidActionException(
                            $"Discard exactly {decision.RequiredCount} card(s) from your hand.");
                    }
                    foreach (int id in ids)
                    {
                        owner.Hand.Remove(id);
                        State.LemonDiscard.Add(id);
                    }
                    StatsFor(owner.PlayerId).CardsKept -= ids.Count;
                    events.Add(new CardsDiscarded { PlayerId = owner.PlayerId, InstanceIds = ids.ToList() });
                    State.PendingDecisions.Remove(decision);
                    break;
                }

                case DecisionKind.InnovationCopy:
                {
                    if (!(action.EquippedInstanceId is int copyId) ||
                        !owner.Turf.Equipped.Contains(copyId) ||
                        copyId == decision.SourceInstanceId)
                    {
                        throw new InvalidActionException("Innovation: choose another of your Power Pour cards.");
                    }
                    var copyDef = EquippedDef(copyId);
                    if (copyDef.Timing != EffectTiming.PowerPour || copyDef.Id == "innovation")
                    {
                        throw new InvalidActionException("Innovation: that card has no Power Pour ability to copy.");
                    }
                    State.PendingDecisions.Remove(decision);
                    TriggerPourCard(owner, copyId, copyDef, events);
                    break;
                }

                case DecisionKind.WordOfMouthStand:
                {
                    var stand = owner.Stands.FirstOrDefault(st => st.InstanceId == action.StandInstanceId)
                        ?? throw new InvalidActionException("Word of Mouth: choose one of your own Stands.");
                    State.PendingDecisions.Remove(decision);
                    SellStand(owner, stand, events);
                    break;
                }
            }

            Pump(events);
        }

        private void ResolveVictimChoice(PendingDecision decision, PlayerState owner, SubmitAbilityChoice action, List<GameEvent> events)
        {
            var victim = RequireOtherPlayer(action.TargetPlayerId, owner.PlayerId);
            var def = EquippedDef(decision.SourceInstanceId!.Value);
            State.PendingDecisions.Remove(decision);

            switch (def.Id)
            {
                case "juice-box-joey":
                case "gone-fishin":
                    TriggerSteal(victim, owner, 1, def.Name, events);
                    break;
                case "two-bit-timmy":
                    TriggerSteal(victim, owner, 2, def.Name, events);
                    break;
                case "nap-time-nino":
                case "half-pint-harry":
                {
                    int? stolen = StealRandomCard(victim, owner, events);
                    if (stolen is int stolenId)
                    {
                        MaybeGiveBack(owner, victim, stolenId, decision.SourceInstanceId, events);
                    }
                    break;
                }
                case "family-business":
                {
                    TriggerSteal(victim, owner, 1, def.Name, events);
                    int? stolen = StealRandomCard(victim, owner, events);
                    if (stolen is int stolenId2)
                    {
                        MaybeGiveBack(owner, victim, stolenId2, decision.SourceInstanceId, events);
                    }
                    break;
                }
                case "dons-blessings":
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

        // -------------------------------------------------- turn abilities

        private static readonly HashSet<string> RollAbilityIds = new HashSet<string>
        {
            "downsell", "sugared-up", "take-two",
        };

        private bool HasUnusedRollAbility(PlayerState player) =>
            player.Turf.Equipped.Any(id =>
                RollAbilityIds.Contains(EquippedDef(id).Id) &&
                !State.UsedTurnAbilities.Contains(id) &&
                RollAbilityUsable(EquippedDef(id).Id));

        /// <summary>Downsell cannot lower a 1; Sugared Up cannot raise a 6 (card text "max 6").</summary>
        private bool RollAbilityUsable(string defId)
        {
            if (State.PendingRoll == null)
            {
                return false;
            }
            if (defId == "downsell" && State.PendingRoll.Value <= 1)
            {
                return false;
            }
            if (defId == "sugared-up" && State.PendingRoll.Value >= Db.Config.SaleDieSides)
            {
                return false;
            }
            return true;
        }

        private void ApplyUseTurnAbility(UseTurnAbility action, List<GameEvent> events)
        {
            RequirePlaying();
            if (action.PlayerId != State.ActivePlayer)
            {
                throw new InvalidActionException("On Your Turn abilities only work on your own turn.");
            }
            var player = Player(action.PlayerId);
            if (!player.Turf.Equipped.Contains(action.EquippedInstanceId))
            {
                throw new InvalidActionException("That card is not equipped on your Turf.");
            }
            if (State.UsedTurnAbilities.Contains(action.EquippedInstanceId))
            {
                throw new InvalidActionException("That ability was already used this turn.");
            }

            var def = EquippedDef(action.EquippedInstanceId);
            switch (def.Id)
            {
                case "downsell":
                case "sugared-up":
                {
                    var roll = State.PendingRoll
                        ?? throw new InvalidActionException($"{def.Name} modifies a die that was just rolled.");
                    if (!RollAbilityUsable(def.Id))
                    {
                        throw new InvalidActionException(def.Id == "downsell"
                            ? "Downsell cannot lower a roll of 1."
                            : "Sugared Up cannot raise a roll of 6.");
                    }
                    roll.Value = def.Id == "downsell"
                        ? Math.Max(1, roll.Value - 1)
                        : Math.Min(Db.Config.SaleDieSides, roll.Value + 1);
                    State.UsedTurnAbilities.Add(action.EquippedInstanceId);
                    BumpRevision();
                    events.Add(new RollModified { PlayerId = player.PlayerId, SourceDefId = def.Id, NewValue = roll.Value });
                    break;
                }
                case "take-two":
                {
                    var roll = State.PendingRoll
                        ?? throw new InvalidActionException("Take Two rerolls a die that was just rolled.");
                    roll.Value = _rng.Roll(Db.Config.SaleDieSides);
                    State.UsedTurnAbilities.Add(action.EquippedInstanceId);
                    BumpRevision();
                    events.Add(new RollModified { PlayerId = player.PlayerId, SourceDefId = def.Id, NewValue = roll.Value });
                    break;
                }
                case "liquid-energy":
                {
                    if (State.PendingRoll != null || State.ResponseStack.Count > 0 ||
                        State.PendingDecisions.Count > 0 || State.Phase != TurnPhase.Play)
                    {
                        throw new InvalidActionException("Liquid Energy needs a quiet moment in your Play phase.");
                    }
                    State.UsedTurnAbilities.Add(action.EquippedInstanceId);
                    events.Add(new AbilityTriggered { PlayerId = player.PlayerId, DefId = def.Id });
                    OpenRollWindow(RollPurpose.ExtraSale, player.PlayerId, events);
                    break;
                }
                default:
                    throw new InvalidActionException($"{def.Name} has no activated ability.");
            }
            Pump(events);
        }

        // -------------------------------------------------------- reactions

        /// <summary>Swear Jar: each tantrum played against this player's card costs the thrower $2 per jar.</summary>
        private void ChargeSwearJars(PlayerState thrower, PlayerState target, List<GameEvent> events)
        {
            int jars = CountOnTurf(target, "swear-jar");
            if (jars == 0)
            {
                return;
            }
            int charge = Math.Min(2 * jars, thrower.Money);
            if (charge > 0)
            {
                thrower.Money -= charge;
                target.Money += charge;
                events.Add(new MoneyStolen
                {
                    FromPlayerId = thrower.PlayerId,
                    ToPlayerId = target.PlayerId,
                    Amount = charge,
                    Reason = "swear jar",
                });
            }
        }

        /// <summary>Bouncer: after an attack resolves against its owner, they may play an attack for free.</summary>
        private void CheckBouncer(StackItem resolvedAttack, List<GameEvent> events)
        {
            if (!(resolvedAttack.AttackTargetId is int victimId))
            {
                return;
            }
            var victim = Player(victimId);
            int bouncers = CountOnTurf(victim, "bouncer");
            if (bouncers == 0)
            {
                return;
            }
            bool hasAttack = victim.Hand.Any(id =>
                Db.Lemon(State.LemonInstances[id].DefId).Type == LemonCardType.Attack);
            if (!hasAttack)
            {
                return;
            }
            for (int i = 0; i < bouncers; i++)
            {
                AddDecision(new PendingDecision
                {
                    PlayerId = victimId,
                    Kind = DecisionKind.BouncerAttack,
                }, events);
            }
        }
    }
}
