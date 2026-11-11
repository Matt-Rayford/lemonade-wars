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
        /// <summary>Reading size, the same scale as the Lemon Lord picker at game setup.</summary>
        private const float DiscardCardWidthPx = 280f;
        private const float DiscardCardHeightPx = 392f;
        private const float DiscardSelectGlowPx = 52f;
        private const float DiscardEdgeScrollZone = 0.2f;
        private const float DiscardEdgeScrollPxPerSecond = 900f;
        private static readonly Color DiscardBackButtonColor = new Color(0.15f, 0.18f, 0.25f, 0.96f);

        private RectTransform _discardOverlay;
        private RectTransform _discardGrid;
        private ScrollRect _discardScrollRect;
        private TMP_Text _discardTitle;
        private GameObject _discardTakeButton;
        private GameObject _discardBackButton;
        private TMP_Text _discardTakeLabel;
        private System.Action<int> _discardOnTake;

        /// <summary>
        /// True while the grid is up to pick from (browsing does not count): the modal
        /// pipeline must stand down, or each pacing revision rebuilds its prompt over the pick.
        /// </summary>
        public bool DiscardPickerOpen =>
            _discardOverlay.gameObject.activeSelf && _discardOnTake != null;
        private string _discardVerb = "TAKE";
        private System.Action _discardOnBack;
        private int? _discardSelectedId;
        private readonly Dictionary<int, GameObject> _discardSelectGlows =
            new Dictionary<int, GameObject>();

        /// <summary>A click anywhere off a card closes the viewer.</summary>
        private void BuildDiscardViewer()
        {
            // On the canvas root, not the table: the HUD's message strip is built after
            // the table and would otherwise sit above the dimming, undimmed.
            _discardOverlay = UiKit.CreatePanel(_canvasRoot, "DiscardOverlay", new Color(0, 0, 0, 0.82f));
            UiKit.Anchor(_discardOverlay, Vector2.zero, Vector2.one);
            UiKit.AddClick(_discardOverlay.gameObject, CloseDiscardViewer);

            _discardTitle = UiKit.CreateText(_discardOverlay, "", 34,
                TextAnchor.MiddleCenter, Color.white);
            UiKit.Anchor((RectTransform)_discardTitle.transform,
                new Vector2(0.1f, 0.89f), new Vector2(0.9f, 0.97f));
            _discardTitle.raycastTarget = false;
            UiKit.AddTextShadow(_discardTitle);

            BuildDiscardScrollGrid();
            BuildDiscardTakeButton();
            BuildDiscardBackButton();

            _discardOverlay.gameObject.SetActive(false);
        }

        private void BuildDiscardScrollGrid()
        {
            // Transparent but raycastable: the wheel works anywhere in the band, and a
            // click between cards still closes the viewer.
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(_discardOverlay, false);
            UiKit.Anchor((RectTransform)scrollGo.transform, new Vector2(0.21f, 0.02f), new Vector2(0.79f, 0.89f));
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            UiKit.AddClick(scrollGo, CloseDiscardViewer);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            // Raw wheel deltas are tiny (~0.1 per tick), so the default sensitivity of 1 crawls.
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
            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(DiscardCardWidthPx, DiscardCardHeightPx);
            grid.spacing = new Vector2(18, 18);
            // Side padding keeps edge cards' rounded corners and glow clear of the viewport mask.
            grid.padding = new RectOffset(20, 20, 12, 12);
            grid.childAlignment = TextAnchor.UpperCenter;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = (RectTransform)viewportGo.transform;
            scroll.content = content;
            _discardGrid = content;
            _discardScrollRect = scroll;
        }

        /// <summary>Bottom-centre "TAKE CARD NAME": shown only once a card is selected.</summary>
        private void BuildDiscardTakeButton()
        {
            var buttonGo = BuildDiscardButtonPill("TakeButton", new Vector2(0.5f, 0f),
                new Vector2(440f, 58f), new Vector2(0, 26f), UiKit.ButtonColor);
            var buttonImage = buttonGo.GetComponent<Image>();
            _discardTakeLabel = UiKit.CreateText(buttonGo.transform, "", 24,
                TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
            _discardTakeLabel.raycastTarget = false;
            UiKit.Anchor((RectTransform)_discardTakeLabel.transform, Vector2.zero, Vector2.one);
            UiKit.AddHover(buttonGo,
                () => buttonImage.color = new Color(1f, 0.92f, 0.35f),
                () => buttonImage.color = UiKit.ButtonColor);
            UiKit.AddClick(buttonGo, TakeSelectedDiscardCard);
            _discardTakeButton = buttonGo;
        }

        /// <summary>Bottom-left BACK: only for multi-step flows (Finders Keepers returning to its victim choice).</summary>
        private void BuildDiscardBackButton()
        {
            var backGo = BuildDiscardButtonPill("BackButton", new Vector2(0f, 0f),
                new Vector2(180f, 58f), new Vector2(30f, 26f), DiscardBackButtonColor);
            var backImage = backGo.GetComponent<Image>();
            var backLabel = UiKit.CreateText(backGo.transform, "BACK", 22,
                TextAnchor.MiddleCenter, new Color(0.96f, 0.96f, 0.92f));
            backLabel.raycastTarget = false;
            UiKit.Anchor((RectTransform)backLabel.transform, Vector2.zero, Vector2.one);
            UiKit.AddHover(backGo,
                () => backImage.color = new Color(0.22f, 0.26f, 0.35f, 0.96f),
                () => backImage.color = DiscardBackButtonColor);
            UiKit.AddClick(backGo, LeaveDiscardPickerBackwards);
            _discardBackButton = backGo;
        }

        private GameObject BuildDiscardButtonPill(string name, Vector2 bottomAnchor, Vector2 size,
            Vector2 position, Color color)
        {
            var pillGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Shadow));
            pillGo.transform.SetParent(_discardOverlay, false);
            var rect = (RectTransform)pillGo.transform;
            rect.anchorMin = rect.anchorMax = bottomAnchor;
            rect.pivot = bottomAnchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = pillGo.GetComponent<Image>();
            image.sprite = UiSprites.RoundedRect;
            image.type = Image.Type.Sliced;
            image.color = color;
            var shadow = pillGo.GetComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.5f);
            shadow.effectDistance = new Vector2(0, -4f);
            return pillGo;
        }

        private void TakeSelectedDiscardCard()
        {
            var onTake = _discardOnTake;
            int? picked = _discardSelectedId;
            CloseDiscardViewer();
            if (onTake != null && picked is int id)
            {
                onTake(id);
            }
        }

        private void LeaveDiscardPickerBackwards()
        {
            var back = _discardOnBack;
            CloseDiscardViewer();
            back?.Invoke();
        }

        /// <summary>Browse a discard pile (no selection, no button).</summary>
        private void OpenDiscardViewer(string title, IReadOnlyList<PlayerView.CardInfo> cards,
            bool blackMarket)
        {
            _discardTitle.text = cards.Count == 0 ? $"{title} — EMPTY" : $"{title} ({cards.Count})";
            FillDiscardGrid(cards, blackMarket, null);
            ShowDiscardOverlay();
        }

        /// <summary>Raised to the top of the canvas every time, so everything built since (HUD strip, theatre) goes under its dim.</summary>
        private void ShowDiscardOverlay()
        {
            _discardOverlay.SetAsLastSibling();
            _discardOverlay.gameObject.SetActive(true);
        }

        /// <summary>
        /// Pick a card out of a pile: click one to select it, then confirm with the action
        /// button. A click off the cards cancels; nothing has been submitted by then.
        /// </summary>
        /// <param name="verb">Names what confirming does: taking and trashing are not the same.</param>
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

                // The glow sits in a wrapper cell, outside the card's rounded mask, which would clip it.
                var cellGo = new GameObject("DiscardCell", typeof(RectTransform));
                cellGo.transform.SetParent(_discardGrid, false);
                var cell = (RectTransform)cellGo.transform;
                var glow = UiKit.CreateGlow(cell, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, DiscardCardWidthPx + DiscardSelectGlowPx,
                    DiscardCardHeightPx + DiscardSelectGlowPx, DropGlowHot);
                var image = UiKit.CreateCardImage(cell, texture, DiscardCardWidthPx, DiscardCardHeightPx);
                UiKit.Anchor((RectTransform)image.transform.parent, Vector2.zero, Vector2.one);

                _preview.Attach(image.gameObject, texture);

                string reason = disabledReason?.Invoke(card);
                if (reason != null)
                {
                    DimDiscardCardWithReason(cell, image, reason);
                }
                else if (nameOf != null)
                {
                    int instanceId = card.InstanceId;
                    string takeName = nameOf(card);
                    _discardSelectGlows[instanceId] = glow;
                    UiKit.AddClick(image.gameObject, () => SelectDiscardCard(instanceId, takeName));
                }
            }
        }

        /// <summary>The pile shows every copy (players count it); one with no legal take stays visible, dimmed.</summary>
        private static void DimDiscardCardWithReason(RectTransform cell, RawImage image, string reason)
        {
            image.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            var reasonBar = UiKit.CreatePanel(cell, "DisabledReason",
                new Color(0f, 0f, 0f, 0.72f));
            UiKit.Anchor(reasonBar, new Vector2(0f, 0.42f), new Vector2(1f, 0.56f));
            reasonBar.GetComponent<Image>().raycastTarget = false;
            var reasonText = UiKit.CreateText(reasonBar, reason, 20,
                TextAnchor.MiddleCenter, Color.white);
            UiKit.Anchor((RectTransform)reasonText.transform, Vector2.zero, Vector2.one);
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
        /// Call every frame: with the viewer open, the top and bottom 20% of the screen
        /// glide the grid, faster nearer the edge.
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
                return;
            }
            float fraction = screenPosition.y / Mathf.Max(1f, Screen.height);
            float direction = 0f;
            if (fraction > 1f - DiscardEdgeScrollZone)
            {
                direction = Mathf.InverseLerp(1f - DiscardEdgeScrollZone, 1f, fraction);
            }
            else if (fraction < DiscardEdgeScrollZone)
            {
                direction = -Mathf.InverseLerp(DiscardEdgeScrollZone, 0f, fraction);
            }
            if (direction == 0f)
            {
                return;
            }
            _discardScrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                _discardScrollRect.verticalNormalizedPosition +
                direction * DiscardEdgeScrollPxPerSecond * Time.deltaTime / scrollable);
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
