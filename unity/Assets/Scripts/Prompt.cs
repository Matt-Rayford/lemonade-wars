using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Centered option prompt used for both contextual action menus (with Cancel) and
    /// blocking window/decision modals (options only). One at a time.
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

        private readonly RectTransform _root;
        private readonly Text _title;
        private readonly RectTransform _cardStrip;
        private readonly RectTransform _optionList;
        private readonly CardArt _art;

        public bool IsOpen { get; private set; }

        public Prompt(RectTransform canvasRoot, CardArt art)
        {
            _art = art;

            // Dim layer that blocks clicks to the table underneath.
            _root = UiKit.CreatePanel(canvasRoot, "Prompt", new Color(0, 0, 0, 0.72f));
            UiKit.Anchor(_root, Vector2.zero, Vector2.one);

            var window = UiKit.CreatePanel(_root, "Window", new Color(0.13f, 0.16f, 0.22f, 0.98f));
            UiKit.Anchor(window, new Vector2(0.28f, 0.12f), new Vector2(0.72f, 0.88f));

            var titlePanel = UiKit.CreatePanel(window, "Title", new Color(0.98f, 0.83f, 0.10f));
            UiKit.Anchor(titlePanel, new Vector2(0, 0.90f), new Vector2(1, 1));
            _title = UiKit.CreateText(titlePanel, "", 24, TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
            UiKit.Anchor((RectTransform)_title.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 0), new Vector2(-10, 0));

            var stripHost = UiKit.CreatePanel(window, "Cards", new Color(0, 0, 0, 0.2f));
            UiKit.Anchor(stripHost, new Vector2(0, 0.55f), new Vector2(1, 0.90f));
            _cardStrip = UiKit.CreateCardRow(stripHost, "CardStrip");
            var stripLayout = _cardStrip.GetComponent<HorizontalLayoutGroup>();
            stripLayout.childAlignment = TextAnchor.MiddleCenter;

            var listHost = UiKit.CreatePanel(window, "Options", new Color(0, 0, 0, 0));
            UiKit.Anchor(listHost, new Vector2(0, 0), new Vector2(1, 0.55f));
            _optionList = UiKit.CreateScrollList(listHost);

            Hide();
        }

        /// <summary>Show a prompt. Pass showCancel for optional (contextual) menus.</summary>
        public void Show(string title, IReadOnlyList<Texture2D> cards,
            IReadOnlyList<Option> options, bool showCancel)
        {
            IsOpen = true;
            _root.gameObject.SetActive(true);
            _title.text = title;

            UiKit.Clear(_cardStrip);
            if (cards != null)
            {
                foreach (var texture in cards)
                {
                    if (texture != null)
                    {
                        UiKit.CreateCardImage(_cardStrip, texture, 130, 182);
                    }
                }
            }

            UiKit.Clear(_optionList);
            foreach (var option in options)
            {
                var captured = option;
                UiKit.CreateButton(_optionList, captured.Label, 17, () =>
                {
                    Hide();
                    captured.OnPick();
                });
            }
            if (showCancel)
            {
                UiKit.CreateButton(_optionList, "Cancel", 17, Hide);
            }
        }

        public void Hide()
        {
            IsOpen = false;
            _root.gameObject.SetActive(false);
        }
    }

    /// <summary>Hover-to-enlarge card preview, anchored to the left side of the screen.</summary>
    public sealed class CardPreview
    {
        private readonly RectTransform _root;
        private readonly RawImage _image;

        public CardPreview(RectTransform canvasRoot)
        {
            _root = UiKit.CreatePanel(canvasRoot, "Preview", new Color(0, 0, 0, 0));
            UiKit.Anchor(_root, new Vector2(0.005f, 0.28f), new Vector2(0.20f, 0.78f));
            _root.GetComponent<Image>().raycastTarget = false;

            // Rounded frame + masked texture, same treatment as table cards.
            var frame = new GameObject("PreviewFrame", typeof(RectTransform), typeof(Image), typeof(Mask));
            frame.transform.SetParent(_root, false);
            UiKit.Anchor((RectTransform)frame.transform, Vector2.zero, Vector2.one,
                new Vector2(6, 6), new Vector2(-6, -6));
            var frameImage = frame.GetComponent<Image>();
            frameImage.sprite = UiSprites.RoundedRect;
            frameImage.type = Image.Type.Sliced;
            frameImage.raycastTarget = false;
            frame.GetComponent<Mask>().showMaskGraphic = true;

            var go = new GameObject("PreviewImage", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(frame.transform, false);
            UiKit.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);
            _image = go.GetComponent<RawImage>();
            _image.raycastTarget = false;
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
        }

        public void Hide() => _root.gameObject.SetActive(false);

        /// <summary>Wire a card image to preview on hover.</summary>
        public void Attach(GameObject cardGo, Texture2D texture)
        {
            UiKit.AddHover(cardGo, () => Show(texture), Hide);
        }
    }
}
