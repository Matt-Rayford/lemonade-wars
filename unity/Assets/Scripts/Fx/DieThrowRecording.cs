using System.Collections.Generic;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// A die's flight, captured one physics step at a time so it can be replayed at any
    /// speed, skipped to its end, or inspected before anyone sees it.
    /// </summary>
    public sealed class DieThrowRecording
    {
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Quaternion> _rotations = new List<Quaternion>();
        private readonly float _stepSeconds;

        public DieThrowRecording(float stepSeconds)
        {
            _stepSeconds = stepSeconds;
        }

        public int FrameCount => _positions.Count;
        public float DurationSeconds => Mathf.Max(0, FrameCount - 1) * _stepSeconds;
        public Vector3 FinalPosition => _positions[FrameCount - 1];
        public Quaternion FinalRotation => _rotations[FrameCount - 1];

        public void Add(Vector3 position, Quaternion rotation)
        {
            _positions.Add(position);
            _rotations.Add(rotation);
        }

        public void Sample(float seconds, out Vector3 position, out Quaternion rotation)
        {
            float frame = Mathf.Clamp(seconds / _stepSeconds, 0f, FrameCount - 1);
            int index = Mathf.Min(Mathf.FloorToInt(frame), FrameCount - 2);
            if (index < 0)
            {
                position = _positions[0];
                rotation = _rotations[0];
                return;
            }
            float blend = frame - index;
            position = Vector3.Lerp(_positions[index], _positions[index + 1], blend);
            rotation = Quaternion.Slerp(_rotations[index], _rotations[index + 1], blend);
        }
    }
}
