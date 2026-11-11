using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class EffectsPlayer
    {
        private const float EarningsHoldSeconds = 0.6f;
        /// <summary>Clear-table pause after one player's earnings land, before the next player's slide in.</summary>
        private const float EarningsBreathSeconds = 0.5f;

        /// <summary>
        /// The purchase choreography reversed: the pile slides in from off-screen right to the
        /// player's showcase spot, holds, then slides into their bar. Blocking, one player at a time.
        /// </summary>
        private void StartEarnings(EarnFx earn)
        {
            var arrive = FiringOnce(earn.OnArrived);

            var barWorld = _barWorldOf(earn.PlayerId);
            var fan = barWorld == null
                ? null
                : BuildBillPile(earn.Amount, earn.BillArt, $"+${earn.Amount}", GainColor);
            if (fan == null)
            {
                arrive(); // nothing to show: the cash just updates
                return;
            }
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
