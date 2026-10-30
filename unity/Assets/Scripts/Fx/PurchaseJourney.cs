using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// A purchased card's trip from the shelf to its buyer: slides down to a showcase
    /// spot, holds there so the table can read it, then flies to the buyer's player
    /// bar while shrinking to badge size and fading out. Two seconds end to end.
    /// OnLeaveShowcase fires once when the flight begins; OnDone only on the natural
    /// end — early teardown is the owner's job (EndActive).
    /// </summary>
    public sealed class PurchaseJourney : MonoBehaviour
    {
        public const float TotalSeconds = 2.0f;
        private const float SlideSeconds = 0.3f;
        private const float ShowcaseUntilSeconds = 1.0f;
        /// <summary>Fade through the back half of the flight, so the shrink reads first.</summary>
        private const float FadeStartFraction = 0.5f;

        public Vector2 Showcase;
        /// <summary>Resolved when the flight begins — the buyer's bar may have re-laid-out.</summary>
        public System.Func<Vector2> Destination;
        /// <summary>Scale on arrival: the card ends a little shorter than a player bar.</summary>
        public float EndScale = 0.28f;
        public System.Action OnLeaveShowcase;
        public System.Action OnDone;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Vector2 _from;
        private Vector2 _to;
        private bool _started;
        private bool _flying;
        private float _elapsed;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
        }

        private void Update()
        {
            if (!_started)
            {
                _started = true;
                _from = _rect.anchoredPosition; // set by the spawner after Awake
            }
            _elapsed += Time.deltaTime;
            if (_elapsed < ShowcaseUntilSeconds)
            {
                Slide(Mathf.Clamp01(_elapsed / SlideSeconds));
                return;
            }
            if (!_flying)
            {
                _flying = true;
                _to = Destination?.Invoke() ?? Showcase;
                OnLeaveShowcase?.Invoke();
            }
            float flight = Mathf.Clamp01((_elapsed - ShowcaseUntilSeconds) / (TotalSeconds - ShowcaseUntilSeconds));
            Fly(flight);
            if (flight >= 1f)
            {
                OnDone?.Invoke();
                Destroy(gameObject);
            }
        }

        private void Slide(float t)
        {
            float eased = 1f - (1f - t) * (1f - t); // fast lift, gentle settle
            _rect.anchoredPosition = Vector2.Lerp(_from, Showcase, eased);
        }

        private void Fly(float t)
        {
            float eased = t * t * (3f - 2f * t);
            _rect.anchoredPosition = Vector2.Lerp(Showcase, _to, eased);
            transform.localScale = Vector3.one * Mathf.Lerp(1f, EndScale, eased);
            _group.alpha = t < FadeStartFraction ? 1f : 1f - (t - FadeStartFraction) / (1f - FadeStartFraction);
        }
    }
}
