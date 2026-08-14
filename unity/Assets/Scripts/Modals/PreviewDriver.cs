using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Gates the preview: hovering a card shows it after a short dwell (long enough
    /// that sweeping the cursor across the hand doesn't strobe), and Alt/Cmd skips
    /// the wait. Once up it stays up until the hover ends — releasing the key while
    /// still on the card shouldn't snatch it away.
    /// </summary>
    public sealed class PreviewDriver : MonoBehaviour
    {
        private const float DwellSeconds = 0.75f;

        public System.Action ShowReady;
        public System.Action HideRequested;
        public Texture2D PendingTexture { get; private set; }
        /// <summary>While true (drags), no previews start and none may show.</summary>
        public bool Suppressed;

        private GameObject _source;
        private bool _hovering;
        private bool _shown;
        private float _hoverTime;

        public void BeginHover(GameObject source, Texture2D texture)
        {
            if (Suppressed)
            {
                return;
            }
            _source = source;
            PendingTexture = texture;
            _hovering = true;
            _shown = false;
            _hoverTime = 0f;
        }

        public void EndHover()
        {
            _source = null;
            _hovering = false;
            _shown = false;
            PendingTexture = null;
        }

        private void Update()
        {
            if (!_hovering)
            {
                return;
            }
            // The hovered card can be DESTROYED by a re-render (drop, bot action);
            // destroyed objects never send pointer-exit, so self-dismiss instead of
            // leaving the preview stranded on screen.
            if (_source == null || Suppressed)
            {
                EndHover();
                HideRequested?.Invoke();
                return;
            }
            bool modifier = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) ||
                            Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

            _hoverTime += Time.unscaledDeltaTime;
            if (!_shown && (modifier || _hoverTime >= DwellSeconds))
            {
                _shown = true;
                ShowReady?.Invoke();
            }
        }
    }
}
