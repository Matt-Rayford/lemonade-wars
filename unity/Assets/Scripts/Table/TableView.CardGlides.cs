using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Card glides: ghost cards that cross the table on their own, for a shelf purchase
    // with nothing to decide and for draws leaving the lemon pile. Ghosts sit on the
    // canvas root, not in a table zone, so they survive re-renders.
    public sealed partial class TableView
    {
        private const float GhostWidthPx = 150f;
        private const float GhostHeightPx = 210f;
        private const float GhostAlpha = 0.92f;
        private const float ShelfGlideSeconds = 0.5f;
        /// <summary>Draws in one burst (draw 2) leave the pile one after another, not as one card.</summary>
        private const float DrawStaggerSeconds = 0.12f;
        private const float DrawToChipFadeFrom = 0.6f;
        public const float TimeoutFlightSeconds = 0.45f;

        private int _shelfGlidesInFlight;
        private int _drawFlightFrame = -1;
        private int _drawFlightsThisFrame;

        /// <summary>
        /// A no-decision purchase: the card slides from its shelf slot to where it will
        /// land, and the buy fires on arrival (at once when either end is unknown).
        /// </summary>
        public void GlideShelfCard(Texture2D art, Vector3? fromWorld, Vector3? toWorld,
            System.Action onArrive)
        {
            if (fromWorld == null || toWorld == null)
            {
                onArrive?.Invoke();
                return;
            }
            _shelfGlidesInFlight++;
            var motion = GlideGhost(BuildShelfGhost(art),
                _canvasRoot.InverseTransformPoint(fromWorld.Value),
                _canvasRoot.InverseTransformPoint(toWorld.Value), ShelfGlideSeconds);
            motion.OnArrive = () =>
            {
                _shelfGlidesInFlight = Mathf.Max(0, _shelfGlidesInFlight - 1);
                onArrive?.Invoke();
            };
        }

        /// <summary>Another player's draw: a face-down card shrinks into their avatar chip, fading as it lands.</summary>
        public void GlideCardToPlayer(Texture2D backArt, Vector3? fromWorld, int playerId)
        {
            var avatar = PlayerAvatarWorld(playerId);
            if (fromWorld == null || backArt == null || avatar == null)
            {
                return;
            }
            Vector2 chipCentre = _canvasRoot.InverseTransformPoint(avatar.Value.Center);
            var motion = GlideGhost(BuildShelfGhost(backArt),
                _canvasRoot.InverseTransformPoint(fromWorld.Value), chipCentre, ShelfGlideSeconds);
            float chipDiameter = 2f * (chipCentre - (Vector2)_canvasRoot.InverseTransformPoint(avatar.Value.Rim)).magnitude;
            motion.EndScale = chipDiameter / GhostWidthPx;
            motion.FadeOutFrom = DrawToChipFadeFrom;
            motion.Delay = NextDrawDelay();
        }

        private float NextDrawDelay()
        {
            if (_drawFlightFrame != Time.frameCount)
            {
                _drawFlightFrame = Time.frameCount;
                _drawFlightsThisFrame = 0;
            }
            return DrawStaggerSeconds * _drawFlightsThisFrame++;
        }

        /// <summary>
        /// A drawn Timeout flies face-up to the middle of the table, growing to the size it
        /// will lie at; the caller shows the real card on arrival.
        /// </summary>
        public void GlideTimeoutToCenter(Texture2D art, Vector3? fromWorld, System.Action onArrive)
        {
            if (fromWorld == null || art == null)
            {
                onArrive?.Invoke();
                return;
            }
            var motion = GlideGhost(BuildShelfGhost(art),
                _canvasRoot.InverseTransformPoint(fromWorld.Value), Vector2.zero, TimeoutFlightSeconds);
            motion.EndScale = TimeoutCardWidthPx / GhostWidthPx;
            motion.OnArrive = () => onArrive?.Invoke();
        }

        /// <summary>
        /// A drawn Lemon card flies face-up into its place in the fan. The hand renders it
        /// invisible in its slot first, so the destination is only read when the flight
        /// starts, once the fan has been laid out.
        /// </summary>
        public void GlideCardToHand(Texture2D art, Vector3? fromWorld, int instanceId, System.Action onArrive)
        {
            if (fromWorld == null || art == null)
            {
                onArrive?.Invoke();
                return;
            }
            Vector2 pile = _canvasRoot.InverseTransformPoint(fromWorld.Value);
            var motion = GlideGhost(BuildShelfGhost(art), pile, pile, ShelfGlideSeconds);
            motion.Delay = NextDrawDelay();
            motion.OnStart = () =>
            {
                var placement = HandCardPlacement(instanceId);
                if (placement == null)
                {
                    return; // nowhere to land: the flight ends where it began and the hold lifts
                }
                motion.To = _canvasRoot.InverseTransformPoint(placement.Value.Center);
                motion.EndScale = placement.Value.Width / GhostWidthPx;
            };
            motion.OnArrive = () => onArrive?.Invoke();
        }

        /// <summary>A straight glide that shows the ghost from its first frame.</summary>
        private static FlightMotion GlideGhost(RectTransform ghost, Vector2 from, Vector2 to, float seconds)
        {
            var motion = ghost.gameObject.AddComponent<FlightMotion>();
            motion.From = from;
            motion.To = to;
            motion.Duration = seconds;
            motion.ArcHeight = 0f;
            motion.VisibleWhileWaiting = true;
            return motion;
        }

        private RectTransform BuildShelfGhost(Texture2D art)
        {
            var image = UiKit.CreateCardImage(_canvasRoot, art, GhostWidthPx, GhostHeightPx);
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, GhostAlpha);
            var ghost = (RectTransform)image.transform.parent;
            ghost.name = "ShelfGhost";
            ghost.gameObject.AddComponent<ShelfGhostTag>();
            ghost.GetComponent<Image>().raycastTarget = false;
            ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
            ghost.pivot = new Vector2(0.5f, 0.5f);
            ghost.sizeDelta = new Vector2(GhostWidthPx, GhostHeightPx);
            ghost.SetAsLastSibling();
            return ghost;
        }

        /// <summary>Leaving the table: no ghost may outlive it, held or mid-glide.</summary>
        public void DropShelfGhosts()
        {
            if (_stickyGhost != null)
            {
                Object.Destroy(EndStickyDrag().gameObject);
            }
            foreach (var ghost in _canvasRoot.GetComponentsInChildren<ShelfGhostTag>())
            {
                Object.Destroy(ghost.gameObject);
            }
            _shelfGlidesInFlight = 0;
        }
    }
}
