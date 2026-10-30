using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// A reaction card slapped onto a staged play: flies in from the responder's bar,
    /// oversized and shrinking to rest as it lands, then sits where it fell until told
    /// to fade. The pile it joins is torn down by its owner.
    /// </summary>
    public sealed class SlapMotion : MonoBehaviour
    {
        public const float SlapSeconds = 0.3f;
        public const float FadeSeconds = 0.4f;
        private const float LiftScale = 1.5f;

        public Vector2 From;
        public Vector2 To;

        private RectTransform _rect;
        private CanvasGroup _group;
        private float _elapsed;
        private float _fadeStart = -1f;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
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
                float f = Mathf.Clamp01((_elapsed - _fadeStart) / FadeSeconds);
                _group.alpha = 1f - f;
                if (f >= 1f)
                {
                    Destroy(gameObject);
                }
                return;
            }
            float t = Mathf.Clamp01(_elapsed / SlapSeconds);
            float eased = t * t; // accelerating in: the hand comes DOWN, it doesn't float
            _rect.anchoredPosition = Vector2.Lerp(From, To, eased);
            transform.localScale = Vector3.one * Mathf.Lerp(LiftScale, 1f, eased);
        }
    }
}
