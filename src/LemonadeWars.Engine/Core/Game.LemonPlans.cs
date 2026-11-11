using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
    public sealed partial class Game
    {
        private bool ResolveApologize(StackItem item, PlayerState owner)
        {
            var record = PickTantrum(owner, item.TantrumInstanceId);
            if (record != null)
            {
                owner.TantrumPile.Remove(record);
                State.LemonDiscard.Add(record.InstanceId);
                // The pump re-settles the baby title once the stack unwinds: with a
                // smaller pile it may pass on, or lapse if no one has any.
                State.EpisodeHadTantrums = true;
            }
            return true;
        }

        /// <summary>
        /// The recorded pick when one was given (older persisted games), else any copy: they are
        /// interchangeable now that the baby tie-break tracks PLAY recency, not pile contents.
        /// </summary>
        private static TantrumRecord? PickTantrum(PlayerState owner, int? instanceId) =>
            instanceId is int id
                ? owner.TantrumPile.FirstOrDefault(t => t.InstanceId == id)
                : owner.TantrumPile.FirstOrDefault();

        private bool ResolveConnections(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            int marketIndex = item.MarketIndex!.Value;
            if (marketIndex >= State.Market.Count)
            {
                return Fizzle(item, events);
            }
            int bmId = State.Market[marketIndex];
            State.Market.RemoveAt(marketIndex);
            EquipOrDiscard(owner, bmId, item, events);
            RefillMarket(marketIndex);
            events.Add(new MarketRefilled { Market = State.Market.ToList() });
            return true;
        }

        private bool ResolveDoorbusterSale(StackItem item, PlayerState owner, List<GameEvent> events)
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

        private bool ResolveRebrand(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            var stand = owner.Stands.FirstOrDefault(s => s.InstanceId == item.TargetStandInstanceId);
            if (stand == null || State.StandSupply[item.NewStandTypeId].Count == 0)
            {
                return Fizzle(item, events);
            }
            foreach (int overflowId in item.SelectedInstanceIds)
            {
                if (stand.Equipped.Remove(overflowId))
                {
                    State.BlackMarketDiscard.Add(overflowId);
                }
            }
            State.StandSupply[stand.StandTypeId].Add(stand.Shape); // old shape to the bottom
            var newSupply = State.StandSupply[item.NewStandTypeId];
            stand.StandTypeId = item.NewStandTypeId;
            stand.Shape = newSupply[0];
            newSupply.RemoveAt(0);
            events.Add(new StandPurchased
            {
                PlayerId = owner.PlayerId,
                StandInstanceId = stand.InstanceId,
                StandTypeId = stand.StandTypeId,
            });
            return true;
        }

        private bool ResolveReduceAndReuse(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            int bmId = item.DiscardedBmInstanceId!.Value;
            if (!State.BlackMarketDiscard.Remove(bmId))
            {
                return Fizzle(item, events);
            }
            EquipOrDiscard(owner, bmId, item, events);
            return true;
        }

        private bool ResolveReverseEngineer(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            if (item.DrawInstead)
            {
                QueueDraws(owner.PlayerId, 2);
                return true;
            }

            int recoveredId = item.DiscardedLemonInstanceId!.Value;
            if (!State.LemonDiscard.Remove(recoveredId))
            {
                return Fizzle(item, events);
            }
            owner.Hand.Add(recoveredId);
            State.PendingDecisions.Add(new PendingDecision
            {
                PlayerId = owner.PlayerId,
                Kind = DecisionKind.ForcedPlay,
                CardInstanceId = recoveredId,
            });
            events.Add(new DecisionRequired { PlayerId = owner.PlayerId, Kind = DecisionKind.ForcedPlay });
            return true;
        }

        private bool ResolveRummageSale(StackItem item, PlayerState owner, List<GameEvent> events)
        {
            int proceeds = 0;
            foreach (int soldId in item.SelectedInstanceIds)
            {
                if (!OwnsEquipped(owner, soldId))
                {
                    continue; // sold out from under the play while it was contested
                }
                RemoveEquipped(owner, soldId);
                State.BlackMarketDiscard.Add(soldId);
                int halfCostRoundedUp = (EquippedDef(soldId).Cost + 1) / 2;
                proceeds += halfCostRoundedUp;
            }
            if (proceeds > 0)
            {
                owner.Money += proceeds;
                events.Add(new MoneyChanged { PlayerId = owner.PlayerId, Amount = proceeds, Reason = "rummage sale" });
            }
            return true;
        }
    }
}
