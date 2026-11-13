using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Arcing hop between two table anchors (stolen coins, flying cards).</summary>
    public sealed class FlightMotion : MonoBehaviour
    {
        public Vector2 From;
        public Vector2 To;
        public float Delay;
        public float Duration = 0.55f;
        public float ArcHeight = 110f;
        public float EndScale = 1f;
        /// <summary>Sit visible at From through the delay (a card waiting on the shelf) instead of hidden.</summary>
        public bool VisibleWhileWaiting;
        /// <summary>Fraction of the flight after which the card fades to nothing by arrival; 1 = no fade (default).</summary>
        public float FadeOutFrom = 1f;
        /// <summary>Stay put at To this long after arriving, so whatever replaces the card underneath is drawn before it goes.</summary>
        public float LingerAfterArrivalSeconds;
        /// <summary>Fires once when the delay elapses; late-bind To here if the layout may have moved meanwhile.</summary>
        public System.Action OnStart;
        public System.Action OnArrive;

        private RectTransform _rect;
        private CanvasGroup _group;
        private float _elapsed;
        private bool _started;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            // A chip that already fades (a pile card flying home) keeps its group: Unity refuses a second.
            _group = GetComponent<CanvasGroup>();
            if (_group == null)
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }
            _group.blocksRaycasts = false;
            _group.alpha = VisibleWhileWaiting ? 1f : 0f;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float progress = (_elapsed - Delay) / Duration;
            if (progress < 0f)
            {
                if (VisibleWhileWaiting)
                {
                    // Awake ran before the flag was set: show the card where it waits.
                    _group.alpha = 1f;
                    _rect.anchoredPosition = From;
                }
                return;
            }
            if (!_started)
            {
                _started = true;
                OnStart?.Invoke();
            }
            if (progress >= 1f)
            {
                _rect.anchoredPosition = To;
                transform.localScale = Vector3.one * EndScale;
                _group.alpha = FadeOutFrom >= 1f ? 1f : 0f;
                OnArrive?.Invoke();
                enabled = false;
                Destroy(gameObject, LingerAfterArrivalSeconds);
                return;
            }
            float fadeOut = FadeOutFrom >= 1f
                ? 1f
                : 1f - Mathf.Clamp01((progress - FadeOutFrom) / (1f - FadeOutFrom));
            _group.alpha = Mathf.Min(VisibleWhileWaiting ? 1f : Mathf.Clamp01(progress * 6f), fadeOut);
            float eased = progress * progress * (3f - 2f * progress);
            var position = Vector2.Lerp(From, To, eased);
            position.y += ArcHeight * 4f * progress * (1f - progress);
            _rect.anchoredPosition = position;
            transform.localScale = Vector3.one * Mathf.Lerp(1f, EndScale, eased);
        }
    }
}
