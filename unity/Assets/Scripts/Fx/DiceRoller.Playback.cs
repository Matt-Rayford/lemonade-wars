using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class DiceRoller
    {
        private const float HoldSeconds = 0.9f;
        private const float FadeSeconds = 0.3f;

        private bool ShouldStay => Linger || KeepForEarnings;

        /// <summary>The result stays in words while the payout plays; a response window needs the strip for itself.</summary>
        private string ParkedHeadline => KeepForEarnings && !Linger ? _landedHeadline : null;

        public void Tick()
        {
            if (_phase == Phase.Idle)
            {
                return;
            }
            _phaseElapsed += Time.deltaTime * _playbackSpeed;

            switch (_phase)
            {
                case Phase.Rolling:
                    TickRolling();
                    break;
                case Phase.Hold:
                    TickHold();
                    break;
                case Phase.Parked:
                    TickParked();
                    break;
                case Phase.FadeOut:
                    TickFadeOut();
                    break;
            }
        }

        private void TickRolling()
        {
            PlaceDie(_phaseElapsed);
            if (_phaseElapsed >= _recording.DurationSeconds)
            {
                Land();
            }
        }

        private void TickHold()
        {
            if (_phaseElapsed >= HoldSeconds)
            {
                if (ShouldStay && _queue.Count == 0)
                {
                    Park();
                }
                else
                {
                    BeginFadeOut();
                }
            }
        }

        private void Park()
        {
            SetPhase(Phase.Parked);
            // The full-screen view would swallow the Pass button of the response strip above it.
            _viewImage.raycastTarget = false;
            SetHeadline(ParkedHeadline);
            // Parking ends the BUSY period: renders are revision-gated, so it must wake
            // the renderer exactly like a fade-out does.
            OnFinished?.Invoke();
        }

        private void TickParked()
        {
            SetHeadline(ParkedHeadline);
            if (!ShouldStay || _queue.Count > 0)
            {
                BeginFadeOut();
            }
        }

        private void TickFadeOut()
        {
            _group.alpha = 1f - Mathf.Clamp01(_phaseElapsed / FadeSeconds);
            if (_phaseElapsed >= FadeSeconds)
            {
                if (_queue.Count > 0)
                {
                    BeginNext();
                }
                else
                {
                    Clear();
                    OnFinished?.Invoke();
                }
            }
        }

        private void BeginNext()
        {
            _current = _queue.Dequeue();
            // Rolls stacking up (a bot round in full swing) play faster, to keep pace.
            _playbackSpeed = _queue.Count >= 3 ? 2.4f : _queue.Count >= 1 ? 1.6f : 1f;
            EnsureRenderTarget();
            SetHeadline(_current.Title);
            _group.alpha = 1f;
            _view.gameObject.SetActive(true);
            _viewImage.raycastTarget = true;
            _camera.enabled = true;

            if (_current.IsHop && _recording != null)
            {
                RecordHop();
            }
            else
            {
                _recording = _simulator.RecordThrow();
                _labelRotation = LabelShowing(_current.Value, _recording.FinalRotation);
            }
            PlaceDie(0f);
            SetPhase(Phase.Rolling);
        }

        /// <summary>
        /// A hop keeps the labels the die already wears, so the simulator retries until the
        /// wanted face lands up. If it gives up, relabel: a one-frame pip jump beats a wrong number.
        /// </summary>
        private void RecordHop()
        {
            int wanted = _current.Value;
            var label = _labelRotation;
            _recording = _simulator.RecordHop(_recording.FinalPosition, _recording.FinalRotation,
                rotation => DieMesh.ValueUp(rotation * label) == wanted);
            if (DieMesh.ValueUp(_recording.FinalRotation * _labelRotation) != wanted)
            {
                _labelRotation = LabelShowing(wanted, _recording.FinalRotation);
            }
        }

        /// <summary>
        /// The local rotation that turns the face physics left on top into the face for
        /// <paramref name="value"/>; a random quarter-turn about it varies the pip spin.
        /// </summary>
        private static Quaternion LabelShowing(int value, Quaternion restingRotation)
        {
            var upFace = DieMesh.FaceNormals[DieMesh.UpFaceIndex(restingRotation)];
            var wantedFace = DieMesh.NormalOf(value);
            return Quaternion.FromToRotation(wantedFace, upFace) *
                   Quaternion.AngleAxis(90f * Random.Range(0, 4), wantedFace);
        }

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            _phaseElapsed = 0f;
        }

        private void SetHeadline(string headline)
        {
            if (Headline == headline)
            {
                return;
            }
            Headline = headline;
            HeadlineRevision++;
        }

        private void BeginFadeOut()
        {
            _viewImage.raycastTarget = false;
            SetHeadline(null);
            SetPhase(Phase.FadeOut);
        }

        private void PlaceDie(float seconds)
        {
            _recording.Sample(seconds, out var position, out var rotation);
            _die.localPosition = position;
            _die.localRotation = rotation * _labelRotation;
            PlaceContactShadow(position);
        }

        /// <summary>The shadow spreads and thins as the die lifts off the floor.</summary>
        private void PlaceContactShadow(Vector3 diePosition)
        {
            float height = Mathf.Max(0f, diePosition.y - 0.5f);
            _shadow.localPosition = new Vector3(diePosition.x, 0.01f, diePosition.z);
            _shadow.localScale = Vector3.one * (1f + 0.15f * height);
            byte alpha = (byte)(255f * 0.6f / (1f + 0.5f * height));
            for (int i = 0; i < 4; i++)
            {
                _shadowColors[i] = new Color32(255, 255, 255, alpha);
            }
            _shadowMesh.colors32 = _shadowColors;
        }

        private void Land()
        {
            PlaceDie(_recording.DurationSeconds);
            _landedHeadline = $"{_current.Title.TrimEnd('.')}: {_current.Value}!";
            SetHeadline(_landedHeadline);
            _current.OnLanded?.Invoke();
            SetPhase(Phase.Hold);
        }

        private void Skip()
        {
            switch (_phase)
            {
                case Phase.Rolling:
                    Land();
                    break;
                case Phase.Hold:
                    BeginFadeOut();
                    break;
            }
        }
    }
}
