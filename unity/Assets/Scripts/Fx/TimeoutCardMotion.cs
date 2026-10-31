using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// A card slapped onto the table askew: lands with a scale-and-twist settle at a
    /// random tilt, then keeps a faint sway so it reads as lying there, not pinned.
    /// </summary>
    public sealed class TimeoutCardMotion : MonoBehaviour
    {
        public const float MaxTiltDegrees = 20f;
        private const float SettleSeconds = 0.35f;
        private const float LandingScale = 1.25f;
        private const float LandingExtraTwistDegrees = 12f;
        private const float SwayDegrees = 1.5f;
        private const float SwayHz = 0.4f;

        private float _tiltDegrees;
        private float _elapsed;

        private void Awake()
        {
            _tiltDegrees = Random.Range(-MaxTiltDegrees, MaxTiltDegrees);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float settle = Mathf.Clamp01(_elapsed / SettleSeconds);
            float eased = 1f - (1f - settle) * (1f - settle);
            float twist = Mathf.Sign(_tiltDegrees) * LandingExtraTwistDegrees * (1f - eased);
            float sway = SwayDegrees * Mathf.Sin(_elapsed * SwayHz * 2f * Mathf.PI);
            transform.localRotation = Quaternion.Euler(0f, 0f, _tiltDegrees + twist + sway);
            transform.localScale = Vector3.one * Mathf.Lerp(LandingScale, 1f, eased);
        }
    }
}
