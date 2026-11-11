using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Cards leaving the shelf: stands, Bragging Rights and Black Market cards. The
    /// engine's refill event carries only the new row of ids, so the viewer's last-seen
    /// row is kept here and diffed to learn which card left which slot.
    /// </summary>
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>The Black Market row as the viewer last saw it (seeded per render, advanced by each MarketRefilled).</summary>
        private List<PlayerView.CardInfo> _marketSnap;
        /// <summary>Stand-supply top shapes as last rendered: the copy a StandPurchased took.</summary>
        private Dictionary<string, Shape> _supplyTopSnap;
        /// <summary>Refilled cards the viewer has not been shown yet: each renders as an
        /// empty slot until its deal flight delivers, which happens exactly once on every exit path.</summary>
        private readonly HashSet<int> _hiddenMarketIds = new HashSet<int>();
        /// <summary>Shelf cards the viewer picked up by double-click: their slot is already
        /// empty, so the refill that follows must not put a stand-in back in it.</summary>
        private readonly HashSet<int> _liftedMarketIds = new HashSet<int>();
        /// <summary>Where the viewer's lifted card came to rest (a stand, the turf): a
        /// contested purchase stages itself from there, not from the empty slot.</summary>
        private UnityEngine.Vector3? _liftedCardLanding;
        /// <summary>Cards equipped onto a stand whose flight has not landed: drawn invisible in place.</summary>
        private readonly HashSet<int> _equippedInFlight = new HashSet<int>();
        /// <summary>A contested purchase's card arrives at this size relative to the staged chip (tucked card over stage card).</summary>
        private const float TuckedOverStagedScale = 188f / 154f;
        /// <summary>For a hidden refill id: the art of the card its slot used to hold,
        /// drawn inert there until the effect that sweeps or lifts it is on screen.</summary>
        private readonly Dictionary<int, UnityEngine.Texture2D> _marketStandIns =
            new Dictionary<int, UnityEngine.Texture2D>();
        /// <summary>Who last paid, or took a card off the market for free. MarketRefilled has
        /// no buyer field, but one of those events always precedes it in the same burst.</summary>
        private int _lastSpenderId = -1;
        /// <summary>What the Black Market take in progress cost (0 for a free take), for the bills its drift slaps down.</summary>
        private int _lastMarketPrice;
        private ShelfDeparture _shelfDeparture;

        /// <summary>An uncontested in-game purchase waiting for the refill that follows it in the same burst.</summary>
        private sealed class ShelfDeparture
        {
            public UnityEngine.Texture2D Art;
            public int Slot;
            public int BuyerId;
            public int Price;
            public bool Lifted;
        }

        /// <summary>Engine truth, not presentation state: a slot held visually empty is still snapshotted.</summary>
        private void SnapshotMarketBaseline()
        {
            _marketSnap = View.Market
                .Select(c => new PlayerView.CardInfo
                {
                    InstanceId = c.InstanceId,
                    DefId = c.DefId,
                    Shape = c.Shape,
                })
                .ToList();
            _supplyTopSnap = new Dictionary<string, Shape>(View.SupplyTopShapes);
        }

        // ---------------------------------------------- stands and Bragging Rights

        private void HandleStandPurchased(StandPurchased standBought)
        {
            // Queued, so the register rings alongside the "-$X" floater.
            _fx.QueueSound(Sfx.CashRegister);
            var standArt = _supplyTopSnap != null &&
                _supplyTopSnap.TryGetValue(standBought.StandTypeId, out var topShape)
                    ? _art.Stand(standBought.StandTypeId, topShape)
                    : _art.Stand(standBought.StandTypeId);
            string typeId = standBought.StandTypeId;
            _fx.QueuePurchase(standArt, standBought.PlayerId, standBought.Price, _art.Bill,
                () => _table.StandSupplyWorld(typeId),
                showCard: ShowsPurchaseTheatre(standBought.PlayerId));
        }

        private void HandleBraggingRightsPurchased(BraggingRightsPurchased brag)
        {
            if (brag.PlayerId == _session.Seat)
            {
                Sfx.Play(Sfx.TitleClaim); // a title claim first, a purchase second
            }
            else
            {
                _fx.QueueSound(Sfx.CashRegister);
            }
            // Which shelf card was bought: $16 base, +$2 per sale (the same
            // index-from-price math as TableView.RenderSupply).
            var bragArt = _art.BraggingRights((brag.Price - 16) / 2)
                ?? _art.Back("braggingRights");
            _fx.QueuePurchase(bragArt, brag.PlayerId, brag.Price, _art.Bill,
                () => _table.BraggingShelfWorld(),
                showCard: ShowsPurchaseTheatre(brag.PlayerId));
        }

        // --------------------------------------------------- Black Market cards

        /// <summary>
        /// An in-game purchase was paid for; its slot stays empty until it resolves. Open to a
        /// Tantrum it is staged like a play; otherwise the refill in this same burst presents it.
        /// </summary>
        private void HandlePurchaseStarted(BlackMarketPurchaseStarted started)
        {
            var oldSnap = _marketSnap;
            _marketSnap = oldSnap?.Where(c => c.InstanceId != started.InstanceId).ToList();
            bool lifted = _liftedMarketIds.Remove(started.InstanceId);
            if (lifted)
            {
                _hiddenMarketIds.Remove(started.InstanceId);
            }
            var landing = lifted ? _liftedCardLanding : null;
            _liftedCardLanding = null;
            if (oldSnap == null)
            {
                return; // no baseline yet (fresh join): nothing to animate from
            }
            var art = _art.BlackMarket(started.DefId, started.Shape);
            int slot = started.MarketIndex;
            _fx.QueueSound(Sfx.CashRegister);
            if (IsContestedPurchase(started.PlayerId, started.DefId))
            {
                _stagedAttacker = started.PlayerId;
                _playTheatre.Begin(art, started.DefId, started.PlayerId, -1,
                    fromWorld: landing ?? _table.MarketSlotWorld(slot));
                _fx.QueuePurchase(art, started.PlayerId, started.Price, _art.Bill,
                    () => _table.MarketSlotWorld(slot), null, showCard: false);
                return;
            }
            _shelfDeparture = new ShelfDeparture
            {
                Art = art, Slot = slot, BuyerId = started.PlayerId, Price = started.Price, Lifted = lifted,
            };
        }

        /// <summary>
        /// Fires when the equip completes (after the Tantrum window, never if cancelled). The card
        /// left the shelf earlier, so the only theatre here is a contested purchase sliding home.
        /// </summary>
        private void HandleBlackMarketPurchased(BlackMarketPurchased bought)
        {
            // For free takes (Connections) this precedes the refill and is the only
            // event naming the taker.
            _lastSpenderId = bought.PlayerId;
            _lastMarketPrice = bought.Price;
            if (_playTheatre.Matches(bought.PlayerId, bought.DefId))
            {
                // The staged card slides to its tucked place on the stand (or the bar, off
                // the viewed board); the stand shows the card only once it has arrived.
                int equippedId = bought.InstanceId;
                _equippedInFlight.Add(equippedId);
                _renderedRevision = -1;
                _playTheatre.ResolveTo(() => _table.EquippedCardWorld(equippedId), TuckedOverStagedScale, () =>
                {
                    _equippedInFlight.Remove(equippedId);
                    _renderedRevision = -1;
                });
            }
        }

        /// <summary>
        /// The moment cards visibly leave the Black Market shelf: diffing the new ids against
        /// the last-seen row gives the departed cards and the freshly dealt slots.
        /// </summary>
        private void HandleMarketRefilled(MarketRefilled refilled)
        {
            var oldSnap = _marketSnap;
            var newIds = refilled.Market;
            // Advanced first (carrying known art forward), so a second purchase in the
            // same burst diffs against THIS refill's row.
            _marketSnap = newIds
                .Select(id => oldSnap?.FirstOrDefault(c => c.InstanceId == id)
                    ?? new PlayerView.CardInfo { InstanceId = id, DefId = null })
                .ToList();
            if (oldSnap == null)
            {
                _shelfDeparture = null;
                return; // no baseline yet (fresh join): nothing to animate from
            }
            var freshSlots = newIds
                .Select((id, slot) => (Slot: slot, InstanceId: id))
                .Where(entry => !oldSnap.Any(c => c.InstanceId == entry.InstanceId))
                .ToList();

            if (refilled.RefreshedByPlayerId is int refresher)
            {
                PresentPaidMarketRefresh(oldSnap, freshSlots, refresher);
                return;
            }

            PresentPendingShelfDeparture(freshSlots);
            PresentUncontestedMarketTakes(oldSnap, newIds, freshSlots);
            // The replacement(s) are dealt from the deck once the purchase has played.
            QueueMarketDeal(freshSlots, new List<(int Slot, UnityEngine.Texture2D Art)>(), payerId: -1, price: 0);
        }

        /// <summary>The dollar comes out, the whole row slides under the deck, and a fresh row is dealt out of it.</summary>
        private void PresentPaidMarketRefresh(List<PlayerView.CardInfo> oldSnap,
            List<(int Slot, int InstanceId)> freshSlots, int refresher)
        {
            var swept = oldSnap
                .Select((card, slot) => (Slot: slot, Art: ShelfArt(card)))
                .Where(entry => entry.Art != null)
                .ToList();
            foreach (var (slot, art) in swept)
            {
                HoldStandIn(freshSlots, slot, art);
            }
            QueueMarketDeal(freshSlots, swept, refresher, _db.Config.BlackMarketRefreshCost);
        }

        /// <summary>An in-game purchase nobody could contest started and resolved in one burst; this refill is its own.</summary>
        private void PresentPendingShelfDeparture(List<(int Slot, int InstanceId)> freshSlots)
        {
            if (_shelfDeparture is ShelfDeparture departure)
            {
                _shelfDeparture = null;
                PresentShelfDeparture(departure.Art, departure.Slot, departure.BuyerId, departure.Price,
                    departure.Lifted, freshSlots);
            }
        }

        /// <summary>Cards that left the shelf in this very event: setup-draft purchases
        /// and free takes (Connections), which are never contested as purchases.</summary>
        private void PresentUncontestedMarketTakes(List<PlayerView.CardInfo> oldSnap, List<int> newIds,
            List<(int Slot, int InstanceId)> freshSlots)
        {
            for (int slot = 0; slot < oldSnap.Count; slot++)
            {
                var card = oldSnap[slot];
                if (newIds.Contains(card.InstanceId))
                {
                    continue;
                }
                var art = ShelfArt(card);
                if (art == null)
                {
                    continue; // refilled and re-bought inside one burst: its art was never seen, nothing to fly
                }
                bool lifted = _liftedMarketIds.Remove(card.InstanceId);
                if (lifted)
                {
                    _hiddenMarketIds.Remove(card.InstanceId);
                }
                _liftedCardLanding = null;
                _fx.QueueSound(Sfx.CashRegister);
                PresentShelfDeparture(art, slot, _lastSpenderId, _lastMarketPrice, lifted, freshSlots);
            }
        }

        /// <summary>
        /// A bought (or taken) card keeps its place as a stand-in until its purchase
        /// effect starts, unless the viewer already lifted it off the shelf themselves.
        /// </summary>
        private void PresentShelfDeparture(UnityEngine.Texture2D art, int originSlot, int buyerId, int price,
            bool lifted, List<(int Slot, int InstanceId)> freshSlots)
        {
            if (!lifted)
            {
                HoldStandIn(freshSlots, originSlot, art);
            }
            _fx.QueuePurchase(art, buyerId, price, _art.Bill,
                () => _table.MarketSlotWorld(originSlot),
                null, ShowsPurchaseTheatre(buyerId),
                onStarted: () => DropStandIns(freshSlots));
        }

        /// <summary>
        /// Hides the dealt cards' slots until each flight lands, then queues the deal.
        /// Art resolves when the effect starts, when the view has certainly caught up.
        /// </summary>
        private void QueueMarketDeal(List<(int Slot, int InstanceId)> dealt,
            List<(int Slot, UnityEngine.Texture2D Art)> swept, int payerId, int price)
        {
            if (dealt.Count == 0 && swept.Count == 0)
            {
                return;
            }
            var deals = new List<ShelfCardFlight>();
            foreach (var (slot, instanceId) in dealt)
            {
                _hiddenMarketIds.Add(instanceId);
                deals.Add(new ShelfCardFlight
                {
                    Art = () => MarketArt(instanceId),
                    SlotWorld = () => _table.MarketSlotWorld(slot),
                    OnLanded = () => ReleaseMarketCard(instanceId),
                });
            }
            var sweep = new List<ShelfCardFlight>();
            foreach (var (slot, art) in swept)
            {
                sweep.Add(new ShelfCardFlight
                {
                    Art = () => art,
                    SlotWorld = () => _table.MarketSlotWorld(slot),
                });
            }
            _fx.QueueMarketDeal(() => _table.BlackMarketDeckWorld(), sweep, deals, payerId, price, _art.Bill,
                onStarted: () => DropStandIns(dealt));
        }

        /// <summary>The purchase just went onto the response stack: others may still Tantrum it.</summary>
        private bool IsContestedPurchase(int buyerId, string defId) =>
            View?.StackTop != null && View.StackTop.IsPurchase &&
            View.StackTop.OwnerId == buyerId && View.StackTop.DefId == defId;

        /// <summary>The viewer already knows what they just bought, so only rivals' purchases
        /// get the showcase-and-fly treatment (the viewer's money still slaps down).</summary>
        private bool ShowsPurchaseTheatre(int buyerId) => buyerId != _session.Seat;

        // ------------------------------------------- lifted cards and stand-ins

        /// <summary>The viewer picked a shelf card up: its slot shows empty until the shelf refills.</summary>
        private void LiftMarketCard(int instanceId)
        {
            _hiddenMarketIds.Add(instanceId);
            _liftedMarketIds.Add(instanceId);
            _liftedCardLanding = null;
            _renderedRevision = -1;
            RenderIfChanged();
        }

        /// <summary>The lifted card was not bought after all: back onto its slot.</summary>
        private void RestoreMarketCard(int instanceId)
        {
            if (_liftedMarketIds.Remove(instanceId))
            {
                _hiddenMarketIds.Remove(instanceId);
                _liftedCardLanding = null;
                _renderedRevision = -1;
            }
        }

        /// <summary>The card that left <paramref name="slot"/> keeps its place on the shelf, inert, until its effect plays.</summary>
        private void HoldStandIn(List<(int Slot, int InstanceId)> fresh, int slot, UnityEngine.Texture2D art)
        {
            foreach (var (freshSlot, instanceId) in fresh)
            {
                if (freshSlot == slot)
                {
                    _marketStandIns[instanceId] = art;
                }
            }
        }

        private void DropStandIns(List<(int Slot, int InstanceId)> fresh)
        {
            bool dropped = false;
            foreach (var (_, instanceId) in fresh)
            {
                dropped |= _marketStandIns.Remove(instanceId);
            }
            if (dropped)
            {
                _renderedRevision = -1; // the slots show their empty sockets under the flights
            }
        }

        /// <summary>A deal flight delivered: the hidden refill card may show itself.</summary>
        private void ReleaseMarketCard(int instanceId)
        {
            _hiddenMarketIds.Remove(instanceId);
            _marketStandIns.Remove(instanceId);
            _renderedRevision = -1;
        }

        private UnityEngine.Texture2D ShelfArt(PlayerView.CardInfo card) =>
            card.DefId == null ? null : _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square);

        /// <summary>Art of a card on the shelf right now, by instance; null once it is gone.</summary>
        private UnityEngine.Texture2D MarketArt(int instanceId)
        {
            var card = View?.Market.FirstOrDefault(c => c.InstanceId == instanceId);
            return card == null ? null : ShelfArt(card);
        }
    }
}
