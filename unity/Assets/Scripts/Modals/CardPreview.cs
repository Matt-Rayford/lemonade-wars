using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Hover-to-enlarge card preview. Follows the mouse (offset to the side, clamped to
    /// the screen) and lives on its own top-sorted canvas so it renders above everything —
    /// table, modals, drag ghosts. Never intercepts input.
    /// </summary>
    public sealed class CardPreview
    {
        private const float Width = 340f;
        private const float Height = 476f;

        private readonly RectTransform _root;
        private readonly RawImage _image;
        private readonly PreviewFollower _follower;
        private readonly PreviewDriver _driver;

        public CardPreview(RectTransform canvasRoot)
        {
            // Own canvas, sorted far above the game canvas; no raycaster: clicks pass through.
            var canvasGo = new GameObject("PreviewCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // The delay/modifier gate lives on the always-active canvas object.
            _driver = canvasGo.AddComponent<PreviewDriver>();
            _driver.ShowReady = () => Show(_driver.PendingTexture);
            _driver.HideRequested = () => _root.gameObject.SetActive(false);

            var rootGo = new GameObject("Preview", typeof(RectTransform), typeof(PreviewFollower));
            rootGo.transform.SetParent(canvasGo.transform, false);
            _root = (RectTransform)rootGo.transform;
            _root.sizeDelta = new Vector2(Width, Height);
            _follower = rootGo.GetComponent<PreviewFollower>();
            _follower.CanvasRect = (RectTransform)canvasGo.transform;

            // Shader-rounded corners, same treatment as table cards.
            var go = new GameObject("PreviewImage", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(_root, false);
            UiKit.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);
            _image = go.GetComponent<RawImage>();
            _image.raycastTarget = false;
            _image.material = UiKit.RoundedImageMaterial(Width, Height);
            _root.gameObject.SetActive(false);
        }

        public void Show(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }
            _image.texture = texture;
            _root.gameObject.SetActive(true);
            _follower.Reposition(); // snap to the cursor immediately, no one-frame lag
        }

        public void Hide()
        {
            _driver.EndHover();
            _root.gameObject.SetActive(false);
        }

        /// <summary>No previews while a drag is in flight (and hide any current one).</summary>
        public void SetDragging(bool dragging)
        {
            _driver.Suppressed = dragging;
            if (dragging)
            {
                Hide();
            }
        }

        /// <summary>
        /// Wire a card image to preview on hover: dwell to show, Alt/Cmd for instant.
        /// </summary>
        public void Attach(GameObject cardGo, Texture2D texture)
        {
            UiKit.AddHover(cardGo, () => _driver.BeginHover(cardGo, texture), Hide);
        }
    }
}
