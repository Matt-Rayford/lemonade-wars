using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// A hand of bills slapped onto the table: slides in to the showcase spot (from
    /// the buyer's bar, growing and fading in — the purchased card's departure
    /// reversed — or from off-screen for earnings), waits there, then either fades
    /// where it lies or, given a Destination, slides into a player's bar shrinking
    /// and fading. The owning effect tears it down on skip.
    /// </summary>
    public sealed class BillFan : MonoBehaviour
    {
        public const float ArriveSeconds = 0.25f;
        public const float FadeSeconds = 0.5f;
        public const float DepartSeconds = 0.5f;
        /// <summary>Fade-in finishes this far into the arrival, so the bills read before they land.</summary>
        private const float FadeInFraction = 0.6f;

        public Vector2 From;
        public Vector2 To;
        public float StartScale = 0.3f;
        /// <summary>Earnings arrive full-size and opaque from off-screen; purchases grow out of the bar.</summary>
        public bool GrowAndFadeIn = true;
        /// <summary>Seconds to sit after arriving before leaving by itself; negative = wait for BeginFade.</summary>
        public float AutoFadeAfterSeconds = -1f;
        /// <summary>Where to slide to when leaving; null = fade in place.</summary>
        public Vector2? Destination;
        public float EndScale = 0.4f;
        /// <summary>Fires once when the pile reaches its Destination.</summary>
        public System.Action OnArrive;

        private RectTransform _rect;
        private CanvasGroup _group;
        private float _elapsed;
        private float _fadeStart = -1f;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.alpha = 0f;
        }

        public void BeginFade()
        {
            if (_fadeStart < 0f)
            {
                _fadeStart = _elapsed;
            }
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_fadeStart >= 0f)
            {
                Leave();
                return;
            }
            if (AutoFadeAfterSeconds >= 0f && _elapsed >= ArriveSeconds + AutoFadeAfterSeconds)
            {
                BeginFade();
                return;
            }
            float t = Mathf.Clamp01(_elapsed / ArriveSeconds);
            float eased = t * t * (3f - 2f * t);
            _rect.anchoredPosition = Vector2.Lerp(From, To, eased);
            if (GrowAndFadeIn)
            {
                transform.localScale = Vector3.one * Mathf.Lerp(StartScale, 1f, eased);
                _group.alpha = Mathf.Clamp01(t / FadeInFraction);
            }
            else
            {
                _group.alpha = 1f;
            }
        }

        private void Leave()
        {
            if (Destination == null)
            {
                float f = Mathf.Clamp01((_elapsed - _fadeStart) / FadeSeconds);
                _group.alpha = 1f - f;
                if (f >= 1f)
                {
                    Destroy(gameObject);
                }
                return;
            }
            float d = Mathf.Clamp01((_elapsed - _fadeStart) / DepartSeconds);
            float eased = d * d * (3f - 2f * d);
            _rect.anchoredPosition = Vector2.Lerp(To, Destination.Value, eased);
            transform.localScale = Vector3.one * Mathf.Lerp(1f, EndScale, eased);
            _group.alpha = d < 0.5f ? 1f : 1f - (d - 0.5f) * 2f;
            if (d >= 1f)
            {
                OnArrive?.Invoke();
                Destroy(gameObject);
            }
        }
    }
}
