using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Non-modal response windows: instead of a blurred full-screen prompt, the attack
    /// takes over the (greyed-out) market band up top and the reaction buttons slide
    /// into the log column — the table itself stays live, so the player can check the
    /// attacker's money, hand size, and board before deciding whether to react.
    /// </summary>
    public sealed class ReactionPanel
    {
        private static readonly Color ButtonIdle = new Color(0.15f, 0.18f, 0.25f, 0.96f);

        private readonly CardPreview _preview;
        private readonly RectTransform _root;
        private readonly TMP_Text _title;
        private readonly RectTransform _cardStrip;
        private readonly RectTransform _optionList;

        public bool IsOpen { get; private set; }

        public ReactionPanel(RectTransform canvasRoot, CardPreview preview)
        {
            _preview = preview;
            // Transparent full-screen root that does NOT swallow input: only its two
            // zones (market band + log column) are raycast surfaces.
            _root = UiKit.CreatePanel(canvasRoot, "Reaction", new Color(0, 0, 0, 0));
            _root.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(_root, Vector2.zero, Vector2.one);

            // ---- top: the market band, greyed out and hosting the attack ----
            var cardZone = UiKit.CreatePanel(_root, "ReactionCards", new Color(0.04f, 0.05f, 0.08f, 0.93f));
            UiKit.Anchor(cardZone, new Vector2(0f, 0.695f), new Vector2(1f, 0.955f));

            _title = UiKit.CreateText(cardZone, "", 26, TextAnchor.MiddleCenter,
                new Color(1f, 0.92f, 0.55f));
            _title.raycastTarget = false;
            UiKit.Anchor((RectTransform)_title.transform, new Vector2(0.02f, 0.74f), new Vector2(0.98f, 1f));
            UiKit.AddTextShadow(_title);

            var stripGo = new GameObject("CardRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            stripGo.transform.SetParent(cardZone, false);
            UiKit.Anchor((RectTransform)stripGo.transform, new Vector2(0.1f, 0.02f), new Vector2(0.9f, 0.72f));
            var stripLayout = stripGo.GetComponent<HorizontalLayoutGroup>();
            stripLayout.spacing = 14;
            stripLayout.childAlignment = TextAnchor.MiddleCenter;
            stripLayout.childForceExpandWidth = false;
            stripLayout.childForceExpandHeight = false;
            stripLayout.childControlWidth = true;
            stripLayout.childControlHeight = true;
            _cardStrip = (RectTransform)stripGo.transform;

            // ---- right: reactions where the action log lives ----
            var optionZone = UiKit.CreatePanel(_root, "ReactionOptions", new Color(0.05f, 0.07f, 0.11f, 0.96f));
            UiKit.Anchor(optionZone, new Vector2(0.79f, 0.24f), new Vector2(1f, 0.695f),
                new Vector2(4, 0), new Vector2(-10, -4));

            var header = UiKit.CreateText(optionZone, "YOUR RESPONSE", 16, TextAnchor.MiddleCenter,
                new Color(0.98f, 0.83f, 0.10f));
            header.raycastTarget = false;
            UiKit.Anchor((RectTransform)header.transform, new Vector2(0f, 0.92f), new Vector2(1f, 1f));

            var listHost = UiKit.CreatePanel(optionZone, "Options", new Color(0, 0, 0, 0));
            listHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(listHost, new Vector2(0f, 0.09f), new Vector2(1f, 0.915f),
                new Vector2(6, 0), new Vector2(-6, 0));
            _optionList = UiKit.CreateScrollList(listHost);
            _optionList.GetComponent<VerticalLayoutGroup>().spacing = 8;

            var hint = UiKit.CreateText(optionZone, "or click a reaction card in your hand",
                13, TextAnchor.MiddleCenter, new Color(0.62f, 0.65f, 0.70f), body: true);
            hint.raycastTarget = false;
            UiKit.Anchor((RectTransform)hint.transform, new Vector2(0f, 0f), new Vector2(1f, 0.085f));

            _root.gameObject.SetActive(false);
        }

        public void Show(string title, IReadOnlyList<Texture2D> cards, IReadOnlyList<Prompt.Option> options)
        {
            IsOpen = true;
            _title.text = title;

            UiKit.Clear(_cardStrip);
            if (cards != null)
            {
                // Sized to sit inside the band under the title.
                foreach (var texture in cards)
                {
                    if (texture != null)
                    {
                        var image = UiKit.CreateCardImage(_cardStrip, texture, 126f, 176f);
                        _preview?.Attach(image.gameObject, texture);
                    }
                }
            }

            UiKit.Clear(_optionList);
            foreach (var option in options)
            {
                AddOptionButton(option);
            }

            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            IsOpen = false;
            _root.gameObject.SetActive(false);
        }

        /// <summary>Same language as the prompt's options: dark row, yellow on hover.</summary>
        private void AddOptionButton(Prompt.Option option)
        {
            var go = new GameObject("Option", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(_optionList, false);
            var background = go.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            background.color = ButtonIdle;
            var layout = go.GetComponent<LayoutElement>();
            layout.minHeight = 48;
            layout.flexibleWidth = 1;

            var text = UiKit.CreateText(go.transform, option.Label, 15, TextAnchor.MiddleLeft,
                new Color(0.96f, 0.96f, 0.92f));
            UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 2), new Vector2(option.Card != null ? -40 : -10, -2));

            if (option.Card != null)
            {
                var thumbGo = new GameObject("CardThumb", typeof(RectTransform), typeof(RawImage));
                thumbGo.transform.SetParent(go.transform, false);
                var thumbRect = (RectTransform)thumbGo.transform;
                thumbRect.anchorMin = thumbRect.anchorMax = new Vector2(1f, 0.5f);
                thumbRect.pivot = new Vector2(1f, 0.5f);
                thumbRect.sizeDelta = new Vector2(26f, 36f);
                thumbRect.anchoredPosition = new Vector2(-6f, 0);
                var thumbImage = thumbGo.GetComponent<RawImage>();
                thumbImage.texture = option.Card;
                thumbImage.raycastTarget = false;
                _preview?.Attach(go, option.Card);
            }

            var onPick = option.OnPick;
            UiKit.AddHover(go,
                () =>
                {
                    background.color = UiKit.ButtonColor;
                    text.color = UiKit.ButtonTextColor;
                },
                () =>
                {
                    background.color = ButtonIdle;
                    text.color = new Color(0.96f, 0.96f, 0.92f);
                });
            UiKit.AddClick(go, () =>
            {
                Hide();
                onPick?.Invoke();
            });
        }
    }
}
