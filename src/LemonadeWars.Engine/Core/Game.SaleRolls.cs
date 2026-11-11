using System.Collections.Generic;
using System.Linq;
using System;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Sale rolls: a roll opens as a pending window (so it can be re-rolled or modified),
    /// then finalizes into payouts and the triggers that ride on them.
    /// </summary>
    public sealed partial class Game
    {
        private const int CashboxTrapMaxSkim = 10;

        private void OpenRollWindow(RollPurpose purpose, int rollerId, List<GameEvent> events, int? standInstanceId = null)
        {
            // A fresh roll starts a fresh "single roll" episode for title tracking.
            State.RollStats.Clear();
            State.PendingRoll = new PendingRoll
            {
                Value = _rng.Roll(Db.Config.SaleDieSides),
                Purpose = purpose,
                RollerId = rollerId,
                StandInstanceId = standInstanceId,
            };
            BumpRevision();
            events.Add(new SaleRolled
            {
                PlayerId = rollerId,
                Value = State.PendingRoll.Value,
                Purpose = purpose,
                StandInstanceId = standInstanceId,
            });
        }

        private void FinalizePendingRoll(List<GameEvent> events)
        {
            var roll = State.PendingRoll!;
            State.PendingRoll = null;
            BumpRevision();
            FinalizeSale(roll, events);
            State.PostRollContinuation = roll.Purpose;
        }

        /// <summary>Base payouts land first, then interactive triggers queue decisions (rulebook p13 ordering).</summary>
        private void FinalizeSale(PendingRoll roll, List<GameEvent> events)
        {
            SellMatchingStands(roll, events);
            if (roll.Purpose == RollPurpose.TurnSale)
            {
                PayTipJars(roll, events);
            }
            SpringCashboxTraps(roll, events);
        }

        /// <summary>
        /// Everyone sells on a table roll; Night Shifts and Spoiled Rotten rolls apply to
        /// the roller only (and Trade Winds, further, to a single stand).
        /// </summary>
        private static bool TakesPartInRoll(PlayerState player, PendingRoll roll) =>
            roll.Purpose == RollPurpose.TurnSale ||
            roll.Purpose == RollPurpose.ExtraSale ||
            player.PlayerId == roll.RollerId;

        private void SellMatchingStands(PendingRoll roll, List<GameEvent> events)
        {
            int playerCount = State.Players.Count;
            for (int offset = 0; offset < playerCount; offset++)
            {
                var player = State.Players[(roll.RollerId + offset) % playerCount];
                if (!TakesPartInRoll(player, roll))
                {
                    continue;
                }

                foreach (var stand in player.Stands.ToList())
                {
                    if (roll.StandInstanceId is int onlyStandId && stand.InstanceId != onlyStandId)
                    {
                        continue; // Trade Winds: this roll is for one specific stand
                    }
                    if (SaleNumbersOf(stand).Contains(roll.Value))
                    {
                        SellStand(player, stand, events);
                    }
                }

                if (roll.StandInstanceId == null && PourNumbersOf(player).Contains(roll.Value))
                {
                    TriggerPowerPour(player, events);
                }
            }
        }

        /// <summary>Tip Jars (once, on your turn): +$1 per own stand that sold on the turn sale.</summary>
        private void PayTipJars(PendingRoll roll, List<GameEvent> events)
        {
            var roller = Player(roll.RollerId);
            int jars = CountOnTurf(roller, BlackMarketCardIds.TipJars);
            if (jars == 0)
            {
                return;
            }
            int standsSold = roller.Stands.Count(s => SaleNumbersOf(s).Contains(roll.Value));
            if (standsSold > 0)
            {
                events.Add(new AbilityTriggered { PlayerId = roller.PlayerId, DefId = BlackMarketCardIds.TipJars });
                GainFromRoll(roller, jars * standsSold, "tip jars", events);
            }
        }

        /// <summary>Steal the Cashbox: skim up to $10 of what the trapped player just earned.</summary>
        private void SpringCashboxTraps(PendingRoll roll, List<GameEvent> events)
        {
            foreach (var trapped in State.Players)
            {
                if (!(trapped.Turf.TrapInstanceId is int trapId) ||
                    !(trapped.Turf.TrapOwnerId is int thiefId) ||
                    !TakesPartInRoll(trapped, roll))
                {
                    continue;
                }
                int earned = State.RollStats.TryGetValue(trapped.PlayerId, out var stats) ? stats.Earned : 0;
                int stolen = Math.Min(earned, CashboxTrapMaxSkim);
                if (stolen > 0)
                {
                    trapped.Money -= stolen;
                    Player(thiefId).Money += stolen;
                    StatsFor(thiefId).MoneyStolen += stolen;
                    events.Add(new MoneyStolen
                    {
                        FromPlayerId = trapped.PlayerId,
                        ToPlayerId = thiefId,
                        Amount = stolen,
                        Reason = "steal the cashbox",
                    });
                    OpenTheftWindow(trapped.PlayerId, thiefId, stolen);
                }
                // "Discard after rolling": the trap goes whether or not it caught anything.
                trapped.Turf.TrapInstanceId = null;
                trapped.Turf.TrapOwnerId = null;
                State.LemonDiscard.Add(trapId);
            }
        }

        private void OpenTheftWindow(int victimId, int attackerId, int amount)
        {
            State.TheftQueue.Add(new PendingTheftResponse
            {
                VictimId = victimId,
                AttackerId = attackerId,
                AmountStolen = amount,
            });
            BumpRevision();
        }
    }
}
