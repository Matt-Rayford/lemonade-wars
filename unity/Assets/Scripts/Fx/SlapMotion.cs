using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// A reaction card slapped onto a staged play: flies in oversized, shrinks to rest as
    /// it lands, then sits where it fell until told to fade.
    /// </summary>
    public sealed class SlapMotion : MonoBehaviour
    {
        public const float SlapSeconds = 0.3f;
        public const float FadeSeconds = 0.4f;
        private const float LiftScale = 1.5f;

        public Vector2 From;
        public Vector2 To;

        /// <summary>Once down, the card is the pile's to place (hover spreads it); the slap no longer moves it.</summary>
        public bool Landed { get; private set; }

        private RectTransform _rect;
        private CanvasGroup _group;
        private float _elapsed;
        private float _fadeStart = -1f;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _group = GetComponent<CanvasGroup>();
            if (_group == null)
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }
            _group.blocksRaycasts = true; // the card is readable on hover like the one under it
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
                float fade = Mathf.Clamp01((_elapsed - _fadeStart) / FadeSeconds);
                _group.alpha = 1f - fade;
                if (fade >= 1f)
                {
                    Destroy(gameObject);
                }
                return;
            }
            if (Landed)
            {
                return;
            }
            float progress = Mathf.Clamp01(_elapsed / SlapSeconds);
            float eased = progress * progress; // accelerating in: the hand comes DOWN, it doesn't float
            _rect.anchoredPosition = Vector2.Lerp(From, To, eased);
            transform.localScale = Vector3.one * Mathf.Lerp(LiftScale, 1f, eased);
            Landed = progress >= 1f;
        }
    }
}
