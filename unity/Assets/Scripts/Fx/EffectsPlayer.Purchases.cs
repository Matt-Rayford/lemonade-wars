using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class EffectsPlayer
    {
        /// <summary>The height a purchased card or a bill pile shrinks to at a bar: a little under the 82px player bar.</summary>
        private const float PurchaseEndHeight = 60f;
        /// <summary>How long the viewer's own money sits on the table before fading on its own.</summary>
        private const float OwnMoneyHoldSeconds = 0.6f;
        /// <summary>Clear-table pause after the viewer's money has faded, before bots may act.</summary>
        private const float OwnMoneyBreathSeconds = 0.35f;

        /// <summary>
        /// Blocking, so back-to-back bot purchases play one at a time. Delivery fires exactly
        /// once on every exit path (leaving the showcase, skip, Clear): a suppressed refill slot depends on it.
        /// </summary>
        private void StartPurchase(PurchaseFx purchase)
        {
            var deliver = FiringOnce(purchase.OnDelivered);

            purchase.OnStarted?.Invoke();
            if (!purchase.ShowCard)
            {
                StartOwnPurchaseMoney(purchase, deliver);
                return;
            }
            var originWorld = purchase.Origin?.Invoke();
            if (originWorld == null || purchase.Art == null)
            {
                deliver(); // nothing on screen to lift from: no motion, no busy window
                return;
            }
            StartPurchaseJourney(purchase, originWorld.Value, deliver);
        }

        /// <summary>
        /// Only the money shows, but it still holds the table through its fade and a breath
        /// after: bots act the moment the theatre is idle, and would land a purchase on your bills.
        /// </summary>
        private void StartOwnPurchaseMoney(PurchaseFx purchase, System.Action deliver)
        {
            var fan = BuildPurchasePile(purchase, ShowcaseSpot(purchase.BuyerId));
            deliver();
            if (fan == null)
            {
                return;
            }
            fan.AutoFadeAfterSeconds = OwnMoneyHoldSeconds;
            _busyUntil = Time.time + BillFan.ArriveSeconds + OwnMoneyHoldSeconds +
                BillFan.FadeSeconds + OwnMoneyBreathSeconds;
            _endActive = () =>
            {
                if (fan != null)
                {
                    Object.Destroy(fan.gameObject); // skip: no orphaned pile
                }
            };
        }

        private void StartPurchaseJourney(PurchaseFx purchase, Vector3 originWorld, System.Action deliver)
        {
            var chip = BuildShelfChip(purchase.Art);
            chip.anchoredPosition = _layer.InverseTransformPoint(originWorld);
            chip.SetAsLastSibling(); // over the shelf card now shifted into this slot

            var showcase = ShowcaseSpot(purchase.BuyerId);
            // The bills are built before the chip is raised again, so the card lands on top of its money.
            var fan = BuildPurchasePile(purchase, showcase);
            chip.SetAsLastSibling();

            var journey = chip.gameObject.AddComponent<PurchaseJourney>();
            journey.Delay = fan != null ? BillFan.ArriveSeconds : 0f;
            journey.Showcase = showcase;
            journey.EndScale = PurchaseEndHeight / ShelfCardHeight;
            journey.Destination = () => PurchaseDestination(purchase.BuyerId, journey.Showcase);
            journey.OnLeaveShowcase = () =>
            {
                deliver(); // the shelf is visibly empty by now
                if (fan != null)
                {
                    fan.BeginFade(); // the money is spent: it goes as the card goes
                }
            };
            journey.OnDone = () => { };

            _busyUntil = Time.time + journey.Delay + PurchaseJourney.TotalSeconds;
            _endActive = () =>
            {
                if (chip != null)
                {
                    Object.Destroy(chip.gameObject); // skip: no orphaned drift
                }
                if (fan != null)
                {
                    Object.Destroy(fan.gameObject);
                }
                deliver(); // a skip still delivers: the refill must never stay hidden
            };
        }

        private BillFan BuildPurchasePile(PurchaseFx purchase, Vector2 showcase) =>
            BuildPurchasePile(purchase.BuyerId, purchase.Price, purchase.BillArt, showcase);

        /// <summary>The bills that paid, growing out of the buyer's bar. Null when there is nothing to show.</summary>
        private BillFan BuildPurchasePile(int buyerId, int price, System.Func<int, Texture2D> billArt, Vector2 showcase)
        {
            var barWorld = _barWorldOf(buyerId);
            if (barWorld == null)
            {
                return null;
            }
            var fan = BuildBillPile(price, billArt, $"-${price}", LossColor);
            if (fan == null)
            {
                return null;
            }
            fan.From = _layer.InverseTransformPoint(barWorld.Value);
            fan.To = showcase + new Vector2(BillPileRightShiftPx, 0);
            fan.StartScale = PurchaseEndHeight / BillHeight;
            ((RectTransform)fan.transform).anchoredPosition = fan.From;
            return fan;
        }

        /// <summary>The buyer's bar in layer space; a bar that is off-screen keeps the card at the showcase.</summary>
        private Vector2 PurchaseDestination(int buyerId, Vector2 fallback)
        {
            var world = _barWorldOf(buyerId);
            return world == null ? fallback : (Vector2)_layer.InverseTransformPoint(world.Value);
        }
    }
}
