using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// "Choose N cards" overlay, HTML-modal style: the whole screen behind it is blurred
    /// and darkened (screenshot bounced through shrinking render textures — bilinear
    /// filtering does the blur), cards float centered with no box, and Accept floats at
    /// the bottom-right. Click a card to lift it with a soft glow; click again to drop it.
    /// </summary>
    public sealed class CardPicker
    {
        private const float LiftHeight = 30f;
        private const float Spacing = 18f;
        private const float CardAspect = 0.714f; // width / height of the card art
        private static readonly Color GlowInnerColor = new Color(1f, 0.97f, 0.88f, 1f);
        private static readonly Color GlowOuterColor = new Color(1f, 0.96f, 0.82f, 0.80f);

        private sealed class Slot
        {
            public int Index;
            public RectTransform Lift;
            public GameObject GlowInner;
            public GameObject GlowOuter;
            public bool Selected;
        }

        private readonly CardPreview _preview;
        private readonly RectTransform _root;
        private readonly ModalBackdrop _backdrop;
        private readonly TMP_Text _title;
        private readonly RectTransform _rowHost;
        private readonly RectTransform _row;
        private readonly Button _accept;
        private readonly TMP_Text _acceptLabel;

        private readonly List<Slot> _slots = new List<Slot>();
        private int _requiredCount;
        private System.Action<List<int>> _onAccept;
        /// <summary>False for pickers whose cards are already near full size (the
        /// Lemon Lord choice) — a magnify pop-up there is just noise.</summary>
        private bool _previewEnabled = true;
        // Reference band beneath the pick row (the First Dibs on offer during the
        // Lemon Lord choice): read-only cards, edge-hover scrolled when they overflow.
        private readonly RectTransform _contextHost;
        private readonly RectTransform _contextRow;
        private readonly RectMask2D _contextMask;
        private readonly TMPro.TMP_Text _contextLabel;
        private float _contextScroll;

        public bool IsOpen { get; private set; }
        /// <summary>Diagnostics: open-but-invisible means a reveal died mid-flight.</summary>
        public bool RootVisible => _root.gameObject.activeSelf;

        public CardPicker(RectTransform canvasRoot, CardPreview preview, MonoBehaviour host)
        {
            _preview = preview;

            _root = UiKit.CreatePanel(canvasRoot, "CardPicker", new Color(0, 0, 0, 0));
            UiKit.Anchor(_root, Vector2.zero, Vector2.one);
            _backdrop = new ModalBackdrop(_root, host);

            // Floating title with a drop shadow — no bar.
            _title = UiKit.CreateText(_root, "", 40,
                TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.75f));
            UiKit.Anchor((RectTransform)_title.transform,
                new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.985f));
            UiKit.AddTextShadow(_title);

            // Card row host: invisible, full width — cards float on the blur.
            _rowHost = UiKit.CreatePanel(_root, "RowHost", new Color(0, 0, 0, 0));
            _rowHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(_rowHost, new Vector2(0.02f, 0.15f), new Vector2(0.98f, 0.88f));

            var rowGo = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(_rowHost, false);
            UiKit.Anchor((RectTransform)rowGo.transform, Vector2.zero, Vector2.one);
            var layout = rowGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            _row = (RectTransform)rowGo.transform;

            // Context band: reference cards under the pick row. Full-bleed and
            // masked; when the row overflows, hovering the screen edges scrolls it
            // (Tick) and the soft mask edge doubles as the "this scrolls" cue.
            _contextLabel = UiKit.CreateText(_root, "", 28,
                TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.55f));
            UiKit.Anchor((RectTransform)_contextLabel.transform,
                new Vector2(0f, 0.35f), new Vector2(1f, 0.415f));
            UiKit.AddTextShadow(_contextLabel);
            _contextHost = UiKit.CreatePanel(_root, "ContextBand", new Color(0, 0, 0, 0));
            _contextHost.GetComponent<Image>().raycastTarget = false;
            // Tall enough for a full 263px card + margins — a shorter band makes the
            // layout clamp card HEIGHT while width stays put, squashing the aspect.
            UiKit.Anchor(_contextHost, new Vector2(0f, 0.085f), new Vector2(1f, 0.35f));
            _contextMask = _contextHost.gameObject.AddComponent<RectMask2D>();
            var contextRowGo = new GameObject("ContextRow",
                typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            contextRowGo.transform.SetParent(_contextHost, false);
            _contextRow = (RectTransform)contextRowGo.transform;
            _contextRow.anchorMin = new Vector2(0f, 0f);
            _contextRow.anchorMax = new Vector2(0f, 1f);
            _contextRow.pivot = new Vector2(0f, 0.5f);
            _contextRow.offsetMin = new Vector2(24f, 6f);
            _contextRow.offsetMax = new Vector2(24f, -6f);
            var contextLayout = contextRowGo.GetComponent<HorizontalLayoutGroup>();
            contextLayout.spacing = 14;
            contextLayout.childAlignment = TextAnchor.MiddleLeft;
            contextLayout.childForceExpandWidth = false;
            contextLayout.childForceExpandHeight = false;
            // Control ON: CreateCardImage sizes cards via LayoutElement preferred
            // sizes, which only apply when the group controls its children (raw
            // rects default to 100x100). The band above is sized taller than a full
            // card, so the height clamp that once squashed the aspect can't engage.
            contextLayout.childControlWidth = true;
            contextLayout.childControlHeight = true;
            contextRowGo.GetComponent<ContentSizeFitter>().horizontalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            _contextHost.gameObject.SetActive(false);
            _contextLabel.gameObject.SetActive(false);

            // Accept floats bottom-right of the screen.
            var acceptGo = new GameObject("Accept", typeof(RectTransform), typeof(Image),
                typeof(Button), typeof(Shadow));
            acceptGo.transform.SetParent(_root, false);
            UiKit.Anchor((RectTransform)acceptGo.transform, new Vector2(0.80f, 0.035f), new Vector2(0.975f, 0.125f));
            var acceptImage = acceptGo.GetComponent<Image>();
            acceptImage.sprite = UiSprites.RoundedRect;
            acceptImage.type = Image.Type.Sliced;
            acceptImage.color = UiKit.ButtonColor;
            var acceptShadow = acceptGo.GetComponent<Shadow>();
            acceptShadow.effectColor = new Color(0, 0, 0, 0.5f);
            acceptShadow.effectDistance = new Vector2(3f, -3f);
            _accept = acceptGo.GetComponent<Button>();
            var colors = _accept.colors;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            _accept.colors = colors;
            _acceptLabel = UiKit.CreateText(acceptGo.transform, "Accept", 22,
                TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
            UiKit.Anchor((RectTransform)_acceptLabel.transform, Vector2.zero, Vector2.one);
            _accept.onClick.AddListener(Accept);

            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Open the picker. onAccept receives the selected indices (into the textures list),
        /// in the order they appear in the row. The overlay appears next frame, once the
        /// backdrop blur has been captured.
        /// </summary>
        public void Show(string title, IReadOnlyList<Texture2D> textures, int requiredCount,
            System.Action<List<int>> onAccept, bool preview = true,
            IReadOnlyList<Texture2D> context = null, string contextLabel = null)
        {
            IsOpen = true;
            _title.text = title;
            _requiredCount = requiredCount;
            _onAccept = onAccept;
            _previewEnabled = preview;
            _slots.Clear();
            UiKit.Clear(_row);

            // Context band (e.g. the First Dibs row during the lord pick): the pick
            // row cedes its lower stretch, and FitCardSize shrinks the picks to match.
            bool hasContext = context != null && context.Count > 0;
            _contextHost.gameObject.SetActive(hasContext);
            _contextLabel.gameObject.SetActive(hasContext);
            UiKit.Anchor(_rowHost,
                new Vector2(0.02f, hasContext ? 0.42f : 0.15f), new Vector2(0.98f, 0.88f));
            UiKit.Clear(_contextRow);
            _contextScroll = 0f;
            if (hasContext)
            {
                _contextLabel.text = contextLabel ?? "";
                foreach (var texture in context)
                {
                    var image = UiKit.CreateCardImage(_contextRow, texture, 188f, 263f);
                    // Small reference cards: the magnify preview is how you READ them.
                    _preview.Attach(image.gameObject, texture);
                }
                ApplyContextScroll(); // centered from the first frame when it fits
            }

            var (cardWidth, cardHeight) = FitCardSize(textures.Count);
            for (int i = 0; i < textures.Count; i++)
            {
                _slots.Add(BuildSlot(i, textures[i], cardWidth, cardHeight));
            }
            RefreshAccept();

            // Appears next frame, once the backdrop blur has been captured.
            _backdrop.Reveal(_root.gameObject);
        }

        public void Hide()
        {
            IsOpen = false;
            _root.gameObject.SetActive(false);
            _backdrop.Hide();
        }

        /// <summary>
        /// Edge-hover scrolling for the context band (the app calls this every
        /// frame): hover the left/right stretch of the band and it glides, same
        /// language as the board's stand overflow.
        /// </summary>
        public void Tick(Vector2 screenPosition)
        {
            if (!IsOpen || !_contextHost.gameObject.activeSelf)
            {
                return;
            }
            float overflow = _contextRow.rect.width + 48f - _contextHost.rect.width;
            var softness = overflow > 0f ? new Vector2Int(70, 0) : Vector2Int.zero;
            if (_contextMask.softness != softness)
            {
                _contextMask.softness = softness;
            }
            if (overflow <= 0f)
            {
                // Re-applied every frame: the centered position depends on rect widths
                // that only settle once the layout pass has run.
                _contextScroll = 0f;
                ApplyContextScroll();
                return;
            }
            _contextScroll = Mathf.Clamp(_contextScroll, 0f, overflow);
            if (!RectTransformUtility.RectangleContainsScreenPoint(_contextHost, screenPosition))
            {
                return;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _contextHost, screenPosition, null, out var local);
            var rect = _contextHost.rect;
            float fraction = (local.x - rect.xMin) / Mathf.Max(1f, rect.width);
            const float zone = 0.15f;
            float velocity = 0f;
            if (fraction < zone)
            {
                velocity = -Mathf.InverseLerp(zone, 0f, fraction);
            }
            else if (fraction > 1f - zone)
            {
                velocity = Mathf.InverseLerp(1f - zone, 1f, fraction);
            }
            if (velocity == 0f)
            {
                return;
            }
            _contextScroll = Mathf.Clamp(
                _contextScroll + velocity * 700f * Time.deltaTime, 0f, overflow);
            ApplyContextScroll();
        }

        private void ApplyContextScroll()
        {
            // Fits: centered. Overflows: left-anchored, edge-hover scrolled.
            float overflow = _contextRow.rect.width + 48f - _contextHost.rect.width;
            var position = _contextRow.anchoredPosition;
            position.x = overflow > 0f
                ? 24f - _contextScroll
                : (_contextHost.rect.width - _contextRow.rect.width) / 2f;
            _contextRow.anchoredPosition = position;
        }

        // ------------------------------------------------------------- cards

        /// <summary>Largest card size where the whole pool fits the host, capped for small pools.</summary>
        private (float Width, float Height) FitCardSize(int count)
        {
            Canvas.ForceUpdateCanvases();
            float hostWidth = _rowHost.rect.width > 10 ? _rowHost.rect.width : 1600f;
            float hostHeight = _rowHost.rect.height > 10 ? _rowHost.rect.height : 560f;

            float height = Mathf.Min(400f, hostHeight - LiftHeight - 24f);
            float width = height * CardAspect;
            float available = hostWidth - 48f - (count - 1) * Spacing;
            if (count * width > available)
            {
                width = available / count;
                height = width / CardAspect;
            }
            return (width, height);
        }

        private Slot BuildSlot(int index, Texture2D texture, float width, float height)
        {
            // Layout-controlled cell with headroom for the lift.
            var cell = new GameObject("Cell", typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_row, false);
            var layoutElement = cell.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = width + 8;
            layoutElement.preferredHeight = height + LiftHeight + 6;
            layoutElement.flexibleWidth = 0;
            layoutElement.flexibleHeight = 0;

            // The lifting container: anchored to the cell bottom, eased upward on select.
            var liftGo = new GameObject("Lift", typeof(RectTransform));
            liftGo.transform.SetParent(cell.transform, false);
            var lift = (RectTransform)liftGo.transform;
            lift.anchorMin = new Vector2(0.5f, 0f);
            lift.anchorMax = new Vector2(0.5f, 0f);
            lift.pivot = new Vector2(0.5f, 0f);
            lift.sizeDelta = new Vector2(width, height);
            lift.anchoredPosition = Vector2.zero;

            // Soft glow halo (hidden until selected): wide faint layer + tighter bright layer.
            var center = new Vector2(0.5f, 0.5f);
            var glowOuter = UiKit.CreateGlow(lift, center, center, Vector2.zero,
                width + 44, height + 44, GlowOuterColor);
            var glowInner = UiKit.CreateGlow(lift, center, center, Vector2.zero,
                width + 20, height + 20, GlowInnerColor);

            // Rounded card art.
            var image = UiKit.CreateCardImage(lift, texture, width, height);
            var frame = (RectTransform)image.transform.parent;
            UiKit.Anchor(frame, Vector2.zero, Vector2.one);

            var slot = new Slot
            {
                Index = index,
                Lift = lift,
                GlowInner = glowInner,
                GlowOuter = glowOuter,
            };
            if (_previewEnabled)
            {
                _preview.Attach(image.gameObject, texture);
            }
            UiKit.AddClick(image.gameObject, () => Toggle(slot));
            return slot;
        }

        private void Toggle(Slot slot)
        {
            if (!slot.Selected && SelectedCount() >= _requiredCount)
            {
                // A pick-one choice switches on click instead of demanding a
                // deselect first; multi-card picks still need room made.
                if (_requiredCount != 1)
                {
                    return;
                }
                foreach (var other in _slots)
                {
                    if (other.Selected)
                    {
                        SetSelected(other, false);
                    }
                }
            }
            SetSelected(slot, !slot.Selected);
            RefreshAccept();
        }

        private static void SetSelected(Slot slot, bool selected)
        {
            slot.Selected = selected;
            slot.GlowInner.SetActive(selected);
            slot.GlowOuter.SetActive(selected);
            UiTween.SlideTo(slot.Lift, selected ? new Vector2(0, LiftHeight) : Vector2.zero);
        }

        private int SelectedCount()
        {
            int count = 0;
            foreach (var slot in _slots)
            {
                if (slot.Selected)
                {
                    count++;
                }
            }
            return count;
        }

        private void RefreshAccept()
        {
            int selected = SelectedCount();
            _accept.interactable = selected == _requiredCount;
            _acceptLabel.text = $"Accept ({selected}/{_requiredCount})";
        }

        private void Accept()
        {
            var picked = new List<int>();
            foreach (var slot in _slots)
            {
                if (slot.Selected)
                {
                    picked.Add(slot.Index);
                }
            }
            if (picked.Count != _requiredCount)
            {
                return;
            }
            var callback = _onAccept;
            Hide();
            callback?.Invoke(picked);
        }
    }
}
