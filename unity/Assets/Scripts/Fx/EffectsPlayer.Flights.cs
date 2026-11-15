using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public sealed partial class EffectsPlayer
    {
        private const float CardFlightSeconds = 0.55f;
        private const float CardFlightStaggerSeconds = 0.15f;

        /// <summary>Stolen or traded cards, face-down, bar to bar.</summary>
        private void StartCardFlight(FlyFx fly)
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
                var motion = BuildCardChip(fly.CardBack).gameObject.AddComponent<FlightMotion>();
                motion.From = from;
                motion.To = to;
                motion.Delay = i * CardFlightStaggerSeconds;
                motion.Duration = CardFlightSeconds;
            }
            _busyUntil = Time.time + CardFlightSeconds + CardFlightStaggerSeconds * (fly.Count - 1) + 0.1f;
            _endActive = () => { };
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
