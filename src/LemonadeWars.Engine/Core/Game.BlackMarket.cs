using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>Equipped Black Market cards at sale time: computed stats and On Sale / Power Pour triggers.</summary>
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
                if (def.Name == "Pushy Salesman" && def.Number is int addedNumber && !numbers.Contains(addedNumber))
                {
                    numbers.Add(addedNumber);
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
                if (def.Name == "Spiked Lemonade" && def.Number is int addedNumber && !numbers.Contains(addedNumber))
                {
                    numbers.Add(addedNumber);
                }
            }
            return numbers;
        }

        /// <summary>
        /// Base + the stand's own bonuses (Diluted/Pink Lemonade) + adjacency (a Lefty Loosey
        /// on the right neighbor, a Righty Tighty on the left).
        /// </summary>
        public int StandEarnings(PlayerState player, StandInstance stand)
        {
            int earnings = Db.StandType(stand.StandTypeId).BaseEarnings;
            foreach (int id in stand.Equipped)
            {
                switch (EquippedDef(id).Id)
                {
                    case BlackMarketCardIds.DilutedLemonade:
                        earnings += 2;
                        break;
                    case BlackMarketCardIds.PinkLemonade:
                        earnings += 1;
                        break;
                }
            }

            int index = player.Stands.IndexOf(stand);
            if (index + 1 < player.Stands.Count)
            {
                earnings += player.Stands[index + 1].Equipped.Count(id => EquippedDef(id).Id == BlackMarketCardIds.LeftyLoosey);
            }
            if (index - 1 >= 0)
            {
                earnings += player.Stands[index - 1].Equipped.Count(id => EquippedDef(id).Id == BlackMarketCardIds.RightyTighty);
            }
            return earnings;
        }

        private int CountOnTurf(PlayerState player, string defId) =>
            player.Turf.Equipped.Count(id => EquippedDef(id).Id == defId);

        /// <summary>Peddlin' Pete's discount counts during the setup draft too (designer ruling).</summary>
        public int BlackMarketPrice(int playerId, BlackMarketCardDef def) =>
            Math.Max(0, def.Cost - CountOnTurf(Player(playerId), BlackMarketCardIds.PeddlinPete));

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

        /// <summary>Returns the stolen card's instance id, or null when the hand was empty.</summary>
        private int? StealRandomCard(PlayerState from, PlayerState to, List<GameEvent> events)
        {
            if (from.Hand.Count == 0)
            {
                return null;
            }
            int handIndex = _rng.Next(from.Hand.Count);
            int cardId = from.Hand[handIndex];
            from.Hand.RemoveAt(handIndex);
            to.Hand.Add(cardId);
            StatsFor(to.PlayerId).CardsStolen++;
            events.Add(new CardsStolen { FromPlayerId = from.PlayerId, ToPlayerId = to.PlayerId, Count = 1 });
            return cardId;
        }

        // ------------------------------------------------- sale resolution

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
                    case BlackMarketCardIds.Meditation:
                        events.Add(new AbilityTriggered { PlayerId = player.PlayerId, DefId = def.Id });
                        QueueDraws(player.PlayerId, 1, countsForRoll: true);
                        break;
                    case BlackMarketCardIds.JuiceBoxJoey:
                    case BlackMarketCardIds.NapTimeNino:
                        EnqueueVictimChoice(player, id, events);
                        break;
                        // diluted-lemonade / pink-lemonade are folded into StandEarnings.
                }
            }
        }

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
                case BlackMarketCardIds.EarlyWorm:
                    GainFromRoll(player, 1, def.Name, events);
                    break;
                case BlackMarketCardIds.Interest:
                    GainFromRoll(player, PourNumbersOf(player).Count, def.Name, events);
                    break;
                case BlackMarketCardIds.Revelation:
                    QueueDraws(player.PlayerId, 1, countsForRoll: true);
                    break;
                case BlackMarketCardIds.WhispersOfFate:
                    // "+2 Lemon cards, then discard 1 OF THEM": the draw queue raises the
                    // discard decision once the two cards exist.
                    QueueDraws(player.PlayerId, 2, countsForRoll: true,
                        discardAfterCount: 1, discardAfterSourceInstanceId: instanceId);
                    break;
                case BlackMarketCardIds.GoneFishin:
                case BlackMarketCardIds.TwoBitTimmy:
                case BlackMarketCardIds.HalfPintHarry:
                case BlackMarketCardIds.DonsBlessings:
                case BlackMarketCardIds.FamilyBusiness:
                    EnqueueVictimChoice(player, instanceId, events);
                    break;
                case BlackMarketCardIds.Innovation:
                    if (HasPourCardToCopy(player, instanceId))
                    {
                        AddAbilityDecision(player, DecisionKind.InnovationCopy, instanceId, events);
                    }
                    break;
                case BlackMarketCardIds.WordOfMouth:
                    if (player.Stands.Count > 0)
                    {
                        AddAbilityDecision(player, DecisionKind.WordOfMouthStand, instanceId, events);
                    }
                    break;
            }
        }

        private static bool IsCopyablePourCard(BlackMarketCardDef def) =>
            def.Timing == EffectTiming.PowerPour && def.Id != BlackMarketCardIds.Innovation;

        private bool HasPourCardToCopy(PlayerState player, int? innovationInstanceId) =>
            player.Turf.Equipped.Any(id =>
                id != innovationInstanceId && IsCopyablePourCard(EquippedDef(id)));

        /// <summary>
        /// The owner always chooses (designer ruling), even when every opponent is broke
        /// or empty-handed: the steal just clamps to zero.
        /// </summary>
        private void EnqueueVictimChoice(PlayerState owner, int instanceId, List<GameEvent> events) =>
            AddAbilityDecision(owner, DecisionKind.AbilityVictim, instanceId, events);

        private void AddAbilityDecision(PlayerState owner, DecisionKind kind, int sourceInstanceId, List<GameEvent> events)
        {
            AddDecision(new PendingDecision
            {
                PlayerId = owner.PlayerId,
                Kind = kind,
                SourceInstanceId = sourceInstanceId,
            }, events);
        }

        private void AddDecision(PendingDecision decision, List<GameEvent> events)
        {
            State.PendingDecisions.Add(decision);
            events.Add(new DecisionRequired { PlayerId = decision.PlayerId, Kind = decision.Kind });
        }
    }
}
