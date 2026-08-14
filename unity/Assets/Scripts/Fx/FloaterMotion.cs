using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Rise, linger, fade, self-destruct. Optional start delay for stacks.</summary>
    public sealed class FloaterMotion : MonoBehaviour
    {
        public float Delay;
        public float RiseSpeed = 55f;
        public float FadeStart = 0.75f;
        public float Lifetime = 1.25f;

        private RectTransform _rect;
        private CanvasGroup _group;
        private float _elapsed;

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
            float t = _elapsed - Delay;
            if (t < 0f)
            {
                return;
            }
            _group.alpha = t < 0.15f
                ? t / 0.15f
                : 1f - Mathf.Clamp01((t - FadeStart) / (Lifetime - FadeStart));
            _rect.anchoredPosition += new Vector2(0, RiseSpeed * Time.deltaTime);
            if (t >= Lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
