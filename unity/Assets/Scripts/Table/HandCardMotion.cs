using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Eases a hand card toward its target height — the rise-on-hover, sink-on-exit
    /// motion. Exponential smoothing: fast start, soft landing.
    /// </summary>
    public sealed class HandCardMotion : MonoBehaviour
    {
        public float TargetY;
        /// <summary>Set while a neighbour is being dragged past; null leaves X alone.</summary>
        public float? TargetX;

        private RectTransform _rect;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        private void Update()
        {
            var position = _rect.anchoredPosition;
            float ease = 1f - Mathf.Exp(-14f * Time.deltaTime);
            position.y = Mathf.Lerp(position.y, TargetY, ease);
            if (TargetX is float targetX)
            {
                position.x = Mathf.Lerp(position.x, targetX, ease);
            }
            _rect.anchoredPosition = position;
        }
    }
}
