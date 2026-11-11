using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public sealed partial class EffectsPlayer
    {
        private const float StealFlightSeconds = 0.55f;
        private const float StealFlightStaggerSeconds = 0.15f;

        private void StartStealFlight(FlyFx fly)
        {
            var fromWorld = _barWorldOf(fly.FromPlayerId);
            var toWorld = _barWorldOf(fly.ToPlayerId);
            if (fromWorld == null || toWorld == null)
            {
                return;
            }
            Vector2 from = _layer.InverseTransformPoint(fromWorld.Value);
            Vector2 to = _layer.InverseTransformPoint(toWorld.Value);

            for (int i = 0; i < fly.Count; i++)
            {
                var chip = fly.CardBack != null ? BuildCardChip(fly.CardBack) : BuildCoinChip();
                var motion = chip.gameObject.AddComponent<FlightMotion>();
                motion.From = from;
                motion.To = to;
                motion.Delay = i * StealFlightStaggerSeconds;
                motion.Duration = StealFlightSeconds;
                bool isLastCoin = fly.CardBack == null && i == fly.Count - 1;
                if (isLastCoin)
                {
                    int thief = fly.ToPlayerId;
                    int victim = fly.FromPlayerId;
                    int amount = fly.Amount;
                    motion.OnArrive = () =>
                    {
                        SpawnMoney(thief, amount, 0);
                        SpawnMoney(victim, -amount, 0);
                    };
                }
            }
            _busyUntil = Time.time + StealFlightSeconds + StealFlightStaggerSeconds * (fly.Count - 1) + 0.1f;
            _endActive = () => { };
        }

        private RectTransform BuildCoinChip()
        {
            var go = new GameObject("Coin", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_layer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(46, 46);
            var image = go.GetComponent<Image>();
            image.sprite = UiSprites.Circle;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = UiKit.ButtonColor;
            image.raycastTarget = false;
            var label = UiKit.CreateText(go.transform, "$", 26,
                TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
            label.raycastTarget = false;
            UiKit.Anchor((RectTransform)label.transform, Vector2.zero, Vector2.one);
            return rect;
        }

        private RectTransform BuildCardChip(Texture2D back)
        {
            var image = UiKit.CreateCardImage(_layer, back, 66, 92);
            image.raycastTarget = false;
            var frame = (RectTransform)image.transform.parent;
            frame.GetComponent<Image>().raycastTarget = false;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(66, 92);
            return frame;
        }
    }
}
