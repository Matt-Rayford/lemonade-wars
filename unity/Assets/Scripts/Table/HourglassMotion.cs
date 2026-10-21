using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The waiting hourglass on a rival's badge: swells from 50% to 65% of the badge
    /// and back, then spins a full turn (360° in-plane) in half a second, forever. Phase is
    /// taken from wall-clock time, not component age — player bars rebuild on every
    /// revision, and restarting the loop each time would stutter it.
    /// </summary>
    public sealed class HourglassMotion : MonoBehaviour
    {
        private const float PulseSeconds = 0.6f;
        private const float FlipSeconds = 0.5f;
        /// <summary>Peak scale relative to rest: rest is 50% of the badge, peak 65%.</summary>
        private const float PulsePeak = 1.3f;

        private RectTransform _rect;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        private void Update()
        {
            const float cycleSeconds = PulseSeconds + FlipSeconds;
            int turnsSoFar = Mathf.FloorToInt(Time.time / cycleSeconds);
            float phase = Time.time - turnsSoFar * cycleSeconds;

            float scale = 1f;
            float flipProgress = 0f;
            if (phase < PulseSeconds)
            {
                scale = 1f + (PulsePeak - 1f) * Mathf.Sin(Mathf.PI * phase / PulseSeconds);
            }
            else
            {
                flipProgress = Mathf.SmoothStep(0f, 1f, (phase - PulseSeconds) / FlipSeconds);
            }
            _rect.localScale = Vector3.one * scale;
            _rect.localRotation = Quaternion.Euler(0f, 0f, -360f * flipProgress);
        }
    }
}
