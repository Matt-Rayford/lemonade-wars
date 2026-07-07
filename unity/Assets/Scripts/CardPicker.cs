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
            System.Action<List<int>> onAccept)
        {
            IsOpen = true;
            _title.text = title;
            _requiredCount = requiredCount;
            _onAccept = onAccept;
            _slots.Clear();
            UiKit.Clear(_row);

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
            _preview.Attach(image.gameObject, texture);
            UiKit.AddClick(image.gameObject, () => Toggle(slot));
            return slot;
        }

        private void Toggle(Slot slot)
        {
            if (!slot.Selected && SelectedCount() >= _requiredCount)
            {
                return; // at the limit; deselect something first
            }
            slot.Selected = !slot.Selected;
            slot.GlowInner.SetActive(slot.Selected);
            slot.GlowOuter.SetActive(slot.Selected);
            UiTween.SlideTo(slot.Lift, slot.Selected ? new Vector2(0, LiftHeight) : Vector2.zero);
            RefreshAccept();
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
