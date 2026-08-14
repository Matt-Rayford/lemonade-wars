using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Small scale punch on spawn (reveal cards, toasts).</summary>
    public sealed class PopIn : MonoBehaviour
    {
        private float _t;

        private void Update()
        {
            _t += Time.deltaTime;
            float progress = Mathf.Clamp01(_t / 0.22f);
            float overshoot = 1f + 0.14f * Mathf.Sin(progress * Mathf.PI);
            transform.localScale = Vector3.one * (0.8f + 0.2f * progress) * overshoot;
            if (progress >= 1f)
            {
                transform.localScale = Vector3.one;
                Destroy(this);
            }
        }
    }
}
