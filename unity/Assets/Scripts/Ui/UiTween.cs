using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Anchored-position tween for UI elements outside layout groups; self-disables when done.</summary>
    public sealed class UiTween : MonoBehaviour
    {
        private Vector2 _start;
        private Vector2 _target;
        private float _elapsed;
        private float _duration;
        private RectTransform _rect;

        public static void SlideTo(RectTransform rectTransform, Vector2 targetAnchored, float duration = 0.15f)
        {
            var tween = rectTransform.gameObject.GetComponent<UiTween>();
            if (tween == null)
            {
                tween = rectTransform.gameObject.AddComponent<UiTween>();
            }
            tween._rect = rectTransform;
            tween._start = rectTransform.anchoredPosition;
            tween._target = targetAnchored;
            tween._duration = Mathf.Max(0.01f, duration);
            tween._elapsed = 0f;
            tween.enabled = true;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float easedProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_elapsed / _duration));
            _rect.anchoredPosition = Vector2.Lerp(_start, _target, easedProgress);
            if (_elapsed >= _duration)
            {
                _rect.anchoredPosition = _target;
                enabled = false;
            }
        }
    }
}
