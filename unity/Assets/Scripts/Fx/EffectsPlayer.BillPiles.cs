using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public sealed partial class EffectsPlayer
    {
        private const float BillWidth = 207f;
        private const float BillHeight = 148f;
        private const int PileAmountFontSize = 40;
        /// <summary>In TMP font-relative units.</summary>
        private const float PileAmountOutlineWidth = 0.25f;
        /// <summary>The amount sits this far right of the top bill's top-right corner, and this far below it.</summary>
        private const float PileAmountGapPx = 10f;
        private const float PileAmountDropPx = 18f;
        /// <summary>The pile lands this far right of the showcase spot, so a card there doesn't sit squarely on it.</summary>
        private const float BillPileRightShiftPx = 50f;
        private const float BillBaseTiltDegrees = 8f;
        /// <summary>
        /// How far a lower bill pokes out from under the one above, as a share of the bill's
        /// half-extent in that direction: enough to read its denomination, still one pile.
        /// </summary>
        private const float BillPeekMin = 0.45f;
        private const float BillPeekMax = 0.75f;
        /// <summary>Scatter around an even spread of the directions lower bills are pushed out in.</summary>
        private const float BillDirectionJitterDegrees = 25f;
        /// <summary>Relative to the pile's base tilt.</summary>
        private const float BillTurnDegrees = 40f;
        /// <summary>Neighbouring bills never sit this close in angle, or the pile reads as one flat stack.</summary>
        private const float BillMinTurnGapDegrees = 10f;

        /// <summary>
        /// A fanned pile of bills for an amount, largest on top, with the amount written
        /// beside it. Positioned by the caller. Null for nothing to show.
        /// </summary>
        private BillFan BuildBillPile(int amount, System.Func<int, Texture2D> billArt, string label, Color labelColor)
        {
            var bills = BillBreakdown.Bills(amount);
            if (bills.Count == 0 || billArt == null)
            {
                return null;
            }
            var root = new GameObject("BillFan", typeof(RectTransform));
            root.transform.SetParent(_layer, false);
            var pile = (RectTransform)root.transform;
            pile.anchorMin = pile.anchorMax = new Vector2(0.5f, 0.5f);
            pile.pivot = new Vector2(0.5f, 0.5f);
            pile.sizeDelta = new Vector2(BillWidth, BillHeight);
            var placements = PilePlacements(bills.Count);
            // Smallest first, so the largest denomination ends up drawn on top.
            for (int i = bills.Count - 1; i >= 0; i--)
            {
                var art = billArt(bills[i]);
                if (art == null)
                {
                    continue;
                }
                AddBillToPile(pile, bills[i], art, placements[i]);
            }
            AddPileAmountLabel(pile, label, labelColor, placements);
            return root.AddComponent<BillFan>();
        }

        /// <param name="placement">Offset in x and y, turn in z.</param>
        private static void AddBillToPile(RectTransform pile, int denomination, Texture2D art, Vector3 placement)
        {
            var billGo = new GameObject($"Bill{denomination}", typeof(RectTransform), typeof(RawImage));
            billGo.transform.SetParent(pile, false);
            var bill = (RectTransform)billGo.transform;
            bill.anchorMin = bill.anchorMax = new Vector2(0.5f, 0.5f);
            bill.sizeDelta = new Vector2(BillWidth, BillHeight);
            bill.anchoredPosition = new Vector2(placement.x, placement.y);
            bill.localEulerAngles = new Vector3(0, 0, placement.z);
            var image = billGo.GetComponent<RawImage>();
            image.texture = art;
            image.raycastTarget = false;
        }

        /// <summary>
        /// A child of the pile, so it moves and fades with the money. Hung off the TOP BILL's
        /// corner: a lower bill turned hard can push the pile's bounding box into empty table.
        /// </summary>
        private static void AddPileAmountLabel(RectTransform pile, string text, Color color, Vector3[] placements)
        {
            var corner = TopRightCorner(placements[0]);
            var label = UiKit.CreateText(pile, text, PileAmountFontSize, TextAnchor.MiddleLeft, color);
            label.raycastTarget = false;
            label.fontStyle = TMPro.FontStyles.Bold;
            UiKit.AddTextOutline(label, PileAmountOutlineWidth, Color.black); // big type over busy art
            var rect = (RectTransform)label.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(200f, 72f); // generous: an outline must never be clipped by its own box
            rect.anchoredPosition = corner + new Vector2(PileAmountGapPx, -PileAmountDropPx);
        }

        /// <summary>Where a bill's top-right corner ends up after its turn.</summary>
        private static Vector2 TopRightCorner(Vector3 placement)
        {
            float radians = placement.z * Mathf.Deg2Rad;
            float x = BillWidth / 2f;
            float y = BillHeight / 2f;
            return new Vector2(
                placement.x + x * Mathf.Cos(radians) - y * Mathf.Sin(radians),
                placement.y + x * Mathf.Sin(radians) + y * Mathf.Cos(radians));
        }

        /// <summary>
        /// A fresh random pile: offset (x, y) and turn (z) per bill, top bill first. The top
        /// bill sits at the centre; each lower one is pushed out far enough to show a corner number.
        /// </summary>
        private static Vector3[] PilePlacements(int count)
        {
            var placements = new Vector3[count];
            float baseTilt = Random.Range(-BillBaseTiltDegrees, BillBaseTiltDegrees);
            placements[0] = new Vector3(0f, 0f, baseTilt);
            float firstDirection = Random.Range(0f, 360f);
            float spread = 360f / Mathf.Max(2, count - 1);
            for (int i = 1; i < count; i++)
            {
                float direction = firstDirection + (i - 1) * spread +
                    Random.Range(-BillDirectionJitterDegrees, BillDirectionJitterDegrees);
                var unit = new Vector2(Mathf.Cos(direction * Mathf.Deg2Rad), Mathf.Sin(direction * Mathf.Deg2Rad));
                // A rectangle's reach in a direction is the sum of its half-sides' projections.
                float reach = BillWidth / 2f * Mathf.Abs(unit.x) + BillHeight / 2f * Mathf.Abs(unit.y);
                var offset = unit * (reach * Random.Range(BillPeekMin, BillPeekMax));
                float turn = baseTilt + Random.Range(-BillTurnDegrees, BillTurnDegrees);
                float above = placements[i - 1].z;
                if (Mathf.Abs(Mathf.DeltaAngle(turn, above)) < BillMinTurnGapDegrees)
                {
                    turn = above + Mathf.Sign(turn - above + 0.01f) * BillMinTurnGapDegrees;
                }
                placements[i] = new Vector3(offset.x, offset.y, turn);
            }
            return placements;
        }
    }
}
