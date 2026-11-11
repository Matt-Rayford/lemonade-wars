using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// A purchased card's trip from the shelf to its buyer: it slides to a showcase spot
    /// beside the buyer's bar, holds there to be read, then slides into the bar shrinking
    /// and fading. OnDone fires only on the natural end; early teardown is the owner's job.
    /// </summary>
    public sealed class PurchaseJourney : MonoBehaviour
    {
        public const float TotalSeconds = 1.5f;
        private const float SlideSeconds = 0.3f;
        private const float ShowcaseUntilSeconds = 1.0f;
        /// <summary>Fade through the back half of the flight, so the shrink reads first.</summary>
        private const float FadeStartFraction = 0.5f;

        /// <summary>Seconds the card waits on its shelf before sliding (the money arrives first).</summary>
        public float Delay;
        public Vector2 Showcase;
        /// <summary>Resolved when the flight begins — the buyer's bar may have re-laid-out.</summary>
        public System.Func<Vector2> Destination;
        public float EndScale = 0.28f;
        /// <summary>Fires once, when the hold ends and the card starts for the bar.</summary>
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
            float sinceDelay = _elapsed - Delay;
            if (sinceDelay < 0f)
            {
                return; // still on the shelf while the money comes out
            }
            if (sinceDelay < ShowcaseUntilSeconds)
            {
                Slide(Mathf.Clamp01(sinceDelay / SlideSeconds));
                return;
            }
            if (!_flying)
            {
                _flying = true;
                _to = Destination?.Invoke() ?? Showcase;
                OnLeaveShowcase?.Invoke();
            }
            float flight = Mathf.Clamp01(
                (sinceDelay - ShowcaseUntilSeconds) / (TotalSeconds - ShowcaseUntilSeconds));
            Fly(flight);
            if (flight >= 1f)
            {
                OnDone?.Invoke();
                Destroy(gameObject);
            }
        }

        private void Slide(float progress)
        {
            float eased = 1f - (1f - progress) * (1f - progress); // fast lift, gentle settle
            _rect.anchoredPosition = Vector2.Lerp(_from, Showcase, eased);
        }

        private void Fly(float progress)
        {
            float eased = progress * progress * (3f - 2f * progress);
            _rect.anchoredPosition = Vector2.Lerp(Showcase, _to, eased);
            transform.localScale = Vector3.one * Mathf.Lerp(1f, EndScale, eased);
            _group.alpha = progress < FadeStartFraction
                ? 1f
                : 1f - (progress - FadeStartFraction) / (1f - FadeStartFraction);
        }
    }
}
