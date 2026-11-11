using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // World positions on the board that flights and glides aim at.
    public sealed partial class TableView
    {
        /// <summary>The board row's HorizontalLayoutGroup gap (UiKit.CreateCardRow).</summary>
        private const float BoardRowSpacingPx = 8f;

        /// <summary>World centre of your turf (null id) or a stand's card: where a shelf card glides to.
        /// Null when the board shown is a rival's.</summary>
        public Vector3? DropCellWorld(int? standInstanceId)
        {
            foreach (var (id, cell, _) in _dropCells)
            {
                if (Equals(id, standInstanceId))
                {
                    return BoardCardWorld(cell);
                }
            }
            return null;
        }

        /// <summary>One cell past the last stand (past the turf on an empty row): where a bought stand
        /// glides when there is only one place it can go.</summary>
        public Vector3? NextStandSlotWorld()
        {
            var last = _standCells.Count > 0 ? _standCells[_standCells.Count - 1] : _turfCell;
            if (last == null)
            {
                return null;
            }
            var lastCenter = BoardCardWorld(last);
            if (lastCenter == null)
            {
                return null;
            }
            var nextCenter = last.TransformPoint(new Vector3(
                last.rect.xMax + BoardRowSpacingPx + last.rect.width / 2f, 0f, 0f));
            return new Vector3(nextCenter.x, lastCenter.Value.y, lastCenter.Value.z);
        }

        /// <summary>World centre of a tucked card's full rect (mostly behind its stand's front card); null when not on the viewed board.</summary>
        public Vector3? EquippedCardWorld(int instanceId)
        {
            foreach (var (id, strip, _) in _equipCells)
            {
                if (id == instanceId && strip != null && strip.parent is RectTransform card)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_boardRow);
                    return card.TransformPoint(card.rect.center);
                }
            }
            return null;
        }

        public Vector3? VictoryCellWorld() => _vpCell != null ? BoardCardWorld(_vpCell) : null;

        /// <summary>A board cell was often built this very frame: settle the row first.</summary>
        private Vector3? BoardCardWorld(RectTransform cell)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_boardRow);
            var cardFrame = cell.Find("Card") as RectTransform ?? cell;
            return cardFrame.TransformPoint(cardFrame.rect.center);
        }

        /// <summary>
        /// Where a bought or staged card pauses: just right of the player column, level with the buyer's bar.
        /// Measured from the static column because the board row is rebuilt, and unplaced, on every render.
        /// </summary>
        public Vector3? PurchaseShowcaseWorld(int buyerId)
        {
            var bar = PlayerBarWorld(buyerId);
            if (bar == null)
            {
                return null;
            }
            float columnRightX = _playersColumn.TransformPoint(new Vector3(_playersColumn.rect.xMax, 0, 0)).x;
            float cardCentreX = columnRightX + BoardCardWidthPx / 2f;
            return new Vector3(cardCentreX, bar.Value.y, 0);
        }
    }
}
