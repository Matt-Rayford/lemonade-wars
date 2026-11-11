using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Buying from the shelf by drag-and-drop or double-click: Black Market
    /// cards, stands from the supply piles, and Bragging Rights.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private bool TryGetMarketMoves(int marketIndex, out List<GameAction> moves)
        {
            moves = null;
            var groups = CurrentGroups();
            return groups != null && groups.MarketMoves.TryGetValue(marketIndex, out moves);
        }

        private bool TryGetSupplyMoves(string standTypeId, out List<GameAction> moves)
        {
            moves = null;
            var groups = CurrentGroups();
            return groups != null && groups.SupplyMoves.TryGetValue(standTypeId, out moves);
        }

        private void OnMarketDragStart(int marketIndex)
        {
            _preview.SetDragging(true);
            if (!TryGetMarketMoves(marketIndex, out var moves))
            {
                return;
            }
            var valid = new HashSet<int?>(moves.OfType<BuyBlackMarket>()
                .Select(m => m.TargetStandInstanceId));
            _table.SetValidDropTargets(valid);
        }

        private void OnMarketDrop(int marketIndex, int? standInstanceId)
        {
            _table.ClearDropHighlights();
            if (!TryGetMarketMoves(marketIndex, out var moves))
            {
                return;
            }
            var matching = moves.OfType<BuyBlackMarket>()
                .Where(m => m.TargetStandInstanceId == standInstanceId)
                .Cast<GameAction>()
                .ToList();
            var card = View.Market[marketIndex];
            if (matching.Count == 1)
            {
                if (_liftedMarketIds.Contains(card.InstanceId) && _liftedCardLanding == null)
                {
                    _liftedCardLanding = Input.mousePosition; // a held card buys from under the cursor
                }
                Submit(matching[0]);
            }
            else
            {
                // A question (or nothing to buy) in between: the card waits on its slot.
                RestoreMarketCard(card.InstanceId);
                if (matching.Count > 1)
                {
                    _prompt.Show("That slot is full — replace which card?",
                        new[] { _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square) },
                        ToOptions(matching), showCancel: true);
                }
            }
        }

        /// <summary>One legal home: the card glides from its slot to that stand and the buy lands on
        /// arrival. Several: it rides the cursor and the next click on a lit stand places it.</summary>
        private void OnMarketDoubleClick(int marketIndex)
        {
            _preview.Hide();
            if (!TryGetMarketMoves(marketIndex, out var moves))
            {
                return;
            }
            var card = View.Market[marketIndex];
            var art = _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square);
            var destinations = DoubleClickBuyDestinations(moves.OfType<BuyBlackMarket>().ToList());
            ShowOwnBoard();
            // Measured while the card still sits in its slot, which then empties: the card
            // itself leaves the shelf, not a copy of it.
            var slotWorld = _table.MarketSlotWorld(marketIndex);
            int instanceId = card.InstanceId;
            LiftMarketCard(instanceId);
            if (destinations.Count == 1)
            {
                int? destination = destinations[0];
                var landing = _table.DropCellWorld(destination);
                _table.GlideShelfCard(art, slotWorld, landing, () =>
                {
                    _liftedCardLanding = landing;
                    OnMarketDrop(marketIndex, destination);
                });
                return;
            }
            _table.OnStickyReturned = () => RestoreMarketCard(instanceId);
            _table.BeginMarketStickyDrag(marketIndex, art);
        }

        /// <summary>A stand with room decides it, even when full stands would also take the card by
        /// replacing something: nobody double-clicks to trash a card.</summary>
        private static List<int?> DoubleClickBuyDestinations(List<BuyBlackMarket> buys)
        {
            var withRoom = buys.Where(b => b.ReplaceInstanceId == null)
                .Select(b => b.TargetStandInstanceId).Distinct().ToList();
            return withRoom.Count > 0
                ? withRoom
                : buys.Select(b => b.TargetStandInstanceId).Distinct().ToList();
        }

        /// <summary>One place it can go (your first stand): it glides straight there. Otherwise it rides
        /// the cursor until a click places it in your row, exactly where a drag would have dropped it.</summary>
        private void OnSupplyDoubleClick(string standTypeId)
        {
            _preview.Hide();
            if (!TryGetSupplyMoves(standTypeId, out var moves))
            {
                return;
            }
            ShowOwnBoard();
            if (moves.Count == 1)
            {
                var onlyMove = moves[0];
                _table.GlideShelfCard(_table.SupplyArtOf(standTypeId), _table.StandSupplyWorld(standTypeId),
                    _table.NextStandSlotWorld(), () => Submit(onlyMove));
                return;
            }
            _table.BeginSupplyStickyDrag(standTypeId);
        }

        private void OnSupplyDrop(string standTypeId, int insertIndex)
        {
            if (!TryGetSupplyMoves(standTypeId, out var moves))
            {
                return;
            }
            var pick = moves.FirstOrDefault(m =>
                    (m as BuyStand)?.InsertIndex == insertIndex ||
                    (m as InitialBuyStand)?.InsertIndex == insertIndex)
                ?? moves[moves.Count - 1];
            Submit(pick);
        }

        private void OnBraggingDoubleClick()
        {
            _preview.Hide();
            if (CurrentGroups()?.BraggingMoves.Count > 0 != true)
            {
                return;
            }
            ShowOwnBoard();
            _table.GlideShelfCard(_table.BraggingArt, _table.BraggingShelfWorld(),
                _table.VictoryCellWorld(), BuyBraggingRights);
        }

        private void BuyBraggingRights()
        {
            var moves = CurrentGroups()?.BraggingMoves;
            if (moves != null && moves.Count > 0)
            {
                Submit(moves[0]);
            }
        }
    }
}
