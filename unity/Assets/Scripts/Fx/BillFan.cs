using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// A hand of bills slapped onto the table: slides out of the buyer's bar to the
    /// showcase spot while growing and fading in (the purchased card's departure,
    /// reversed), waits there under the card, then fades out when told to. OnDone
    /// is not needed — the owning effect tears it down on skip.
    /// </summary>
    public sealed class BillFan : MonoBehaviour
    {
        public const float ArriveSeconds = 0.25f;
        public const float FadeSeconds = 0.5f;
        /// <summary>Fade-in finishes this far into the arrival, so the bills read before they land.</summary>
        private const float FadeInFraction = 0.6f;

        public Vector2 From;
        public Vector2 To;
        public float StartScale = 0.3f;
        /// <summary>Seconds to sit after arriving before fading by itself; negative = wait for BeginFade.</summary>
        public float AutoFadeAfterSeconds = -1f;

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
                float f = Mathf.Clamp01((_elapsed - _fadeStart) / FadeSeconds);
                _group.alpha = 1f - f;
                if (f >= 1f)
                {
                    Destroy(gameObject);
                }
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
            transform.localScale = Vector3.one * Mathf.Lerp(StartScale, 1f, eased);
            _group.alpha = Mathf.Clamp01(t / FadeInFraction);
        }
    }
}
