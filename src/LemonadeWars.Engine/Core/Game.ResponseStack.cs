using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// The response stack itself: pushing a play or purchase, resolving the top item, and
    /// popping resolved or cancelled items to their discards.
    /// </summary>
    public sealed partial class Game
    {
        private StackItem TopStackItem() => State.ResponseStack[State.ResponseStack.Count - 1];

        private StackItem? FindStackItem(int? itemId) =>
            itemId == null ? null : State.ResponseStack.FirstOrDefault(item => item.ItemId == itemId);

        /// <summary>The card a stack item IS: the bought Black Market card, or the played Lemon card.</summary>
        private string StackItemDefId(StackItem item) =>
            item.Kind == StackItemKind.BlackMarketPurchase
                ? State.BlackMarketInstances[item.BmInstanceId!.Value].DefId
                : item.LemonDefId;

        // --------------------------------------------------------------- push

        private void PushStackItem(StackItem item, List<GameEvent> events)
        {
            item.ItemId = State.NextStackItemId++;
            State.ResponseStack.Add(item);
            BumpRevision();
            if (item.Kind == StackItemKind.LemonPlay)
            {
                AnnounceLemonPlay(item, events);
            }
        }

        private void AnnounceLemonPlay(StackItem item, List<GameEvent> events)
        {
            var answered = item.RespondingToItemId is int answeredId
                ? State.ResponseStack.FirstOrDefault(candidate => candidate.ItemId == answeredId)
                : null;
            BlackMarketCardInstance? stolen = null;
            if (item.TargetEquippedInstanceId is int stolenId)
            {
                State.BlackMarketInstances.TryGetValue(stolenId, out stolen);
            }
            var (takenDefId, takenShape, takenIsLemon) = TakenByPlay(item);
            events.Add(new LemonCardPlayed
            {
                PlayerId = item.OwnerId,
                InstanceId = item.LemonInstanceId ?? 0,
                DefId = item.LemonDefId,
                TargetPlayerId = item.AttackTargetId,
                RespondingToOwnerId = answered?.OwnerId,
                RespondingToDefId = answered == null ? null : StackItemDefId(answered),
                StolenDefId = stolen?.DefId,
                StolenShape = stolen?.Shape,
                TakenDefId = takenDefId,
                TakenShape = takenShape,
                TakenIsLemon = takenIsLemon,
            });
        }

        /// <summary>
        /// The card a take is aimed at — a shelf card (Connections), a discarded Black
        /// Market card (Reduce and Reuse) or a discarded Lemon card (Reverse Engineer) —
        /// so the table can show the loot beside the play.
        /// </summary>
        private (string? DefId, Shape? Shape, bool IsLemon) TakenByPlay(StackItem item)
        {
            if (item.MarketIndex is int shelfIndex && shelfIndex >= 0 && shelfIndex < State.Market.Count)
            {
                var shelfCard = State.BlackMarketInstances[State.Market[shelfIndex]];
                return (shelfCard.DefId, shelfCard.Shape, false);
            }
            if (item.DiscardedBmInstanceId is int discardedBm &&
                State.BlackMarketInstances.TryGetValue(discardedBm, out var discardedBmCard))
            {
                return (discardedBmCard.DefId, discardedBmCard.Shape, false);
            }
            if (item.DiscardedLemonInstanceId is int discardedLemon &&
                State.LemonInstances.TryGetValue(discardedLemon, out var discardedLemonCard))
            {
                return (discardedLemonCard.DefId, null, true);
            }
            return (null, null, false);
        }

        // ------------------------------------------------------------ resolve

        /// <summary>Resolve the top stack item. Returns false if resolution paused on a decision.</summary>
        private bool ResolveTopItem(List<GameEvent> events)
        {
            var top = TopStackItem();
            if (top.Kind == StackItemKind.BlackMarketPurchase)
            {
                CompleteBlackMarketPurchase(top, events);
                PopResolved(top, events);
                return true;
            }
            if (TryResolveResponseCard(top, events))
            {
                PopResolved(top, events);
                return true;
            }
            return ResolvePlanOrAttack(top, events);
        }

        /// <summary>Returns false when the item is not one of the five response cards.</summary>
        private bool TryResolveResponseCard(StackItem item, List<GameEvent> events)
        {
            switch (item.LemonDefId)
            {
                case TantrumId:
                    ResolveTantrum(item, events);
                    return true;
                case TagId:
                    ResolveTag(item, events);
                    return true;
                case RubberGlueId:
                    ResolveRubberGlue(item, events);
                    return true;
                case OutOfStockId:
                    ResolveOutOfStock(item, events);
                    return true;
                case ProfitShareId:
                    ResolveProfitShare(item, events);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Card-specific handlers live in Game.LemonEffects.cs. Returns false when an
        /// attack that was re-aimed by a Tag pauses for its owner's AttackRetarget decision.
        /// </summary>
        private bool ResolvePlanOrAttack(StackItem item, List<GameEvent> events)
        {
            if (!ResolveLemonPlay(item, events))
            {
                return false;
            }
            bool wasAttack = Db.Lemon(item.LemonDefId).Type == LemonCardType.Attack;
            PopResolved(item, events);
            if (wasAttack)
            {
                CheckBouncer(item, events);
            }
            return true;
        }

        // ---------------------------------------------------------------- pop

        /// <summary>Pop an item that resolved; its card (if any, and not a tantrum/trap) goes to the discard.</summary>
        private void PopResolved(StackItem item, List<GameEvent> events)
        {
            State.ResponseStack.Remove(item);
            BumpRevision();
            if (item.Kind == StackItemKind.LemonPlay)
            {
                events.Add(new PlayResolved { OwnerId = item.OwnerId, DefId = item.LemonDefId });
                DiscardPlayedLemon(item);
            }
        }

        /// <summary>Pop a cancelled item: purchases refund money; cards go to the discard.</summary>
        private void PopCancelled(StackItem item, List<GameEvent> events)
        {
            State.ResponseStack.Remove(item);
            // The window open on this item dies with it. An Inflatable Decoy cancels
            // WITHOUT pushing a new item, so nothing else would recompute who is awaited
            // and they would be left with a pass-only window on a play that is gone.
            State.AwaitingResponse.Clear();
            BumpRevision();
            if (item.Kind == StackItemKind.BlackMarketPurchase)
            {
                RefundCancelledPurchase(item, events);
            }
            else
            {
                DiscardPlayedLemon(item);
            }
        }

        private void RefundCancelledPurchase(StackItem purchase, List<GameEvent> events)
        {
            var buyer = Player(purchase.OwnerId);
            buyer.Money += purchase.PaidCost;
            events.Add(new MoneyChanged
            {
                PlayerId = buyer.PlayerId,
                Amount = purchase.PaidCost,
                Reason = "purchase refunded",
            });
            State.BlackMarketDiscard.Add(purchase.BmInstanceId!.Value);
            // The shelf slot stayed empty while the purchase was contested: fill it now.
            RefillAfterPurchase(purchase, events);
        }

        private void DiscardPlayedLemon(StackItem item)
        {
            if (item.LemonInstanceId is int id)
            {
                string defId = State.LemonInstances[id].DefId;
                // Tantrums live in piles; a placed Steal the Cashbox trap lives on a turf.
                if (defId != TantrumId && !IsPlacedTrap(id))
                {
                    State.LemonDiscard.Add(id);
                }
            }
        }

        private bool IsPlacedTrap(int instanceId) =>
            State.Players.Any(player => player.Turf.TrapInstanceId == instanceId);
    }
}
