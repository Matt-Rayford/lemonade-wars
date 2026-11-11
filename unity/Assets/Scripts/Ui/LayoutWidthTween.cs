using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Eases a LayoutElement's preferredWidth (SmoothStep in-out): the insertion gap
    /// that slides neighboring cards aside inside a layout group.</summary>
    public sealed class LayoutWidthTween : MonoBehaviour
    {
        private UnityEngine.UI.LayoutElement _element;
        private float _from;
        private float _target;
        private float _elapsed;
        private float _duration = 0.18f;
        private bool _destroyAtZero;

        /// <param name="destroyAtZero">Destroy the GameObject once it has closed to width 0.</param>
        public void SetTarget(float target, float duration = 0.18f, bool destroyAtZero = false)
        {
            if (_element == null)
            {
                _element = GetComponent<UnityEngine.UI.LayoutElement>();
            }
            _from = Mathf.Max(0f, _element.preferredWidth);
            _target = target;
            _duration = Mathf.Max(0.01f, duration);
            _destroyAtZero = destroyAtZero;
            _elapsed = 0f;
            enabled = true;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float easedProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_elapsed / _duration));
            _element.preferredWidth = Mathf.Lerp(_from, _target, easedProgress);
            if (_elapsed >= _duration)
            {
                _element.preferredWidth = _target;
                enabled = false;
                if (_destroyAtZero && _target <= 0.01f)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
