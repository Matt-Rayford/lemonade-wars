using System.Collections.Generic;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Discard viewer/picker: a full-screen browser over either discard pile, doubling
    // as the "pick a card out of a pile" surface for effects.
    public sealed partial class TableView
    {
        private RectTransform _discardOverlay;
        private RectTransform _discardGrid;
        private ScrollRect _discardScrollRect;
        private TMP_Text _discardTitle;
        private GameObject _discardTakeButton;
        private GameObject _discardBackButton;
        private TMP_Text _discardTakeLabel;
        private System.Action<int> _discardOnTake;

        /// <summary>
        /// True while the grid is up in PICK mode (serving a guided take flow) — the
        /// modal pipeline must stand down, or every pacing revision rebuilds the
        /// "Play X" prompt over the player mid-pick. Browsing (no take) doesn't count.
        /// </summary>
        public bool DiscardPickerOpen =>
            _discardOverlay.gameObject.activeSelf && _discardOnTake != null;
        private string _discardVerb = "TAKE";
        private System.Action _discardOnBack;
        private int? _discardSelectedId;
        private readonly Dictionary<int, GameObject> _discardSelectGlows =
            new Dictionary<int, GameObject>();

        /// <summary>
        /// Full-screen discard browser: dim backdrop, title, and a vertically scrolling
        /// grid of cards bounded to the center band. Click anywhere off a card to close.
        /// </summary>
        private void BuildDiscardViewer()
        {
            // On the canvas root, not the table: the HUD's message strip is built after
            // the table and would otherwise sit on top of the dimming, undimmed.
            _discardOverlay = UiKit.CreatePanel(_canvasRoot, "DiscardOverlay", new Color(0, 0, 0, 0.82f));
            UiKit.Anchor(_discardOverlay, Vector2.zero, Vector2.one);
            UiKit.AddClick(_discardOverlay.gameObject, CloseDiscardViewer);

            _discardTitle = UiKit.CreateText(_discardOverlay, "", 34,
                TextAnchor.MiddleCenter, Color.white);
            UiKit.Anchor((RectTransform)_discardTitle.transform,
                new Vector2(0.1f, 0.89f), new Vector2(0.9f, 0.97f));
            _discardTitle.raycastTarget = false;
            UiKit.AddTextShadow(_discardTitle);

            // Transparent-but-raycastable scroll surface: the wheel works anywhere in
            // the band, and a click between cards still closes the viewer.
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(_discardOverlay, false);
            UiKit.Anchor((RectTransform)scrollGo.transform, new Vector2(0.21f, 0.02f), new Vector2(0.79f, 0.89f));
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            UiKit.AddClick(scrollGo, CloseDiscardViewer);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            // Raw wheel deltas are tiny (~0.1/tick): the default sensitivity of 1
            // crawls. ~50px per tick moves a meaningful slice of a 392px card row.
            scroll.scrollSensitivity = 50f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            UiKit.Anchor((RectTransform)viewportGo.transform, Vector2.zero, Vector2.one);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform),
                typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero; // default is (100,100): +100px width overflow
            // Reading size — same scale as the Lemon Lord picker at game setup.
            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(280, 392);
            grid.spacing = new Vector2(18, 18);
            // Generous side padding: edge cards keep their rounded corners and glow
            // clear of the viewport mask instead of getting shaved off.
            grid.padding = new RectOffset(20, 20, 12, 12);
            grid.childAlignment = TextAnchor.UpperCenter;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = (RectTransform)viewportGo.transform;
            scroll.content = content;
            _discardGrid = content;
            _discardScrollRect = scroll;

            // Bottom-center: "TAKE <CARD>" — only exists while a pick is required.
            var buttonGo = new GameObject("TakeButton", typeof(RectTransform), typeof(Image), typeof(Shadow));
            buttonGo.transform.SetParent(_discardOverlay, false);
            var buttonRect = (RectTransform)buttonGo.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.sizeDelta = new Vector2(440f, 58f);
            buttonRect.anchoredPosition = new Vector2(0, 26f);
            var buttonImage = buttonGo.GetComponent<Image>();
            buttonImage.sprite = UiSprites.RoundedRect;
            buttonImage.type = Image.Type.Sliced;
            buttonImage.color = UiKit.ButtonColor;
            var buttonShadow = buttonGo.GetComponent<Shadow>();
            buttonShadow.effectColor = new Color(0, 0, 0, 0.5f);
            buttonShadow.effectDistance = new Vector2(0, -4f);
            _discardTakeLabel = UiKit.CreateText(buttonGo.transform, "", 24,
                TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
            _discardTakeLabel.raycastTarget = false;
            UiKit.Anchor((RectTransform)_discardTakeLabel.transform, Vector2.zero, Vector2.one);
            UiKit.AddHover(buttonGo,
                () => buttonImage.color = new Color(1f, 0.92f, 0.35f),
                () => buttonImage.color = UiKit.ButtonColor);
            UiKit.AddClick(buttonGo, () =>
            {
                var onTake = _discardOnTake;
                int? picked = _discardSelectedId;
                CloseDiscardViewer();
                if (onTake != null && picked is int id)
                {
                    onTake(id);
                }
            });
            _discardTakeButton = buttonGo;

            // Bottom-left: BACK — only for multi-step flows (e.g. Finders Keepers,
            // returning to the victim choice).
            var backGo = new GameObject("BackButton", typeof(RectTransform), typeof(Image), typeof(Shadow));
            backGo.transform.SetParent(_discardOverlay, false);
            var backRect = (RectTransform)backGo.transform;
            backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 0f);
            backRect.pivot = new Vector2(0f, 0f);
            backRect.sizeDelta = new Vector2(180f, 58f);
            backRect.anchoredPosition = new Vector2(30f, 26f);
            var backImage = backGo.GetComponent<Image>();
            backImage.sprite = UiSprites.RoundedRect;
            backImage.type = Image.Type.Sliced;
            backImage.color = new Color(0.15f, 0.18f, 0.25f, 0.96f);
            var backShadow = backGo.GetComponent<Shadow>();
            backShadow.effectColor = new Color(0, 0, 0, 0.5f);
            backShadow.effectDistance = new Vector2(0, -4f);
            var backLabel = UiKit.CreateText(backGo.transform, "BACK", 22,
                TextAnchor.MiddleCenter, new Color(0.96f, 0.96f, 0.92f));
            backLabel.raycastTarget = false;
            UiKit.Anchor((RectTransform)backLabel.transform, Vector2.zero, Vector2.one);
            UiKit.AddHover(backGo,
                () => backImage.color = new Color(0.22f, 0.26f, 0.35f, 0.96f),
                () => backImage.color = new Color(0.15f, 0.18f, 0.25f, 0.96f));
            UiKit.AddClick(backGo, () =>
            {
                var back = _discardOnBack;
                CloseDiscardViewer();
                back?.Invoke();
            });
            _discardBackButton = backGo;

            _discardOverlay.gameObject.SetActive(false);
        }

        /// <summary>Browse a discard pile (no selection, no button).</summary>
        private void OpenDiscardViewer(string title, IReadOnlyList<PlayerView.CardInfo> cards,
            bool blackMarket)
        {
            _discardTitle.text = cards.Count == 0 ? $"{title} — EMPTY" : $"{title} ({cards.Count})";
            FillDiscardGrid(cards, blackMarket, null);
            ShowDiscardOverlay();
        }

        /// <summary>Raised to the top of the canvas every time: everything built since (HUD strip, theatre) goes under its dim.</summary>
        private void ShowDiscardOverlay()
        {
            _discardOverlay.SetAsLastSibling();
            _discardOverlay.gameObject.SetActive(true);
        }

        /// <summary>
        /// Pick a card out of a pile (Reduce and Reuse, Reverse Engineer, an opponent's
        /// board...): click a card to select it, then confirm with the action button.
        /// Clicking off a card cancels — nothing has been submitted yet at that point.
        /// The verb names what confirming DOES: taking and trashing are not the same.
        /// </summary>
        public void OpenDiscardPicker(string title, IReadOnlyList<PlayerView.CardInfo> cards,
            bool blackMarket, System.Func<PlayerView.CardInfo, string> nameOf,
            System.Action<int> onTake, System.Action onBack = null, string verb = "TAKE",
            System.Func<PlayerView.CardInfo, string> disabledReason = null)
        {
            _discardTitle.text = title;
            _discardVerb = verb;
            _discardOnTake = onTake;
            _discardOnBack = onBack;
            FillDiscardGrid(cards, blackMarket, nameOf, disabledReason);
            _discardBackButton.SetActive(onBack != null);
            ShowDiscardOverlay();
        }

        private void FillDiscardGrid(IReadOnlyList<PlayerView.CardInfo> cards, bool blackMarket,
            System.Func<PlayerView.CardInfo, string> nameOf,
            System.Func<PlayerView.CardInfo, string> disabledReason = null)
        {
            UiKit.Clear(_discardGrid);
            _discardSelectGlows.Clear();
            _discardSelectedId = null;
            _discardTakeButton.SetActive(false);

            for (int i = cards.Count - 1; i >= 0; i--) // newest discard first
            {
                var card = cards[i];
                var texture = blackMarket
                    ? _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square)
                    : _art.Lemon(card.DefId);

                // Wrapper cell: the selection glow must sit OUTSIDE the card's rounded
                // mask, or it would be clipped to the card shape.
                var cellGo = new GameObject("DiscardCell", typeof(RectTransform));
                cellGo.transform.SetParent(_discardGrid, false);
                var cell = (RectTransform)cellGo.transform;
                var glow = UiKit.CreateGlow(cell, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, 280 + 52, 392 + 52, DropGlowHot);
                var image = UiKit.CreateCardImage(cell, texture, 280, 392);
                UiKit.Anchor((RectTransform)image.transform.parent, Vector2.zero, Vector2.one);

                _preview.Attach(image.gameObject, texture);

                // The pile always shows every copy (players count it); a copy with no
                // legal take right now stays visible but dimmed, with the reason on it.
                string reason = disabledReason?.Invoke(card);
                if (reason != null)
                {
                    image.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                    var reasonBar = UiKit.CreatePanel(cell, "DisabledReason",
                        new Color(0f, 0f, 0f, 0.72f));
                    UiKit.Anchor(reasonBar, new Vector2(0f, 0.42f), new Vector2(1f, 0.56f));
                    reasonBar.GetComponent<Image>().raycastTarget = false;
                    var reasonText = UiKit.CreateText(reasonBar, reason, 20,
                        TextAnchor.MiddleCenter, Color.white);
                    UiKit.Anchor((RectTransform)reasonText.transform, Vector2.zero, Vector2.one);
                    continue;
                }
                if (nameOf != null)
                {
                    int instanceId = card.InstanceId;
                    string takeName = nameOf(card);
                    _discardSelectGlows[instanceId] = glow;
                    UiKit.AddClick(image.gameObject, () => SelectDiscardCard(instanceId, takeName));
                }
            }
        }

        private void SelectDiscardCard(int instanceId, string name)
        {
            foreach (var pair in _discardSelectGlows)
            {
                pair.Value.SetActive(pair.Key == instanceId);
            }
            _discardSelectedId = instanceId;
            _discardTakeLabel.text = $"{_discardVerb} {name.ToUpperInvariant()}";
            _discardTakeButton.SetActive(true);
        }

        /// <summary>
        /// Hover-based scrolling for the discard browser/picker: the top and bottom 20%
        /// of the screen glide the grid, faster nearer the edge. Call every frame.
        /// </summary>
        public void TickDiscardScroll(Vector2 screenPosition)
        {
            if (!_discardOverlay.gameObject.activeSelf)
            {
                return;
            }
            float scrollable = _discardGrid.rect.height - _discardScrollRect.viewport.rect.height;
            if (scrollable <= 1f)
            {
                return; // everything fits, nothing to scroll
            }
            float fraction = screenPosition.y / Mathf.Max(1f, Screen.height);
            const float zone = 0.2f;
            float direction = 0f;
            if (fraction > 1f - zone)
            {
                direction = Mathf.InverseLerp(1f - zone, 1f, fraction);
            }
            else if (fraction < zone)
            {
                direction = -Mathf.InverseLerp(zone, 0f, fraction);
            }
            if (direction == 0f)
            {
                return;
            }
            _discardScrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                _discardScrollRect.verticalNormalizedPosition +
                direction * 900f * Time.deltaTime / scrollable);
        }

        private void CloseDiscardViewer()
        {
            _discardOverlay.gameObject.SetActive(false);
            _discardOnTake = null;
            _discardOnBack = null;
            _discardSelectedId = null;
            _discardTakeButton.SetActive(false);
            _discardBackButton.SetActive(false);
        }
    }
}
