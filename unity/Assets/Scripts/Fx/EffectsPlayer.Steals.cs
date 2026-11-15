using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Stolen money made visible: the bills leave the victim's bar under a grabbing hand, pause
    // beside the thief's bar where their purchases show, then slide into it.
    public sealed partial class EffectsPlayer
    {
        private const float StealCrossSeconds = 0.55f;
        private const float StealShowSeconds = 1f;
        private const float StealBreathSeconds = 0.2f;
        private const float StealHandSizePx = 110f;
        /// <summary>The hand's centre above the pile's: it reaches down over the top bill, as over a stolen card.</summary>
        private const float StealHandLiftPx = 62f;

        /// <summary>The grabbing hand that marks a steal; null leaves the bills to travel bare.</summary>
        public Texture2D GrabIcon;

        private void StartMoneySteal(StealFx steal)
        {
            var arrive = FiringOnce(steal.OnArrived);
            var victimBar = _barWorldOf(steal.FromPlayerId);
            var thiefBar = _barWorldOf(steal.ToPlayerId);
            var fan = victimBar == null || thiefBar == null
                ? null
                : BuildBillPile(steal.Amount, steal.BillArt, $"+${steal.Amount}", GainColor);
            if (fan == null)
            {
                arrive(); // nothing to show: the cash just updates
                return;
            }
            AddGrabbingHand((RectTransform)fan.transform);
            fan.From = _layer.InverseTransformPoint(victimBar.Value);
            fan.To = ShowcaseSpot(steal.ToPlayerId) + new Vector2(BillPileRightShiftPx, 0);
            fan.StartScale = PurchaseEndHeight / BillHeight;
            fan.ArriveDurationSeconds = StealCrossSeconds;
            fan.AutoFadeAfterSeconds = StealShowSeconds;
            fan.Destination = _layer.InverseTransformPoint(thiefBar.Value);
            fan.EndScale = PurchaseEndHeight / BillHeight;
            fan.OnArrive = arrive;
            ((RectTransform)fan.transform).anchoredPosition = fan.From;

            _busyUntil = Time.time + StealCrossSeconds + StealShowSeconds +
                BillFan.DepartSeconds + StealBreathSeconds;
            _endActive = () =>
            {
                if (fan != null)
                {
                    Object.Destroy(fan.gameObject); // skip: no orphaned pile
                }
                arrive(); // a skip still lands the money: the thief's cash must never stay behind
            };
        }

        /// <summary>Last child of the pile, so it draws over the bills and travels, shrinks and fades with them.</summary>
        private void AddGrabbingHand(RectTransform pile)
        {
            if (GrabIcon == null)
            {
                return;
            }
            var handGo = new GameObject("GrabbingHand", typeof(RectTransform), typeof(RawImage));
            handGo.transform.SetParent(pile, false);
            var hand = (RectTransform)handGo.transform;
            hand.anchorMin = hand.anchorMax = new Vector2(0.5f, 0.5f);
            hand.sizeDelta = new Vector2(StealHandSizePx, StealHandSizePx);
            hand.anchoredPosition = new Vector2(0f, StealHandLiftPx);
            var handImage = handGo.GetComponent<RawImage>();
            handImage.texture = GrabIcon;
            handImage.raycastTarget = false;
        }
    }
}
