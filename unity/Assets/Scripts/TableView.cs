using System.Collections.Generic;
using System.Linq;
using System.Text;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The game table, rendered purely from a PlayerView (+ static CardDatabase for names
    /// and costs) — identical for local and networked sessions. Interactive elements get
    /// glow affordances and click/drag through to the app's handlers.
    /// </summary>
    public sealed class TableView
    {
        public System.Action<int> OnHandCard;      // lemon instance id

        // Market drag & drop.
        public System.Func<int, bool> CanBuyMarket;         // market index -> any legal buy?
        public System.Action<int> OnMarketDragStart;        // market index
        public System.Action OnMarketDragEnd;
        public System.Action<int, int?> OnMarketDrop;       // market index, stand id (null = turf)

        // Supply drag & drop (stand purchases with row-position insert).
        public System.Func<string, bool> CanBuySupply;      // stand type -> any legal buy?
        public System.Action<string, int> OnSupplyDrop;     // stand type, insert index

        private static readonly Color GlowInnerColor = new Color(1f, 0.97f, 0.88f, 1f);
        private static readonly Color GlowOuterColor = new Color(1f, 0.96f, 0.82f, 0.80f);
        /// <summary>Drop zones glow lemonade-yellow while a card is being dragged.</summary>
        private static readonly Color DropGlowHot = new Color(1f, 0.93f, 0.45f, 1f);
        private static readonly Color DropGlowWide = new Color(1f, 0.82f, 0.10f, 1f);

        private readonly List<(int? StandId, GameObject Glow)> _dropGlows =
            new List<(int?, GameObject)>();
        private readonly HashSet<int?> _validDropTargets = new HashSet<int?>();
        private RectTransform _canvasRoot;

        // Supply-drag insertion preview.
        private RectTransform _boardPanel;
        private readonly List<RectTransform> _standCells = new List<RectTransform>();
        private GameObject _spacer;
        private LayoutWidthTween _spacerTween;
        private int _supplyInsertIndex = -1;
        private bool _supplyDragActive;

        private readonly CardArt _art;
        private readonly CardPreview _preview;

        private Text _bannerText;
        private Text _opponentsText;
        private RectTransform _marketRow;
        private RectTransform _boardRow;
        private RectTransform _handHost;
        private RectTransform _lordHost;
        private RectTransform _dibsHost;
        private RectTransform _discardOverlay;
        private RectTransform _discardGrid;
        private Text _discardTitle;
        private GameObject _discardTakeButton;
        private Text _discardTakeLabel;
        private System.Action<int> _discardOnTake;
        private int? _discardSelectedId;
        private readonly Dictionary<int, GameObject> _discardSelectGlows =
            new Dictionary<int, GameObject>();

        // Hand fan scrolling (when the hand outgrows the center band).
        private readonly List<(RectTransform Frame, float BaseX)> _handFrames =
            new List<(RectTransform, float)>();
        private float _handScroll;
        private float _handMaxScroll;
        public RectTransform Root { get; private set; }
        public RectTransform ActionBar { get; private set; }

        public TableView(RectTransform canvasRoot, CardArt art, CardPreview preview)
        {
            _art = art;
            _preview = preview;
            _canvasRoot = canvasRoot;
            Build(canvasRoot);
        }

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);

        private void Build(RectTransform root)
        {
            Root = UiKit.CreatePanel(root, "Table", new Color(0, 0, 0, 0));
            UiKit.Anchor(Root, Vector2.zero, Vector2.one);
            Root.GetComponent<Image>().raycastTarget = false;

            // Left: opponents, below the full-width market shelf.
            var opponents = UiKit.CreatePanel(Root, "Opponents", UiKit.PanelColor);
            UiKit.Anchor(opponents, new Vector2(0, 0.24f), new Vector2(0.21f, 0.695f),
                new Vector2(6, 4), new Vector2(-3, -4));
            _opponentsText = UiKit.CreateText(opponents, "", 17);
            UiKit.Anchor((RectTransform)_opponentsText.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 8), new Vector2(-10, -8));

            // Top: one full-width shelf — Black Market row, stand supply, Bragging
            // Rights, and the Black Market discard pile.
            var market = UiKit.CreatePanel(Root, "Market", UiKit.PanelColor);
            UiKit.Anchor(market, new Vector2(0f, 0.70f), new Vector2(1f, 0.95f),
                new Vector2(6, 4), new Vector2(-6, -4));
            _marketRow = UiKit.CreateCardRow(market, "MarketRow");

            // Center band: turn/roll banner.
            var banner = UiKit.CreatePanel(Root, "Banner", new Color(0.16f, 0.20f, 0.28f, 0.95f));
            UiKit.Anchor(banner, new Vector2(0.21f, 0.60f), new Vector2(0.79f, 0.67f),
                new Vector2(3, 2), new Vector2(-3, -2));
            _bannerText = UiKit.CreateText(banner, "", 20, TextAnchor.MiddleCenter,
                new Color(1f, 0.92f, 0.55f));
            UiKit.Anchor((RectTransform)_bannerText.transform, Vector2.zero, Vector2.one);

            // Center: your board (turf + stands). Also a drop zone for supply stands.
            // Transparent — cards float on the table; the Image still catches drops.
            var board = UiKit.CreatePanel(Root, "Board", new Color(0, 0, 0, 0));
            UiKit.Anchor(board, new Vector2(0.21f, 0.22f), new Vector2(0.79f, 0.60f),
                new Vector2(3, 2), new Vector2(-3, -2));
            _boardPanel = board;
            board.gameObject.AddComponent<BoardDropZone>().SupplyDropped = HandleSupplyDrop;
            _boardRow = UiKit.CreateCardRow(board, "BoardRow");

            // Persistent actions: a floating button strip above the hand's peek band.
            var actions = UiKit.CreatePanel(Root, "Actions", new Color(0, 0, 0, 0));
            actions.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(actions, new Vector2(0.21f, 0.17f), new Vector2(0.79f, 0.22f),
                new Vector2(3, 1), new Vector2(-3, -1));
            var bar = new GameObject("ActionBarRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(actions, false);
            UiKit.Anchor((RectTransform)bar.transform, Vector2.zero, Vector2.one,
                new Vector2(8, 3), new Vector2(-8, -3));
            var barLayout = bar.GetComponent<HorizontalLayoutGroup>();
            barLayout.spacing = 8;
            barLayout.childForceExpandHeight = true;
            barLayout.childForceExpandWidth = false;
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            ActionBar = (RectTransform)bar.transform;

            // Bottom edge: the hand. Cards peek up from below the screen and rise on
            // hover, Dune: Imperium style. Built LAST so raised cards overlay the table.
            _handHost = UiKit.CreatePanel(Root, "Hand", new Color(0, 0, 0, 0));
            _handHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(_handHost, new Vector2(0.21f, 0f), new Vector2(0.79f, 0f));
            _handHost.pivot = new Vector2(0.5f, 0f);
            _handHost.sizeDelta = new Vector2(_handHost.sizeDelta.x, 300f);
            _handHost.anchoredPosition = Vector2.zero;

            // Bottom-right: your two secret Lemon Lords, same peek-and-rise treatment,
            // floating over the dark side zone.
            _lordHost = UiKit.CreatePanel(Root, "LemonLords", new Color(0, 0, 0, 0));
            _lordHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(_lordHost, new Vector2(0.79f, 0f), new Vector2(1f, 0f));
            _lordHost.pivot = new Vector2(0.5f, 0f);
            _lordHost.sizeDelta = new Vector2(_lordHost.sizeDelta.x, 300f);
            _lordHost.anchoredPosition = Vector2.zero;

            // Bottom-left: the First Dibs titles, peeking where the log used to live.
            _dibsHost = UiKit.CreatePanel(Root, "FirstDibs", new Color(0, 0, 0, 0));
            _dibsHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(_dibsHost, new Vector2(0f, 0f), new Vector2(0.21f, 0f));
            _dibsHost.pivot = new Vector2(0.5f, 0f);
            _dibsHost.sizeDelta = new Vector2(_dibsHost.sizeDelta.x, 300f);
            _dibsHost.anchoredPosition = Vector2.zero;

            BuildDiscardViewer();
        }

        /// <summary>
        /// Full-screen discard browser: dim backdrop, title, and a vertically scrolling
        /// grid of cards bounded to the center band. Click anywhere off a card to close.
        /// </summary>
        private void BuildDiscardViewer()
        {
            _discardOverlay = UiKit.CreatePanel(Root, "DiscardOverlay", new Color(0, 0, 0, 0.82f));
            UiKit.Anchor(_discardOverlay, Vector2.zero, Vector2.one);
            UiKit.AddClick(_discardOverlay.gameObject, CloseDiscardViewer);

            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(Text), typeof(Shadow));
            titleGo.transform.SetParent(_discardOverlay, false);
            UiKit.Anchor((RectTransform)titleGo.transform, new Vector2(0.1f, 0.89f), new Vector2(0.9f, 0.97f));
            _discardTitle = titleGo.GetComponent<Text>();
            _discardTitle.font = UiKit.DefaultFont;
            _discardTitle.fontSize = 34;
            _discardTitle.alignment = TextAnchor.MiddleCenter;
            _discardTitle.color = Color.white;
            _discardTitle.raycastTarget = false;
            var titleShadow = titleGo.GetComponent<Shadow>();
            titleShadow.effectColor = new Color(0, 0, 0, 0.85f);
            titleShadow.effectDistance = new Vector2(2.5f, -2.5f);

            // Transparent-but-raycastable scroll surface: the wheel works anywhere in
            // the band, and a click between cards still closes the viewer.
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(_discardOverlay, false);
            UiKit.Anchor((RectTransform)scrollGo.transform, new Vector2(0.21f, 0.02f), new Vector2(0.79f, 0.89f));
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            UiKit.AddClick(scrollGo, CloseDiscardViewer);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;

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
            // Reading size — same scale as the Lemon Lord picker at game setup.
            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(280, 392);
            grid.spacing = new Vector2(18, 18);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.childAlignment = TextAnchor.UpperCenter;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = (RectTransform)viewportGo.transform;
            scroll.content = content;
            _discardGrid = content;

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

            _discardOverlay.gameObject.SetActive(false);
        }

        /// <summary>Browse a discard pile (no selection, no button).</summary>
        private void OpenDiscardViewer(string title, IReadOnlyList<PlayerView.CardInfo> cards,
            bool blackMarket)
        {
            _discardTitle.text = cards.Count == 0 ? $"{title} — EMPTY" : $"{title} ({cards.Count})";
            FillDiscardGrid(cards, blackMarket, null);
            _discardOverlay.gameObject.SetActive(true);
        }

        /// <summary>
        /// Pick a card out of a discard pile (Reduce and Reuse, Reverse Engineer...):
        /// click a card to select it, then confirm with the TAKE button. Clicking off a
        /// card cancels — nothing has been submitted yet at that point.
        /// </summary>
        public void OpenDiscardPicker(string title, IReadOnlyList<PlayerView.CardInfo> cards,
            bool blackMarket, System.Func<PlayerView.CardInfo, string> nameOf,
            System.Action<int> onTake)
        {
            _discardTitle.text = title;
            _discardOnTake = onTake;
            FillDiscardGrid(cards, blackMarket, nameOf);
            _discardOverlay.gameObject.SetActive(true);
        }

        private void FillDiscardGrid(IReadOnlyList<PlayerView.CardInfo> cards, bool blackMarket,
            System.Func<PlayerView.CardInfo, string> nameOf)
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
            _discardTakeLabel.text = $"TAKE {name.ToUpperInvariant()}";
            _discardTakeButton.SetActive(true);
        }

        private void CloseDiscardViewer()
        {
            _discardOverlay.gameObject.SetActive(false);
            _discardOnTake = null;
            _discardSelectedId = null;
            _discardTakeButton.SetActive(false);
        }

        // ------------------------------------------------------------ render

        public void SetBanner(string text) => _bannerText.text = text;

        /// <summary>The on-table log is retired for now (First Dibs lives there); kept
        /// as a no-op until the log returns somewhere nicer.</summary>
        public void SetLog(IEnumerable<string> lines)
        {
        }

        public void Render(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            RenderMarket(view, db, groups);
            RenderBoard(view);
            RenderHand(view, groups);
            RenderLords(view);
            RenderFirstDibs(view);
            RenderSupply(view, db, groups);
            RenderOpponents(view, db);
        }

        private void RenderMarket(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            UiKit.Clear(_marketRow);
            // The discard piles form their own group: Lemon, then Black Market,
            // separated from the face-up market cards.
            BuildDiscardPile(view, blackMarket: false);
            BuildDiscardPile(view, blackMarket: true);
            AddRowGap();
            for (int i = 0; i < view.Market.Count; i++)
            {
                var card = view.Market[i];
                var texture = _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square);
                BuildMarketCell(i, texture);
            }
        }

        /// <summary>A market card: hover glows it; drag it onto your turf/stands to buy.</summary>
        private void BuildMarketCell(int marketIndex, Texture2D texture)
        {
            const float width = 154f;
            const float height = 216f;

            var cell = new GameObject("MarketCell", typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_marketRow, false);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width + 8;
            cellElement.preferredHeight = height + 4;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var liftGo = new GameObject("Lift", typeof(RectTransform));
            liftGo.transform.SetParent(cell.transform, false);
            var lift = (RectTransform)liftGo.transform;
            lift.anchorMin = new Vector2(0.5f, 0f);
            lift.anchorMax = new Vector2(0.5f, 0f);
            lift.pivot = new Vector2(0.5f, 0f);
            lift.sizeDelta = new Vector2(width, height);
            lift.anchoredPosition = Vector2.zero;

            // Glow margins split evenly above and below: centered on the card.
            var top = new Vector2(0.5f, 1f);
            var glowOuter = UiKit.CreateGlow(lift, top, top, new Vector2(0, 22),
                width + 44, height + 44, GlowOuterColor);
            var glowInner = UiKit.CreateGlow(lift, top, top, new Vector2(0, 10),
                width + 20, height + 20, GlowInnerColor);

            var image = UiKit.CreateCardImage(lift, texture, width, height);
            var frame = (RectTransform)image.transform.parent;
            frame.anchorMin = top;
            frame.anchorMax = top;
            frame.pivot = top;
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(width, height);

            _preview.Attach(image.gameObject, texture);
            var drag = image.gameObject.AddComponent<DragSource>();
            drag.Kind = DragKind.MarketCard;
            drag.MarketIndex = marketIndex;
            drag.Texture = texture;
            drag.CanvasRoot = _canvasRoot;
            drag.GlowInner = glowInner;
            drag.GlowOuter = glowOuter;
            drag.CanAct = () => CanBuyMarket?.Invoke(marketIndex) == true;
            drag.DragStarted = () => OnMarketDragStart?.Invoke(marketIndex);
            drag.DragEnded = () => OnMarketDragEnd?.Invoke();
        }

        private void RenderBoard(PlayerView view)
        {
            UiKit.Clear(_boardRow);
            _dropGlows.Clear();
            _standCells.Clear();
            _spacer = null; // destroyed with the row; recreated lazily
            _spacerTween = null;
            _supplyInsertIndex = -1;
            var me = view.Players[view.ViewerId];

            var turfTexture = _art.Turf(me.TurfPowerPourNumber);
            var turfCaption = "Pours " + string.Join(",", me.PourNumbers);
            var turfCell = AddCard(_boardRow, turfTexture, 150, 210, turfCaption, false, null);
            AddEquipList(turfCell, me.TurfEquipped);
            MakeDropTarget(turfCell, null);

            foreach (var stand in me.Stands)
            {
                string caption = $"[{string.Join(",", stand.SaleNumbers)}] ${stand.Earnings}";
                var cell = AddCard(_boardRow, _art.Stand(stand.StandTypeId, stand.Shape),
                    150, 210, caption, false, null);
                AddEquipList(cell, stand.Equipped);
                MakeDropTarget(cell, stand.InstanceId);
                _standCells.Add(cell);
            }
        }

        private void RenderHand(PlayerView view, MoveGroups groups)
        {
            UiKit.Clear(_handHost);
            _handFrames.Clear();
            int count = view.Hand.Count;
            if (count == 0)
            {
                return;
            }

            const float width = 190f;
            const float height = 266f;
            const float raisedY = 12f;         // fully visible, floating just off the edge
            const float peekPlayable = 180f;   // ~2/3 visible at rest
            const float peekIdle = 158f;       // unplayable cards sit a little lower

            float available = _handHost.rect.width;
            if (available < 10f)
            {
                available = 1100f; // first-frame fallback before canvas layout settles
            }
            // Constant Dune Imperium overlap; a big hand scrolls (wheel) instead of
            // compressing past readability or spilling out of the center band.
            float spacing = width * 0.72f;
            float span = width + spacing * (count - 1);
            _handMaxScroll = Mathf.Max(0f, span - available);
            _handScroll = Mathf.Clamp(_handScroll, 0f, _handMaxScroll);
            float startX = _handMaxScroll > 0f
                ? -available / 2f + width / 2f
                : -span / 2f + width / 2f;

            for (int i = 0; i < count; i++)
            {
                var card = view.Hand[i];
                int optionCount = groups?.HandMoves.TryGetValue(card.InstanceId, out var moves) == true
                    ? moves.Count
                    : 0;
                var texture = _art.Lemon(card.DefId);

                var image = UiKit.CreateCardImage(_handHost, texture, width, height);
                var frame = (RectTransform)image.transform.parent;
                frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
                frame.pivot = new Vector2(0.5f, 0f);
                frame.sizeDelta = new Vector2(width, height);
                float restY = (optionCount > 0 ? peekPlayable : peekIdle) - height;
                float baseX = startX + i * spacing;
                frame.anchoredPosition = new Vector2(baseX - _handScroll, restY);
                _handFrames.Add((frame, baseX));
                var motion = frame.gameObject.AddComponent<HandCardMotion>();
                motion.TargetY = restY;
                image.gameObject.AddComponent<HandScrollRelay>().Scrolled = ScrollHand;

                if (optionCount > 0)
                {
                    // Thin lemonade strip along the visible top edge: playable at a glance.
                    var strip = UiKit.CreatePanel(frame, "PlayableStrip", UiKit.ButtonColor);
                    UiKit.Anchor(strip, new Vector2(0f, 1f), new Vector2(1f, 1f),
                        new Vector2(0, -7f), new Vector2(0, 0));

                    // Revealed only when the card rises.
                    var badge = UiKit.CreatePanel(frame, "PlayBadge", UiKit.ButtonColor);
                    UiKit.Anchor(badge, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
                    badge.sizeDelta = new Vector2(width * 0.6f, 26f);
                    badge.anchoredPosition = new Vector2(0, 20f);
                    var badgeText = UiKit.CreateText(badge, $"PLAY ({optionCount})", 14,
                        TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
                    UiKit.Anchor((RectTransform)badgeText.transform, Vector2.zero, Vector2.one);

                    int captured = card.InstanceId;
                    UiKit.AddClick(image.gameObject, () => OnHandCard?.Invoke(captured));
                }

                int sibling = i;
                UiKit.AddHover(image.gameObject,
                    () =>
                    {
                        frame.SetAsLastSibling();
                        motion.TargetY = raisedY;
                    },
                    () =>
                    {
                        frame.SetSiblingIndex(sibling);
                        motion.TargetY = restY;
                    });
                _preview.Attach(image.gameObject, texture);
            }
        }

        /// <summary>Wheel over the hand: slide the fan sideways when it overflows.</summary>
        private void ScrollHand(float delta)
        {
            if (_handMaxScroll <= 0f)
            {
                return;
            }
            _handScroll = Mathf.Clamp(_handScroll - delta * 48f, 0f, _handMaxScroll);
            foreach (var (frame, baseX) in _handFrames)
            {
                if (frame != null)
                {
                    var position = frame.anchoredPosition;
                    position.x = baseX - _handScroll;
                    frame.anchoredPosition = position;
                }
            }
        }

        /// <summary>
        /// First Dibs titles: overlapped peek cards, bottom-left. Unclaimed titles from
        /// the row first, then claimed ones wearing their claimant's name chip.
        /// </summary>
        private void RenderFirstDibs(PlayerView view)
        {
            UiKit.Clear(_dibsHost);
            var entries = new List<(string TitleId, string ClaimedBy)>();
            foreach (string titleId in view.FirstDibsRow)
            {
                entries.Add((titleId, null));
            }
            foreach (var player in view.Players)
            {
                foreach (string claimed in player.FirstDibsClaimed)
                {
                    entries.Add((claimed, player.Name));
                }
            }
            if (entries.Count == 0)
            {
                return;
            }

            const float width = 190f;
            const float height = 266f;
            const float raisedY = 12f;
            const float peek = 158f;

            float available = _dibsHost.rect.width;
            if (available < 10f)
            {
                available = 390f;
            }
            // The zone is narrow: compress overlap as needed, hover-raise keeps it readable.
            float spacing = entries.Count > 1
                ? Mathf.Min(width * 0.72f, (available - width) / (entries.Count - 1))
                : 0f;
            float startX = -(width + spacing * (entries.Count - 1)) / 2f + width / 2f;

            for (int i = 0; i < entries.Count; i++)
            {
                var (titleId, claimedBy) = entries[i];
                var texture = _art.Title(titleId);

                var image = UiKit.CreateCardImage(_dibsHost, texture, width, height);
                var frame = (RectTransform)image.transform.parent;
                frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
                frame.pivot = new Vector2(0.5f, 0f);
                frame.sizeDelta = new Vector2(width, height);
                float restY = peek - height;
                frame.anchoredPosition = new Vector2(startX + i * spacing, restY);
                var motion = frame.gameObject.AddComponent<HandCardMotion>();
                motion.TargetY = restY;

                if (claimedBy != null)
                {
                    var chip = UiKit.CreatePanel(frame, "ClaimChip", UiKit.ButtonColor);
                    UiKit.Anchor(chip, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                    chip.sizeDelta = new Vector2(width - 40f, 24f);
                    chip.anchoredPosition = new Vector2(0, -24f);
                    var chipText = UiKit.CreateText(chip, claimedBy.ToUpperInvariant(), 13,
                        TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
                    UiKit.Anchor((RectTransform)chipText.transform, Vector2.zero, Vector2.one);
                }

                int sibling = i;
                UiKit.AddHover(image.gameObject,
                    () =>
                    {
                        frame.SetAsLastSibling();
                        motion.TargetY = raisedY;
                    },
                    () =>
                    {
                        frame.SetSiblingIndex(sibling);
                        motion.TargetY = restY;
                    });
                _preview.Attach(image.gameObject, texture);
            }
        }

        /// <summary>Your two secret Lemon Lord titles: overlapped peek cards, bottom-right.</summary>
        private void RenderLords(PlayerView view)
        {
            UiKit.Clear(_lordHost);
            int count = view.LemonLordStatus.Count;
            if (count == 0)
            {
                return;
            }

            const float width = 190f;
            const float height = 266f;
            const float raisedY = 12f;
            const float peek = 158f;
            float spacing = width * 0.72f;
            float startX = -(width + spacing * (count - 1)) / 2f + width / 2f;

            for (int i = 0; i < count; i++)
            {
                var lord = view.LemonLordStatus[i];
                var texture = _art.Title(lord.TitleId);

                var image = UiKit.CreateCardImage(_lordHost, texture, width, height);
                var frame = (RectTransform)image.transform.parent;
                frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
                frame.pivot = new Vector2(0.5f, 0f);
                frame.sizeDelta = new Vector2(width, height);
                float restY = peek - height;
                frame.anchoredPosition = new Vector2(startX + i * spacing, restY);
                var motion = frame.gameObject.AddComponent<HandCardMotion>();
                motion.TargetY = restY;

                if (lord.Met)
                {
                    var chip = UiKit.CreatePanel(frame, "MetChip", UiKit.ButtonColor);
                    UiKit.Anchor(chip, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                    chip.sizeDelta = new Vector2(74f, 24f);
                    chip.anchoredPosition = new Vector2(0, -24f);
                    var chipText = UiKit.CreateText(chip, "MET!", 14,
                        TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
                    UiKit.Anchor((RectTransform)chipText.transform, Vector2.zero, Vector2.one);
                }

                int sibling = i;
                UiKit.AddHover(image.gameObject,
                    () =>
                    {
                        frame.SetAsLastSibling();
                        motion.TargetY = raisedY;
                    },
                    () =>
                    {
                        frame.SetSiblingIndex(sibling);
                        motion.TargetY = restY;
                    });
                _preview.Attach(image.gameObject, texture);
            }
        }

        /// <summary>Appends to the market row — call after RenderMarket (which clears it).</summary>
        private void RenderSupply(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            // Equal stretchy gaps split the shelf: market | stands | Bragging Rights.
            AddRowGap();

            foreach (var type in db.StandTypes)
            {
                var texture = view.SupplyTopShapes.TryGetValue(type.Id, out var shape)
                    ? _art.Stand(type.Id, shape)
                    : _art.Stand(type.Id);
                BuildSupplyCell(type.Id, texture);
            }

            if (view.NextBraggingRightsPrice is int braggingPrice)
            {
                AddRowGap();
                // Index derived from price: $16 base, +$2 per sale.
                int sold = (braggingPrice - 16) / 2;
                AddCard(_marketRow, _art.BraggingRights(sold), 154, 216, "", false, null);
            }
        }

        /// <summary>A discard pile: card back, count on hover, click to browse.</summary>
        private void BuildDiscardPile(PlayerView view, bool blackMarket)
        {
            var back = _art.Back(blackMarket ? "blackMarket" : "lemon");
            var image = UiKit.CreateCardImage(_marketRow, back, 154, 216);
            var frame = (RectTransform)image.transform.parent;

            var discards = blackMarket ? view.BlackMarketDiscard : view.LemonDiscard;
            int count = discards.Count;
            var chip = UiKit.CreatePanel(frame, "CountChip", new Color(0, 0, 0, 0.78f));
            UiKit.Anchor(chip, new Vector2(0, 0.5f), new Vector2(1, 0.5f));
            chip.sizeDelta = new Vector2(0, 36);
            // Click-transparent, or the chip steals the raycast from the card under the
            // cursor and hover enters/exits in an endless flicker loop.
            chip.GetComponent<Image>().raycastTarget = false;
            var chipText = UiKit.CreateText(chip,
                count == 1 ? "1 DISCARD" : $"{count} DISCARDS", 16,
                TextAnchor.MiddleCenter, Color.white);
            chipText.raycastTarget = false;
            UiKit.Anchor((RectTransform)chipText.transform, Vector2.zero, Vector2.one);
            chip.gameObject.SetActive(false);

            UiKit.AddHover(image.gameObject,
                () => chip.gameObject.SetActive(true),
                () => chip.gameObject.SetActive(false));
            UiKit.AddClick(image.gameObject, () => OpenDiscardViewer(
                blackMarket ? "BLACK MARKET DISCARDS" : "LEMON DISCARDS",
                discards, blackMarket));
        }

        /// <summary>A flexible spacer; multiple gaps in the row share leftover width equally.</summary>
        private void AddRowGap()
        {
            var gap = new GameObject("RowGap", typeof(RectTransform), typeof(LayoutElement));
            gap.transform.SetParent(_marketRow, false);
            var gapElement = gap.GetComponent<LayoutElement>();
            gapElement.preferredWidth = 24;
            gapElement.flexibleWidth = 1;
        }

        private void RenderOpponents(PlayerView view, CardDatabase db)
        {
            var text = new StringBuilder();
            foreach (var p in view.Players)
            {
                if (p.PlayerId == view.ViewerId)
                {
                    continue;
                }
                text.Append(p.PlayerId == view.ActivePlayer ? "> " : "  ")
                    .Append($"{p.Name}  ${p.Money}  {p.HandCount} cards  {p.InGameVictoryPoints} VP");
                if (view.WhiniestBabyHolder == p.PlayerId)
                {
                    text.Append("  BABY");
                }
                if (view.SpoiledRottenHolder == p.PlayerId)
                {
                    text.Append("  SPOILED");
                }
                if (p.TantrumCount > 0)
                {
                    text.Append($"  {p.TantrumCount}xTANTRUM");
                }
                text.AppendLine();
                text.Append("   Turf ").Append(string.Join(",", p.PourNumbers));
                foreach (var equip in p.TurfEquipped)
                {
                    text.Append(" | ").Append(db.BlackMarket(equip.DefId).Name);
                }
                text.AppendLine();
                foreach (var stand in p.Stands)
                {
                    text.Append($"   {db.StandType(stand.StandTypeId).Name} " +
                                $"[{string.Join(",", stand.SaleNumbers)}] ${stand.Earnings}");
                    foreach (var equip in stand.Equipped)
                    {
                        text.Append(" | ").Append(db.BlackMarket(equip.DefId).Name);
                    }
                    text.AppendLine();
                }
                text.AppendLine();
            }
            _opponentsText.text = text.ToString();
        }

        // ------------------------------------------- supply drag insertion

        /// <summary>A supply pile: hover glows it; drag it into your board row to buy a stand.</summary>
        private void BuildSupplyCell(string standTypeId, Texture2D texture)
        {
            const float width = 154f;
            const float height = 216f;

            var cell = new GameObject("SupplyCell", typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_marketRow, false);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width + 8;
            cellElement.preferredHeight = height + 4;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var liftGo = new GameObject("Lift", typeof(RectTransform));
            liftGo.transform.SetParent(cell.transform, false);
            var lift = (RectTransform)liftGo.transform;
            lift.anchorMin = new Vector2(0.5f, 0f);
            lift.anchorMax = new Vector2(0.5f, 0f);
            lift.pivot = new Vector2(0.5f, 0f);
            lift.sizeDelta = new Vector2(width, height);
            lift.anchoredPosition = Vector2.zero;

            // Glow margins split evenly above and below: centered on the card.
            var top = new Vector2(0.5f, 1f);
            var glowOuter = UiKit.CreateGlow(lift, top, top, new Vector2(0, 22),
                width + 44, height + 44, GlowOuterColor);
            var glowInner = UiKit.CreateGlow(lift, top, top, new Vector2(0, 10),
                width + 20, height + 20, GlowInnerColor);

            var image = UiKit.CreateCardImage(lift, texture, width, height);
            var frame = (RectTransform)image.transform.parent;
            frame.anchorMin = top;
            frame.anchorMax = top;
            frame.pivot = top;
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(width, height);

            _preview.Attach(image.gameObject, texture);
            var drag = image.gameObject.AddComponent<DragSource>();
            drag.Kind = DragKind.SupplyStand;
            drag.SupplyTypeId = standTypeId;
            drag.Texture = texture;
            drag.CanvasRoot = _canvasRoot;
            drag.GlowInner = glowInner;
            drag.GlowOuter = glowOuter;
            drag.CanAct = () => CanBuySupply?.Invoke(standTypeId) == true;
            drag.DragStarted = () =>
            {
                _supplyDragActive = true;
                _preview.Hide();
            };
            drag.DragEnded = () =>
            {
                _supplyDragActive = false;
                HideInsertPreview();
            };
        }

        /// <summary>Called every frame by the app while a supply stand is being dragged.</summary>
        public void TickSupplyDrag(Vector2 screenPosition)
        {
            if (!_supplyDragActive)
            {
                return;
            }
            if (!RectTransformUtility.RectangleContainsScreenPoint(_boardPanel, screenPosition))
            {
                HideInsertPreview();
                return;
            }
            int index = ComputeInsertIndex(screenPosition);
            if (index != _supplyInsertIndex)
            {
                _supplyInsertIndex = index;
                ShowInsertPreview(index);
            }
        }

        private int ComputeInsertIndex(Vector2 screenPosition)
        {
            for (int i = 0; i < _standCells.Count; i++)
            {
                var cell = _standCells[i];
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    cell, screenPosition, null, out var local);
                float fraction = (local.x - cell.rect.xMin) / Mathf.Max(1f, cell.rect.width);
                if (fraction < 0f)
                {
                    return i;
                }
                if (fraction <= 1f)
                {
                    if (fraction < 0.4f)
                    {
                        return i;
                    }
                    if (fraction > 0.6f)
                    {
                        return i + 1;
                    }
                    return _supplyInsertIndex >= 0 ? _supplyInsertIndex : i;
                }
            }
            return _standCells.Count;
        }

        private void ShowInsertPreview(int index)
        {
            CollapseActiveSpacer();

            _spacer = new GameObject("InsertSpacer", typeof(RectTransform),
                typeof(LayoutElement), typeof(LayoutWidthTween));
            _spacer.transform.SetParent(_boardRow, false);
            var element = _spacer.GetComponent<LayoutElement>();
            element.preferredWidth = 0;
            element.preferredHeight = 10;
            element.flexibleWidth = 0;

            int sibling = index < _standCells.Count
                ? _standCells[index].GetSiblingIndex()
                : _boardRow.childCount - 1;
            _spacer.transform.SetSiblingIndex(sibling);

            _spacerTween = _spacer.GetComponent<LayoutWidthTween>();
            _spacerTween.SetTarget(130f);
        }

        private void CollapseActiveSpacer()
        {
            if (_spacerTween != null)
            {
                _spacerTween.SetTarget(0f, destroyAtZero: true);
                _spacer = null;
                _spacerTween = null;
            }
        }

        private void HideInsertPreview()
        {
            _supplyInsertIndex = -1;
            CollapseActiveSpacer();
        }

        private void HandleSupplyDrop(string standTypeId)
        {
            int index = _supplyInsertIndex >= 0 ? _supplyInsertIndex : _standCells.Count;
            OnSupplyDrop?.Invoke(standTypeId, index);
        }

        // --------------------------------------------------------- drop zones

        private void MakeDropTarget(RectTransform cell, int? standInstanceId)
        {
            var target = cell.gameObject.AddComponent<DropTarget>();
            target.StandInstanceId = standInstanceId;
            target.Dropped = (marketIndex, standId) => OnMarketDrop?.Invoke(marketIndex, standId);
            target.SupplyDropped = HandleSupplyDrop;
            target.HoverChanged = OnDropTargetHover;

            var top = new Vector2(0.5f, 1f);
            var wide = UiKit.CreateGlow(cell, top, top, new Vector2(0, 30),
                150 + 60, 210 + 60, DropGlowWide);
            wide.transform.SetAsFirstSibling();
            var hot = UiKit.CreateGlow(cell, top, top, new Vector2(0, 14),
                150 + 28, 210 + 28, DropGlowHot);
            hot.transform.SetSiblingIndex(1);

            _dropGlows.Add((standInstanceId, wide));
            _dropGlows.Add((standInstanceId, hot));
        }

        public void SetValidDropTargets(ISet<int?> validTargets)
        {
            _validDropTargets.Clear();
            foreach (var id in validTargets)
            {
                _validDropTargets.Add(id);
            }
        }

        private void OnDropTargetHover(int? standId, bool hovering)
        {
            bool show = hovering && _validDropTargets.Contains(standId);
            foreach (var (id, glow) in _dropGlows)
            {
                if (Equals(id, standId))
                {
                    glow.SetActive(show);
                }
            }
        }

        public void ClearDropHighlights()
        {
            _validDropTargets.Clear();
            foreach (var (_, glow) in _dropGlows)
            {
                glow.SetActive(false);
            }
        }

        // ----------------------------------------------------------- helpers

        private RectTransform AddCard(RectTransform parent, Texture2D texture,
            float width, float height, string caption, bool clickable, System.Action onClick)
        {
            var cell = new GameObject("Cell", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(LayoutElement));
            cell.transform.SetParent(parent, false);
            var layout = cell.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 2;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var image = UiKit.CreateCardImage((RectTransform)cell.transform, texture, width, height);
            _preview.Attach(image.gameObject, texture);
            if (clickable && onClick != null)
            {
                UiKit.AddClick(image.gameObject, () => onClick());
            }

            if (!string.IsNullOrEmpty(caption) || clickable)
            {
                UiKit.CreateBadge((RectTransform)cell.transform, caption, 13,
                    clickable ? UiKit.ButtonColor : new Color(0, 0, 0, 0.55f))
                    .color = clickable ? UiKit.ButtonTextColor : Color.white;
            }
            return (RectTransform)cell.transform;
        }

        private void AddEquipList(RectTransform cell, List<PlayerView.CardInfo> equipped)
        {
            foreach (var equip in equipped)
            {
                var texture = _art.BlackMarket(equip.DefId, equip.Shape ?? Shape.Square);
                var badge = UiKit.CreateBadge(cell, PrettyName(equip.DefId), 12,
                    new Color(0.20f, 0.32f, 0.24f, 0.9f));
                _preview.Attach(badge.transform.parent.gameObject, texture);
            }
        }

        private static string PrettyName(string defId)
        {
            var parts = defId.Split('-');
            return string.Join(" ", parts.Select(p =>
                p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1) : p));
        }
    }

    /// <summary>
    /// Forwards mouse-wheel input on a hand card to the fan scroller. A dedicated
    /// component (not PointerRelay) so ordinary hover/click objects elsewhere — e.g.
    /// inside prompt scroll lists — never swallow wheel events.
    /// </summary>
    public sealed class HandScrollRelay : MonoBehaviour,
        UnityEngine.EventSystems.IScrollHandler
    {
        public System.Action<float> Scrolled;

        public void OnScroll(UnityEngine.EventSystems.PointerEventData eventData)
        {
            Scrolled?.Invoke(eventData.scrollDelta.y);
        }
    }

    /// <summary>
    /// Eases a hand card toward its target height — the rise-on-hover, sink-on-exit
    /// motion. Exponential smoothing: fast start, soft landing.
    /// </summary>
    public sealed class HandCardMotion : MonoBehaviour
    {
        public float TargetY;

        private RectTransform _rect;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        private void Update()
        {
            var position = _rect.anchoredPosition;
            position.y = Mathf.Lerp(position.y, TargetY, 1f - Mathf.Exp(-14f * Time.deltaTime));
            _rect.anchoredPosition = position;
        }
    }
}
