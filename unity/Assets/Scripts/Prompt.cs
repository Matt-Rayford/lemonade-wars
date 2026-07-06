using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Option prompt in the same style as the card picker: blurred darkened backdrop,
    /// floating white title, the card(s) in question centered, and a column of button
    /// options below that turn lemonade-yellow on hover. Used for contextual action menus
    /// (with Cancel) and blocking window/decision modals (options only). One at a time.
    /// </summary>
    public sealed class Prompt
    {
        public readonly struct Option
        {
            public readonly string Label;
            public readonly System.Action OnPick;

            public Option(string label, System.Action onPick)
            {
                Label = label;
                OnPick = onPick;
            }
        }

        private static readonly Color ButtonIdle = new Color(0.15f, 0.18f, 0.25f, 0.96f);
        private static readonly Color TextIdle = new Color(0.96f, 0.96f, 0.92f);

        private readonly ModalBackdrop _backdrop;
        private readonly TMP_Text _title;
        private readonly RectTransform _root;
        private readonly RectTransform _cardStrip;
        private readonly RectTransform _optionList;

        public bool IsOpen { get; private set; }

        public Prompt(RectTransform canvasRoot, MonoBehaviour host)
        {
            _root = UiKit.CreatePanel(canvasRoot, "Prompt", new Color(0, 0, 0, 0));
            UiKit.Anchor(_root, Vector2.zero, Vector2.one);
            _backdrop = new ModalBackdrop(_root, host);

            // Floating title with a drop shadow — no bar.
            _title = UiKit.CreateText(_root, "", 36, TextAnchor.MiddleCenter, Color.white);
            UiKit.Anchor((RectTransform)_title.transform,
                new Vector2(0.04f, 0.87f), new Vector2(0.96f, 0.98f));
            UiKit.AddTextShadow(_title);

            // The card(s) in question, centered on the blur.
            var stripHost = UiKit.CreatePanel(_root, "Cards", new Color(0, 0, 0, 0));
            stripHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(stripHost, new Vector2(0.10f, 0.44f), new Vector2(0.90f, 0.87f));
            var rowGo = new GameObject("CardRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(stripHost, false);
            UiKit.Anchor((RectTransform)rowGo.transform, Vector2.zero, Vector2.one);
            var rowLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 16;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            _cardStrip = (RectTransform)rowGo.transform;

            // Options flow in a centered column below the card.
            var listHost = UiKit.CreatePanel(_root, "Options", new Color(0, 0, 0, 0));
            UiKit.Anchor(listHost, new Vector2(0.27f, 0.03f), new Vector2(0.73f, 0.44f));
            _optionList = UiKit.CreateScrollList(listHost);
            _optionList.GetComponent<VerticalLayoutGroup>().spacing = 8;

            _root.gameObject.SetActive(false);
        }

        /// <summary>Show a prompt. Pass showCancel for optional (contextual) menus.</summary>
        public void Show(string title, IReadOnlyList<Texture2D> cards,
            IReadOnlyList<Option> options, bool showCancel)
        {
            IsOpen = true;
            _title.text = title;

            UiKit.Clear(_cardStrip);
            if (cards != null && cards.Count > 0)
            {
                // Size cards to fit the strip, generous when there are few.
                float width = Mathf.Min(240f, (1500f - (cards.Count - 1) * 16f) / cards.Count);
                float height = width / 0.714f;
                foreach (var texture in cards)
                {
                    if (texture != null)
                    {
                        UiKit.CreateCardImage(_cardStrip, texture, width, height);
                    }
                }
            }

            UiKit.Clear(_optionList);
            foreach (var option in options)
            {
                AddOptionButton(option.Label, option.OnPick, emphasized: true);
            }
            if (showCancel)
            {
                AddOptionButton("Cancel", null, emphasized: false);
            }

            // Appears next frame, once the backdrop blur has been captured.
            _backdrop.Reveal(_root.gameObject);
        }

        public void Hide()
        {
            IsOpen = false;
            _root.gameObject.SetActive(false);
            _backdrop.Hide();
        }

        /// <summary>Dark rounded button that turns lemonade-yellow on hover.</summary>
        private void AddOptionButton(string label, System.Action onPick, bool emphasized)
        {
            var go = new GameObject("Option", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(_optionList, false);
            var background = go.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            background.color = ButtonIdle;
            var layout = go.GetComponent<LayoutElement>();
            layout.minHeight = 46;
            layout.flexibleWidth = 1;

            var text = UiKit.CreateText(go.transform, label, 18, TextAnchor.MiddleCenter,
                emphasized ? TextIdle : new Color(0.75f, 0.75f, 0.75f));
            UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(12, 2), new Vector2(-12, -2));

            UiKit.AddHover(go,
                () =>
                {
                    background.color = UiKit.ButtonColor;
                    text.color = UiKit.ButtonTextColor;
                },
                () =>
                {
                    background.color = ButtonIdle;
                    text.color = emphasized ? TextIdle : new Color(0.75f, 0.75f, 0.75f);
                });
            UiKit.AddClick(go, () =>
            {
                Hide();
                onPick?.Invoke();
            });
        }
    }

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
        /// Wire a card image to preview on hover: shows after a 1s dwell, or instantly
        /// while Alt/Cmd is held.
        /// </summary>
        public void Attach(GameObject cardGo, Texture2D texture)
        {
            UiKit.AddHover(cardGo, () => _driver.BeginHover(cardGo, texture), Hide);
        }
    }

    /// <summary>
    /// Gates the preview: 1 second of continuous hover, or Alt/Cmd for instant show.
    /// A preview opened via the key closes the moment the key is released — the key
    /// takes precedence over the dwell timer — and stays closed until the next hover.
    /// </summary>
    public sealed class PreviewDriver : MonoBehaviour
    {
        private const float DwellSeconds = 1f;

        public System.Action ShowReady;
        public System.Action HideRequested;
        public Texture2D PendingTexture { get; private set; }
        /// <summary>While true (drags), no previews start and none may show.</summary>
        public bool Suppressed;

        private GameObject _source;
        private bool _hovering;
        private bool _shown;
        private bool _shownByKey;
        private bool _suppressed;
        private float _elapsed;

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
            _shownByKey = false;
            _suppressed = false;
            _elapsed = 0f;
        }

        public void EndHover()
        {
            _source = null;
            _hovering = false;
            _shown = false;
            _shownByKey = false;
            _suppressed = false;
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

            if (_shown)
            {
                if (_shownByKey && !modifier)
                {
                    // Key released: close and stay closed until the next hover.
                    _shown = false;
                    _suppressed = true;
                    HideRequested?.Invoke();
                }
                return;
            }
            // Re-pressing the key always reopens; suppression only blocks the dwell timer
            // (so a key-dismissed preview cannot sneak back via elapsed time).
            if (modifier)
            {
                _shown = true;
                _shownByKey = true;
                ShowReady?.Invoke();
                return;
            }
            if (_suppressed)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed >= DwellSeconds)
            {
                _shown = true;
                _shownByKey = false;
                ShowReady?.Invoke();
            }
        }
    }

    /// <summary>Keeps the preview beside the mouse, flipping sides near screen edges.</summary>
    public sealed class PreviewFollower : MonoBehaviour
    {
        private const float CursorGap = 30f;

        public RectTransform CanvasRect;

        private RectTransform _rect;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        private void LateUpdate()
        {
            Reposition();
        }

        public void Reposition()
        {
            if (CanvasRect == null)
            {
                return;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                CanvasRect, Input.mousePosition, null, out var local);

            var half = _rect.sizeDelta * 0.5f;
            var canvasHalf = CanvasRect.rect.size * 0.5f;

            // Sit to the right of the cursor; flip left when there is no room.
            float x = local.x + CursorGap + half.x;
            if (x + half.x > canvasHalf.x)
            {
                x = local.x - CursorGap - half.x;
            }
            float y = Mathf.Clamp(local.y, -canvasHalf.y + half.y, canvasHalf.y - half.y);

            _rect.anchoredPosition = new Vector2(x, y);
        }
    }
}
