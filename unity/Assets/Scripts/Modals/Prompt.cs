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
            /// <summary>When set, the button wears a thumbnail and Alt/Cmd-hover previews the card.</summary>
            public readonly Texture2D Card;

            public Option(string label, System.Action onPick, Texture2D card = null)
            {
                Label = label;
                OnPick = onPick;
                Card = card;
            }
        }

        private static readonly Color ButtonIdle = new Color(0.15f, 0.18f, 0.25f, 0.96f);
        private static readonly Color TextIdle = new Color(0.96f, 0.96f, 0.92f);
        /// <summary>De-emphasized option text (the Cancel row).</summary>
        private static readonly Color TextMuted = new Color(0.75f, 0.75f, 0.75f);

        private readonly CardPreview _preview;
        private readonly ModalBackdrop _backdrop;
        private readonly TMP_Text _title;
        private readonly RectTransform _root;
        private readonly RectTransform _cardStrip;
        private readonly RectTransform _optionList;
        private RectTransform _cancelHost;
        private TMP_Text _moreHint;

        public bool IsOpen { get; private set; }
        /// <summary>True while an optional (dismissable) prompt is up — Esc may close it.</summary>
        public bool IsCancelable { get; private set; }
        /// <summary>Diagnostics: open-but-invisible means a reveal died mid-flight.</summary>
        public bool RootVisible => _root.gameObject.activeSelf;

        public Prompt(RectTransform canvasRoot, MonoBehaviour host, CardPreview preview = null)
        {
            _preview = preview;
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

            // Options flow in a centered column below the card. The scroll area stops
            // short of the bottom: Cancel lives OUTSIDE it, pinned and always visible,
            // so a flooded option list can never hide the way out.
            var listHost = UiKit.CreatePanel(_root, "Options", new Color(0, 0, 0, 0));
            UiKit.Anchor(listHost, new Vector2(0.27f, 0.03f), new Vector2(0.73f, 0.44f));
            var scrollHost = UiKit.CreatePanel(listHost, "OptionScroll", new Color(0, 0, 0, 0));
            UiKit.Anchor(scrollHost, new Vector2(0, 0.17f), new Vector2(1, 1));
            _optionList = UiKit.CreateScrollList(scrollHost);
            _optionList.GetComponent<VerticalLayoutGroup>().spacing = 8;

            _moreHint = UiKit.CreateText(listHost, "scroll for more options", 13,
                TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.55f, 0.9f), body: true);
            _moreHint.raycastTarget = false;
            UiKit.Anchor((RectTransform)_moreHint.transform, new Vector2(0, 0.155f), new Vector2(1, 0.21f));
            UiKit.AddTextShadow(_moreHint);

            _cancelHost = UiKit.CreatePanel(listHost, "CancelRow", new Color(0, 0, 0, 0));
            UiKit.Anchor(_cancelHost, new Vector2(0, 0), new Vector2(1, 0.14f));

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
            UiKit.Clear(_cancelHost);
            IsCancelable = showCancel;
            // Big lists compress so more choices fit before scrolling starts.
            bool compact = options.Count > 8;
            foreach (var option in options)
            {
                if (option.OnPick == null)
                {
                    // No action = pure information (the end-game score rows): a plain
                    // line, not a button pretending it does something.
                    AddStaticRow(_optionList, option.Label, compact);
                    continue;
                }
                AddOptionButton(_optionList, option.Label, option.OnPick,
                    emphasized: true, option.Card, compact);
            }
            _moreHint.gameObject.SetActive(options.Count > 9);
            if (showCancel)
            {
                var cancel = AddOptionButton(_cancelHost, "Cancel", null,
                    emphasized: false, null, compact: false);
                UiKit.Anchor((RectTransform)cancel.transform, Vector2.zero, Vector2.one);
            }

            // Appears next frame, once the backdrop blur has been captured.
            _backdrop.Reveal(_root.gameObject);
        }

        public void Hide()
        {
            IsOpen = false;
            IsCancelable = false;
            _root.gameObject.SetActive(false);
            _backdrop.Hide();
        }

        /// <summary>Read-only line in the option column — same footprint, no chrome.</summary>
        private static void AddStaticRow(RectTransform parent, string label, bool compact)
        {
            var go = new GameObject("StaticRow", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = compact ? 34 : 46;
            var text = UiKit.CreateText(go.transform, label, compact ? 16 : 19,
                TextAnchor.MiddleCenter, TextIdle);
            text.raycastTarget = false;
            UiKit.AddTextShadow(text);
            UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(12, 2), new Vector2(-12, -2));
        }

        /// <summary>Dark rounded button that turns lemonade-yellow on hover.</summary>
        private GameObject AddOptionButton(RectTransform parent, string label, System.Action onPick,
            bool emphasized, Texture2D card, bool compact)
        {
            var go = new GameObject("Option", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var background = go.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            background.color = ButtonIdle;
            var layout = go.GetComponent<LayoutElement>();
            layout.minHeight = compact ? 34 : 46;
            layout.flexibleWidth = 1;

            var text = UiKit.CreateText(go.transform, label, compact ? 15 : 18, TextAnchor.MiddleCenter,
                emphasized ? TextIdle : TextMuted);
            UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(12, 2), new Vector2(card != null ? -44 : -12, -2));

            if (card != null)
            {
                // Mini card on the right edge; Alt/Cmd over the button blows it up so
                // the option's rules text is always one keypress away.
                var thumbGo = new GameObject("CardThumb", typeof(RectTransform), typeof(RawImage));
                thumbGo.transform.SetParent(go.transform, false);
                var thumbRect = (RectTransform)thumbGo.transform;
                thumbRect.anchorMin = thumbRect.anchorMax = new Vector2(1f, 0.5f);
                thumbRect.pivot = new Vector2(1f, 0.5f);
                thumbRect.sizeDelta = compact ? new Vector2(22f, 30f) : new Vector2(28f, 39f);
                thumbRect.anchoredPosition = new Vector2(-8f, 0);
                var thumbImage = thumbGo.GetComponent<RawImage>();
                thumbImage.texture = card;
                thumbImage.raycastTarget = false;
                _preview?.Attach(go, card);
            }

            UiKit.AddHover(go,
                () =>
                {
                    background.color = UiKit.ButtonColor;
                    text.color = UiKit.ButtonTextColor;
                },
                () =>
                {
                    background.color = ButtonIdle;
                    text.color = emphasized ? TextIdle : TextMuted;
                });
            UiKit.AddClick(go, () =>
            {
                Hide();
                onPick?.Invoke();
            });
            return go;
        }
    }
}
