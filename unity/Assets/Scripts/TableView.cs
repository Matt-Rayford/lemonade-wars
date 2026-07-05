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
        private Text _sideText;
        private Text _logText;
        private RectTransform _marketRow;
        private RectTransform _supplyRow;
        private RectTransform _boardRow;
        private RectTransform _handHost;
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

            // Left: opponents.
            var opponents = UiKit.CreatePanel(Root, "Opponents", UiKit.PanelColor);
            UiKit.Anchor(opponents, new Vector2(0, 0.30f), new Vector2(0.21f, 0.95f),
                new Vector2(6, 4), new Vector2(-3, -4));
            _opponentsText = UiKit.CreateText(opponents, "", 17);
            UiKit.Anchor((RectTransform)_opponentsText.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 8), new Vector2(-10, -8));

            // Left-bottom: event log.
            var log = UiKit.CreatePanel(Root, "Log", UiKit.PanelColor);
            UiKit.Anchor(log, new Vector2(0, 0), new Vector2(0.21f, 0.30f),
                new Vector2(6, 6), new Vector2(-3, -3));
            _logText = UiKit.CreateText(log, "", 14, TextAnchor.LowerLeft, new Color(0.8f, 0.9f, 0.8f));
            UiKit.Anchor((RectTransform)_logText.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 6), new Vector2(-10, -6));

            // Center-top: the Black Market row.
            var market = UiKit.CreatePanel(Root, "Market", UiKit.PanelColor);
            UiKit.Anchor(market, new Vector2(0.21f, 0.70f), new Vector2(0.79f, 0.95f),
                new Vector2(3, 4), new Vector2(-3, -4));
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

            // Right: supply, bragging rights, first dibs.
            var side = UiKit.CreatePanel(Root, "Side", UiKit.PanelColor);
            UiKit.Anchor(side, new Vector2(0.79f, 0), new Vector2(1, 0.95f),
                new Vector2(3, 6), new Vector2(-6, -4));
            var supplyHost = UiKit.CreatePanel(side, "SupplyHost", new Color(0, 0, 0, 0.15f));
            UiKit.Anchor(supplyHost, new Vector2(0, 0.60f), new Vector2(1, 1),
                new Vector2(4, 2), new Vector2(-4, -4));
            _supplyRow = UiKit.CreateCardRow(supplyHost, "SupplyRow");
            _sideText = UiKit.CreateText(side, "", 16);
            var sideTextRt = (RectTransform)_sideText.transform;
            UiKit.Anchor(sideTextRt, new Vector2(0, 0), new Vector2(1, 0.60f),
                new Vector2(10, 8), new Vector2(-10, -4));

            // Bottom edge: the hand. Cards peek up from below the screen and rise on
            // hover, Dune: Imperium style. Built LAST so raised cards overlay the table.
            _handHost = UiKit.CreatePanel(Root, "Hand", new Color(0, 0, 0, 0));
            _handHost.GetComponent<Image>().raycastTarget = false;
            UiKit.Anchor(_handHost, new Vector2(0.21f, 0f), new Vector2(0.79f, 0f));
            _handHost.pivot = new Vector2(0.5f, 0f);
            _handHost.sizeDelta = new Vector2(_handHost.sizeDelta.x, 300f);
            _handHost.anchoredPosition = Vector2.zero;
        }

        // ------------------------------------------------------------ render

        public void SetBanner(string text) => _bannerText.text = text;

        public void SetLog(IEnumerable<string> lines) => _logText.text = string.Join("\n", lines);

        public void Render(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            RenderMarket(view, db, groups);
            RenderBoard(view);
            RenderHand(view, groups);
            RenderSupply(view, db, groups);
            RenderOpponents(view, db);
            RenderSide(view, db);
        }

        private void RenderMarket(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            UiKit.Clear(_marketRow);
            for (int i = 0; i < view.Market.Count; i++)
            {
                var card = view.Market[i];
                var texture = _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square);
                int price = i < view.MarketPrices.Count
                    ? view.MarketPrices[i]
                    : db.BlackMarket(card.DefId).Cost;
                bool buyable = groups?.MarketMoves.ContainsKey(i) == true;
                BuildMarketCell(i, texture, price, buyable);
            }
        }

        /// <summary>A market card: hover lifts it with a glow; drag it onto your turf/stands to buy.</summary>
        private void BuildMarketCell(int marketIndex, Texture2D texture, int price, bool buyable)
        {
            const float width = 140f;
            const float height = 196f;
            const float badgeHeight = 22f;
            const float liftRoom = 14f;

            var cell = new GameObject("MarketCell", typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_marketRow, false);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width + 8;
            cellElement.preferredHeight = height + badgeHeight + liftRoom + 4;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var liftGo = new GameObject("Lift", typeof(RectTransform));
            liftGo.transform.SetParent(cell.transform, false);
            var lift = (RectTransform)liftGo.transform;
            lift.anchorMin = new Vector2(0.5f, 0f);
            lift.anchorMax = new Vector2(0.5f, 0f);
            lift.pivot = new Vector2(0.5f, 0f);
            lift.sizeDelta = new Vector2(width, height + badgeHeight + 2);
            lift.anchoredPosition = Vector2.zero;

            var top = new Vector2(0.5f, 1f);
            var glowOuter = UiKit.CreateGlow(lift, top, top, new Vector2(0, 14),
                width + 44, height + 44, GlowOuterColor);
            var glowInner = UiKit.CreateGlow(lift, top, top, new Vector2(0, 7),
                width + 20, height + 20, GlowInnerColor);

            var image = UiKit.CreateCardImage(lift, texture, width, height);
            var frame = (RectTransform)image.transform.parent;
            frame.anchorMin = top;
            frame.anchorMax = top;
            frame.pivot = top;
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(width, height);

            var badgeGo = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badgeGo.transform.SetParent(lift, false);
            var badgeRect = (RectTransform)badgeGo.transform;
            badgeRect.anchorMin = new Vector2(0, 0);
            badgeRect.anchorMax = new Vector2(1, 0);
            badgeRect.pivot = new Vector2(0.5f, 0);
            badgeRect.sizeDelta = new Vector2(0, badgeHeight);
            badgeGo.GetComponent<Image>().color =
                buyable ? UiKit.ButtonColor : new Color(0, 0, 0, 0.55f);
            var badgeText = UiKit.CreateText(badgeGo.transform,
                buyable ? $"${price} · drag" : $"${price}", 13, TextAnchor.MiddleCenter,
                buyable ? UiKit.ButtonTextColor : Color.white);
            UiKit.Anchor((RectTransform)badgeText.transform, Vector2.zero, Vector2.one);

            _preview.Attach(image.gameObject, texture);
            var drag = image.gameObject.AddComponent<DragSource>();
            drag.Kind = DragKind.MarketCard;
            drag.MarketIndex = marketIndex;
            drag.Texture = texture;
            drag.CanvasRoot = _canvasRoot;
            drag.LiftTarget = lift;
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
            // Always overlapped, Dune Imperium style; compresses further when the hand grows.
            float spacing = count > 1
                ? Mathf.Min(width * 0.72f, (available - width) / (count - 1))
                : 0f;
            float startX = -(width + spacing * (count - 1)) / 2f + width / 2f;

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
                frame.anchoredPosition = new Vector2(startX + i * spacing, restY);
                var motion = frame.gameObject.AddComponent<HandCardMotion>();
                motion.TargetY = restY;

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

        private void RenderSupply(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            UiKit.Clear(_supplyRow);
            foreach (var type in db.StandTypes)
            {
                view.StandSupplyCounts.TryGetValue(type.Id, out int stock);
                bool buyable = groups?.SupplyMoves.ContainsKey(type.Id) == true;
                int price = view.SupplyPrices.TryGetValue(type.Id, out int p) ? p : type.BaseCost;
                string caption = $"${price} x{stock}" + (buyable ? " · drag" : "");
                var texture = view.SupplyTopShapes.TryGetValue(type.Id, out var shape)
                    ? _art.Stand(type.Id, shape)
                    : _art.Stand(type.Id);
                BuildSupplyCell(type.Id, texture, caption, buyable);
            }

            if (view.NextBraggingRightsPrice is int braggingPrice)
            {
                // Index derived from price: $16 base, +$2 per sale.
                int sold = (braggingPrice - 16) / 2;
                AddCard(_supplyRow, _art.BraggingRights(sold), 92, 129, $"${braggingPrice}", false, null);
            }
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

        private void RenderSide(PlayerView view, CardDatabase db)
        {
            var text = new StringBuilder();
            text.AppendLine($"Lemon deck: {view.LemonDeckCount}   discard: {view.LemonDiscard.Count}");
            text.AppendLine($"BM deck: {view.BlackMarketDeckCount}   discard: {view.BlackMarketDiscard.Count}");
            text.AppendLine();
            text.AppendLine("FIRST DIBS:");
            foreach (string titleId in view.FirstDibsRow)
            {
                var title = db.Title(titleId);
                text.AppendLine($"  {title.Name} — {title.Condition}");
            }
            foreach (var p in view.Players)
            {
                foreach (string claimed in p.FirstDibsClaimed)
                {
                    text.AppendLine($"  [{p.Name}] {db.Title(claimed).Name}");
                }
            }
            if (view.LemonLordStatus.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("YOUR LEMON LORDS (secret):");
                foreach (var lord in view.LemonLordStatus)
                {
                    var title = db.Title(lord.TitleId);
                    text.AppendLine($"  {title.Name}{(lord.Met ? " (MET!)" : "")} — {title.Condition}");
                }
            }
            _sideText.text = text.ToString();
        }

        // ------------------------------------------- supply drag insertion

        /// <summary>A supply pile: hover lifts it; drag it into your board row to buy a stand.</summary>
        private void BuildSupplyCell(string standTypeId, Texture2D texture, string caption, bool buyable)
        {
            const float width = 92f;
            const float height = 129f;
            const float badgeHeight = 20f;
            const float liftRoom = 12f;

            var cell = new GameObject("SupplyCell", typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_supplyRow, false);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width + 6;
            cellElement.preferredHeight = height + badgeHeight + liftRoom + 4;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var liftGo = new GameObject("Lift", typeof(RectTransform));
            liftGo.transform.SetParent(cell.transform, false);
            var lift = (RectTransform)liftGo.transform;
            lift.anchorMin = new Vector2(0.5f, 0f);
            lift.anchorMax = new Vector2(0.5f, 0f);
            lift.pivot = new Vector2(0.5f, 0f);
            lift.sizeDelta = new Vector2(width, height + badgeHeight + 2);
            lift.anchoredPosition = Vector2.zero;

            var top = new Vector2(0.5f, 1f);
            var glowOuter = UiKit.CreateGlow(lift, top, top, new Vector2(0, 10),
                width + 34, height + 34, GlowOuterColor);
            var glowInner = UiKit.CreateGlow(lift, top, top, new Vector2(0, 5),
                width + 16, height + 16, GlowInnerColor);

            var image = UiKit.CreateCardImage(lift, texture, width, height);
            var frame = (RectTransform)image.transform.parent;
            frame.anchorMin = top;
            frame.anchorMax = top;
            frame.pivot = top;
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(width, height);

            var badgeGo = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badgeGo.transform.SetParent(lift, false);
            var badgeRect = (RectTransform)badgeGo.transform;
            badgeRect.anchorMin = new Vector2(0, 0);
            badgeRect.anchorMax = new Vector2(1, 0);
            badgeRect.pivot = new Vector2(0.5f, 0);
            badgeRect.sizeDelta = new Vector2(0, badgeHeight);
            badgeGo.GetComponent<Image>().color =
                buyable ? UiKit.ButtonColor : new Color(0, 0, 0, 0.55f);
            var badgeText = UiKit.CreateText(badgeGo.transform, caption, 12,
                TextAnchor.MiddleCenter, buyable ? UiKit.ButtonTextColor : Color.white);
            UiKit.Anchor((RectTransform)badgeText.transform, Vector2.zero, Vector2.one);

            _preview.Attach(image.gameObject, texture);
            var drag = image.gameObject.AddComponent<DragSource>();
            drag.Kind = DragKind.SupplyStand;
            drag.SupplyTypeId = standTypeId;
            drag.Texture = texture;
            drag.CanvasRoot = _canvasRoot;
            drag.LiftTarget = lift;
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
            var wide = UiKit.CreateGlow(cell, top, top, new Vector2(0, 24),
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
