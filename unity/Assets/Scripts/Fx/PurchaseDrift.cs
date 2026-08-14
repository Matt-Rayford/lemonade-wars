using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// A purchased card leaving its shelf slot: eased drift diagonally down-left,
    /// slight swell, fade out through the back half, then self-destruct. OnDone fires
    /// only on the natural end — early teardown is the owner's job (EndActive).
    /// </summary>
    public sealed class PurchaseDrift : MonoBehaviour
    {
        public Vector2 Drift = new Vector2(-200f, -150f);
        public float Duration = 0.7f;
        public System.Action OnDone;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Vector2 _from;
        private bool _started;
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
            float t = Mathf.Clamp01(_elapsed / Duration);
            float eased = 1f - (1f - t) * (1f - t); // fast lift, gentle settle
            _rect.anchoredPosition = _from + Drift * eased;
            transform.localScale = Vector3.one * (1f + 0.12f * eased);
            _group.alpha = t < 0.45f ? 1f : 1f - (t - 0.45f) / 0.55f;
            if (t >= 1f)
            {
                OnDone?.Invoke();
                Destroy(gameObject);
            }
        }
    }
}
