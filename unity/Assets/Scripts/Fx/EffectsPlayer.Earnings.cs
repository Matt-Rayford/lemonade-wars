using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class EffectsPlayer
    {
        private const float EarningsHoldSeconds = 0.6f;
        /// <summary>Each further card dealt by the same roll peeks out this far from under the one above.</summary>
        private const float RiderCardFanPx = 18f;
        /// <summary>Clear-table pause after one player's earnings land, before the next player's slide in.</summary>
        private const float EarningsBreathSeconds = 0.5f;

        /// <summary>
        /// The purchase choreography reversed: the pile slides in from off-screen right to the
        /// player's showcase spot, holds, then slides into their bar. Blocking, one player at a time.
        /// </summary>
        /// <summary>Children of the pile, so they slide, shrink and fade with the money, like a bought card over its bills.</summary>
        private static void AddRiderCards(RectTransform pile, Texture2D cardArt, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var image = UiKit.CreateCardImage(pile, cardArt, ShelfCardWidth, ShelfCardHeight);
                image.raycastTarget = false;
                var card = (RectTransform)image.transform.parent;
                card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
                card.pivot = new Vector2(0.5f, 0.5f);
                // Back at the showcase spot, which the pile itself sits right of.
                card.anchoredPosition = new Vector2(-BillPileRightShiftPx + i * RiderCardFanPx, -i * RiderCardFanPx);
            }
        }

        private void StartEarnings(EarnFx earn)
        {
            var arrive = FiringOnce(earn.OnArrived);

            var barWorld = _barWorldOf(earn.PlayerId);
            var fan = barWorld == null
                ? null
                : BuildBillPile(earn.Amount, earn.BillArt, $"+${earn.Amount}", GainColor)
                  ?? (earn.RiderCardCount > 0 ? CreateEmptyPile().gameObject.AddComponent<BillFan>() : null);
            if (fan == null)
            {
                arrive(); // nothing to show: the cash just updates
                return;
            }
            AddRiderCards((RectTransform)fan.transform, earn.RiderCardArt, earn.RiderCardCount);
            var showcase = ShowcaseSpot(earn.PlayerId) + new Vector2(BillPileRightShiftPx, 0);
            fan.GrowAndFadeIn = false;
            fan.From = new Vector2(_layer.rect.xMax + BillWidth, showcase.y);
            fan.To = showcase;
            fan.AutoFadeAfterSeconds = EarningsHoldSeconds;
            fan.Destination = _layer.InverseTransformPoint(barWorld.Value);
            fan.EndScale = PurchaseEndHeight / BillHeight;
            fan.OnArrive = arrive;
            ((RectTransform)fan.transform).anchoredPosition = fan.From;

            _busyUntil = Time.time + BillFan.ArriveSeconds + EarningsHoldSeconds +
                BillFan.DepartSeconds + EarningsBreathSeconds;
            _endActive = () =>
            {
                if (fan != null)
                {
                    Object.Destroy(fan.gameObject); // skip: no orphaned pile
                }
                arrive(); // a skip still lands the money: the cash must never stay behind
            };
        }
    }
}
