using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The Timeout card, slapped askew in the middle of the table for everyone while
    // the fine is owed; the message strip above it says who pays what.
    public sealed partial class TableView
    {
        private const float TimeoutCardWidthPx = 190f;
        private const float TimeoutCardHeightPx = 266f;
        private static readonly Vector2 TimeoutCardShadowOffsetPx = new Vector2(8f, -10f);
        private static readonly Color TimeoutCardShadowColor = new Color(0f, 0f, 0f, 0.55f);

        public bool TimeoutCardShown => _timeoutCard != null;

        private RectTransform _timeoutCard;

        /// <summary>Idempotent per render: the card lands once and lies still across re-renders.</summary>
        public void ShowTimeoutCard(Texture2D art)
        {
            if (_timeoutCard != null)
            {
                return;
            }
            var image = UiKit.CreateCardImage(_canvasRoot, art, TimeoutCardWidthPx, TimeoutCardHeightPx);
            image.raycastTarget = false;
            var shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = TimeoutCardShadowColor;
            shadow.effectDistance = TimeoutCardShadowOffsetPx;
            var frame = (RectTransform)image.transform.parent;
            frame.name = "TimeoutCard";
            frame.GetComponent<Image>().raycastTarget = false;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(TimeoutCardWidthPx, TimeoutCardHeightPx);
            frame.anchoredPosition = Vector2.zero;
            frame.gameObject.AddComponent<TimeoutCardMotion>();
            frame.SetAsLastSibling();
            _timeoutCard = frame;
        }

        public void HideTimeoutCard()
        {
            if (_timeoutCard == null)
            {
                return;
            }
            Object.Destroy(_timeoutCard.gameObject);
            _timeoutCard = null;
        }
    }
}
