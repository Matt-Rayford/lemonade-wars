using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// A hand of bills slapped onto the table: slides in to its spot, waits, then either
    /// fades where it lies or, given a Destination, slides into a player's bar shrinking
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
            float arrival = Mathf.Clamp01(_elapsed / ArriveSeconds);
            float eased = arrival * arrival * (3f - 2f * arrival);
            _rect.anchoredPosition = Vector2.Lerp(From, To, eased);
            if (GrowAndFadeIn)
            {
                transform.localScale = Vector3.one * Mathf.Lerp(StartScale, 1f, eased);
                _group.alpha = Mathf.Clamp01(arrival / FadeInFraction);
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
                float fade = Mathf.Clamp01((_elapsed - _fadeStart) / FadeSeconds);
                _group.alpha = 1f - fade;
                if (fade >= 1f)
                {
                    Destroy(gameObject);
                }
                return;
            }
            float departure = Mathf.Clamp01((_elapsed - _fadeStart) / DepartSeconds);
            float eased = departure * departure * (3f - 2f * departure);
            _rect.anchoredPosition = Vector2.Lerp(To, Destination.Value, eased);
            transform.localScale = Vector3.one * Mathf.Lerp(1f, EndScale, eased);
            _group.alpha = departure < 0.5f ? 1f : 1f - (departure - 0.5f) * 2f;
            if (departure >= 1f)
            {
                OnArrive?.Invoke();
                Destroy(gameObject);
            }
        }
    }
}
