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
        /// <summary>Scale on arrival (1 = unchanged) — claim flights land smaller.</summary>
        public float EndScale = 1f;
        /// <summary>Fires once when the delay elapses and the flight actually begins —
        /// late-bind To here if the layout may have moved during the delay.</summary>
        public System.Action OnStart;
        public System.Action OnArrive;

        private RectTransform _rect;
        private CanvasGroup _group;
        private float _elapsed;
        private bool _started;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.alpha = 0f;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = (_elapsed - Delay) / Duration;
            if (t < 0f)
            {
                return;
            }
            if (!_started)
            {
                _started = true;
                OnStart?.Invoke();
            }
            if (t >= 1f)
            {
                OnArrive?.Invoke();
                Destroy(gameObject);
                return;
            }
            _group.alpha = Mathf.Clamp01(t * 6f);
            float eased = t * t * (3f - 2f * t);
            var position = Vector2.Lerp(From, To, eased);
            position.y += ArcHeight * 4f * t * (1f - t);
            _rect.anchoredPosition = position;
            transform.localScale = Vector3.one * Mathf.Lerp(1f, EndScale, eased);
        }
    }
}
