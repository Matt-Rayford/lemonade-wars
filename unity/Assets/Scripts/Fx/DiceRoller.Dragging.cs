using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // A die that has come to rest can be picked up and set down elsewhere, off whatever it
    // landed on. The die itself is a picture from its own camera, so an invisible grip in the
    // canvas rides over it to take the pointer.
    public sealed partial class DiceRoller
    {
        /// <summary>The grip's side, in die widths: the resting die with its tilt toward the camera.</summary>
        private const float GripSizeInDice = 1.7f;
        /// <summary>The die's centre stays this far inside the screen, as a share of its width and height.</summary>
        private const float ScreenEdgeMarginViewport = 0.04f;

        private RectTransform _grip;
        /// <summary>How far the player has moved the die from where the throw left it, across the floor.
        /// Only the picture shifts: a hop is still simulated from the recorded resting spot, inside the walls.</summary>
        private Vector3 _movedBy;
        private Vector3 _grabOffset;
        private bool _isHeldByPlayer;

        private bool IsAtRest => _phase == Phase.Hold || _phase == Phase.Parked;

        private void BuildGrip(RectTransform canvasRoot)
        {
            var gripGo = new GameObject("DieGrip", typeof(RectTransform), typeof(Image));
            gripGo.transform.SetParent(canvasRoot, false);
            _grip = (RectTransform)gripGo.transform;
            gripGo.GetComponent<Image>().color = new Color(0, 0, 0, 0); // unseen, but it takes the pointer

            var drag = gripGo.AddComponent<DragRelay>();
            drag.CanStartDrag = () => IsAtRest;
            drag.Began = TakeHoldOfDie;
            drag.Moved = MoveHeldDie;
            drag.Ended = _ => _isHeldByPlayer = false;
            // A plain click on the die still does what a click anywhere on the roll does.
            UiKit.AddClick(gripGo, Skip);
            gripGo.SetActive(false);
        }

        /// <summary>Every tick: the grip exists only over a resting die, wherever that die now is.</summary>
        private void PlaceGrip()
        {
            bool isGrippable = IsAtRest && _recording != null;
            _grip.gameObject.SetActive(isGrippable);
            if (!isGrippable)
            {
                _isHeldByPlayer = false;
                return;
            }
            Vector2 dieOnScreen = _camera.WorldToViewportPoint(_die.position);
            _grip.anchorMin = _grip.anchorMax = dieOnScreen;
            _grip.anchoredPosition = Vector2.zero;
            float dieWidthPx = _view.rect.height / (2f * CameraOrthographicHalfHeight) * DieSizeUnits;
            _grip.sizeDelta = Vector2.one * (dieWidthPx * GripSizeInDice);
        }

        private void TakeHoldOfDie(Vector2 screenPosition)
        {
            if (!IsAtRest || _recording == null)
            {
                return;
            }
            _isHeldByPlayer = true;
            // Wherever on the die it was grabbed, that point stays under the cursor.
            _grabOffset = _die.localPosition - FloorPointUnder(ViewportOf(screenPosition));
        }

        private void MoveHeldDie(Vector2 screenPosition)
        {
            if (!_isHeldByPlayer || !IsAtRest || _recording == null)
            {
                return;
            }
            var wanted = FloorPointUnder(ViewportOf(screenPosition)) + _grabOffset;
            Vector2 centreOnScreen = _camera.WorldToViewportPoint(_rig.TransformPoint(wanted));
            var keptOnScreen = new Vector2(
                Mathf.Clamp(centreOnScreen.x, ScreenEdgeMarginViewport, 1f - ScreenEdgeMarginViewport),
                Mathf.Clamp(centreOnScreen.y, ScreenEdgeMarginViewport, 1f - ScreenEdgeMarginViewport));
            if (keptOnScreen != centreOnScreen)
            {
                wanted = FloorPointUnder(keptOnScreen);
            }
            var restingPosition = _die.localPosition - _movedBy;
            _movedBy = new Vector3(wanted.x - restingPosition.x, 0f, wanted.z - restingPosition.z);
            PlaceDie(_recording.DurationSeconds);
        }

        private static Vector2 ViewportOf(Vector2 screenPosition) =>
            new Vector2(screenPosition.x / Screen.width, screenPosition.y / Screen.height);

        /// <summary>The rig-local point, at the resting die's own height, that a spot on the screen looks at.</summary>
        private Vector3 FloorPointUnder(Vector2 viewportPoint)
        {
            var ray = _camera.ViewportPointToRay(viewportPoint);
            float distance = (_die.position.y - ray.origin.y) / ray.direction.y;
            return _rig.InverseTransformPoint(ray.GetPoint(distance));
        }
    }
}
