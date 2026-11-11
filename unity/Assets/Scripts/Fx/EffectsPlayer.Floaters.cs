using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class EffectsPlayer
    {
        private static readonly Color ToastColor = new Color(1f, 0.92f, 0.55f);
        private const float ToastHoldSeconds = 1.0f;
        private const float MoneyFloaterStackSpacingPx = 26f;
        private const float MoneyFloaterStaggerSeconds = 0.12f;

        /// <param name="stackIndex">Position among floaters released together for the same player.</param>
        private void SpawnMoney(int playerId, int amount, int stackIndex)
        {
            // From the cash icon, not the bar's middle: the number reads as the cash figure
            // changing, and a loss does not appear over the hand count.
            var world = _cashIconWorldOf(playerId) ?? _barWorldOf(playerId);
            if (world == null)
            {
                return;
            }
            var text = UiKit.CreateText(_layer,
                (amount > 0 ? "+$" : "-$") + Mathf.Abs(amount), 26,
                TextAnchor.MiddleCenter, amount > 0 ? GainColor : LossColor, body: true);
            text.raycastTarget = false;
            text.fontStyle = TMPro.FontStyles.Bold;
            UiKit.AddTextShadow(text);

            var rect = (RectTransform)text.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(220, 40);
            Vector2 local = _layer.InverseTransformPoint(world.Value);
            rect.anchoredPosition = local + new Vector2(0, stackIndex * MoneyFloaterStackSpacingPx);

            var motion = text.gameObject.AddComponent<FloaterMotion>();
            motion.Delay = stackIndex * MoneyFloaterStaggerSeconds;
        }

        private void SpawnToast(string content)
        {
            var text = UiKit.CreateText(_layer, content, 44, TextAnchor.MiddleCenter, ToastColor);
            text.raycastTarget = false;
            UiKit.AddTextShadow(text, 1.3f);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1400, 70);
            rect.anchoredPosition = new Vector2(0, 140f);
            text.gameObject.AddComponent<PopIn>();

            var motion = text.gameObject.AddComponent<FloaterMotion>();
            motion.RiseSpeed = 18f;
            motion.FadeStart = 0.9f;
            motion.Lifetime = 1.4f;
        }
    }
}
