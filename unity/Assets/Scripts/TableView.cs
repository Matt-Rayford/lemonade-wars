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
    /// The game table: market, supply piles, opponents, your board, and your hand — all
    /// rendered from engine state with real card art. Interactive elements (cards with
    /// legal moves) get a yellow option badge and click through to the app's handlers.
    /// </summary>
    public sealed class TableView
    {
        public System.Action<int> OnHandCard;      // lemon instance id
        public System.Action<string> OnSupplyPile; // stand type id

        // Market drag & drop.
        public System.Func<int, bool> CanBuyMarket;         // market index -> any legal buy?
        public System.Action<int> OnMarketDragStart;        // market index
        public System.Action OnMarketDragEnd;
        public System.Action<int, int?> OnMarketDrop;       // market index, stand id (null = turf)

        private static readonly Color GlowInnerColor = new Color(1f, 0.97f, 0.88f, 1f);
        private static readonly Color GlowOuterColor = new Color(1f, 0.96f, 0.82f, 0.80f);
        /// <summary>Drop zones glow lemonade-yellow while a card is being dragged.</summary>
        private static readonly Color DropGlowHot = new Color(1f, 0.93f, 0.45f, 1f);
        private static readonly Color DropGlowWide = new Color(1f, 0.82f, 0.10f, 1f);
        private readonly List<(int? StandId, GameObject Glow)> _dropGlows =
            new List<(int?, GameObject)>();
        private readonly HashSet<int?> _validDropTargets = new HashSet<int?>();
        private RectTransform _canvasRoot;

        private readonly CardArt _art;
        private readonly CardPreview _preview;

        private Text _bannerText;
        private Text _opponentsText;
        private Text _sideText;
        private Text _logText;
        private RectTransform _marketRow;
        private RectTransform _supplyRow;
        private RectTransform _boardRow;
        private RectTransform _handRow;
        public RectTransform ActionBar { get; private set; }

        public TableView(RectTransform canvasRoot, CardArt art, CardPreview preview)
        {
            _art = art;
            _preview = preview;
            _canvasRoot = canvasRoot;
            Build(canvasRoot);
        }

        private void Build(RectTransform root)
        {
            // Left: opponents.
            var opponents = UiKit.CreatePanel(root, "Opponents", UiKit.PanelColor);
            UiKit.Anchor(opponents, new Vector2(0, 0.30f), new Vector2(0.21f, 0.95f),
                new Vector2(6, 4), new Vector2(-3, -4));
            _opponentsText = UiKit.CreateText(opponents, "", 17);
            UiKit.Anchor((RectTransform)_opponentsText.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 8), new Vector2(-10, -8));

            // Left-bottom: event log.
            var log = UiKit.CreatePanel(root, "Log", UiKit.PanelColor);
            UiKit.Anchor(log, new Vector2(0, 0), new Vector2(0.21f, 0.30f),
                new Vector2(6, 6), new Vector2(-3, -3));
            _logText = UiKit.CreateText(log, "", 14, TextAnchor.LowerLeft, new Color(0.8f, 0.9f, 0.8f));
            UiKit.Anchor((RectTransform)_logText.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 6), new Vector2(-10, -6));

            // Center-top: the Black Market row.
            var market = UiKit.CreatePanel(root, "Market", UiKit.PanelColor);
            UiKit.Anchor(market, new Vector2(0.21f, 0.70f), new Vector2(0.79f, 0.95f),
                new Vector2(3, 4), new Vector2(-3, -4));
            _marketRow = UiKit.CreateCardRow(market, "MarketRow");

            // Center band: turn/roll banner.
            var banner = UiKit.CreatePanel(root, "Banner", new Color(0.16f, 0.20f, 0.28f, 0.95f));
            UiKit.Anchor(banner, new Vector2(0.21f, 0.63f), new Vector2(0.79f, 0.70f),
                new Vector2(3, 2), new Vector2(-3, -2));
            _bannerText = UiKit.CreateText(banner, "", 20, TextAnchor.MiddleCenter,
                new Color(1f, 0.92f, 0.55f));
            UiKit.Anchor((RectTransform)_bannerText.transform, Vector2.zero, Vector2.one);

            // Center: your board (turf + stands).
            var board = UiKit.CreatePanel(root, "Board", UiKit.PanelColor);
            UiKit.Anchor(board, new Vector2(0.21f, 0.315f), new Vector2(0.79f, 0.63f),
                new Vector2(3, 2), new Vector2(-3, -2));
            _boardRow = UiKit.CreateCardRow(board, "BoardRow");

            // Bottom-center: your hand.
            var hand = UiKit.CreatePanel(root, "Hand", UiKit.PanelColor);
            UiKit.Anchor(hand, new Vector2(0.21f, 0), new Vector2(0.79f, 0.27f),
                new Vector2(3, 6), new Vector2(-3, -2));
            _handRow = UiKit.CreateScrollRow(hand);

            // Bottom-center strip: persistent actions.
            var actions = UiKit.CreatePanel(root, "Actions", new Color(0.09f, 0.10f, 0.13f, 0.95f));
            UiKit.Anchor(actions, new Vector2(0.21f, 0.27f), new Vector2(0.79f, 0.315f),
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
            var side = UiKit.CreatePanel(root, "Side", UiKit.PanelColor);
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
        }

        // ------------------------------------------------------------ render

        public void SetBanner(string text) => _bannerText.text = text;

        public void SetLog(IEnumerable<string> lines) => _logText.text = string.Join("\n", lines);

        public void Render(Game game, int humanSeat, MoveGroups groups)
        {
            RenderMarket(game, humanSeat, groups);
            RenderBoard(game, humanSeat);
            RenderHand(game, humanSeat, groups);
            RenderSupply(game, humanSeat, groups);
            RenderOpponents(game, humanSeat);
            RenderSide(game);
        }

        private void RenderMarket(Game game, int humanSeat, MoveGroups groups)
        {
            UiKit.Clear(_marketRow);
            var s = game.State;
            for (int i = 0; i < s.Market.Count; i++)
            {
                var instance = s.BlackMarketInstances[s.Market[i]];
                var def = game.Db.BlackMarket(instance.DefId);
                var texture = _art.BlackMarket(instance.DefId, instance.Shape);
                bool buyable = groups?.MarketMoves.ContainsKey(i) == true;
                BuildMarketCell(i, texture, game.BlackMarketPrice(humanSeat, def), buyable);
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

            // Price badge pinned under the card.
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
            drag.MarketIndex = marketIndex;
            drag.Texture = texture;
            drag.CanvasRoot = _canvasRoot;
            drag.LiftTarget = lift;
            drag.GlowInner = glowInner;
            drag.GlowOuter = glowOuter;
            drag.CanAct = i => CanBuyMarket?.Invoke(i) == true;
            drag.DragStarted = i => OnMarketDragStart?.Invoke(i);
            drag.DragEnded = () => OnMarketDragEnd?.Invoke();
        }

        private void RenderBoard(Game game, int humanSeat)
        {
            UiKit.Clear(_boardRow);
            _dropGlows.Clear();
            var s = game.State;
            var me = s.Players[humanSeat];

            var turfTexture = _art.Turf(me.Turf.PowerPourNumber);
            var turfCaption = "Pours " + string.Join(",", game.PourNumbersOf(me).OrderBy(x => x));
            var turfCell = AddCard(_boardRow, turfTexture, 120, 168, turfCaption, false, null);
            AddEquipList(turfCell, game, me.Turf.Equipped);
            MakeDropTarget(turfCell, null);

            foreach (var stand in me.Stands)
            {
                var type = game.Db.StandType(stand.StandTypeId);
                string caption = $"[{string.Join(",", game.SaleNumbersOf(stand).OrderBy(x => x))}] " +
                                 $"${game.StandEarnings(me, stand)}";
                var cell = AddCard(_boardRow, _art.Stand(stand.StandTypeId, stand.Shape),
                    120, 168, caption, false, null);
                AddEquipList(cell, game, stand.Equipped);
                MakeDropTarget(cell, stand.InstanceId);
            }
        }

        /// <summary>Register a board cell as a market-card drop zone with a highlight glow.</summary>
        private void MakeDropTarget(RectTransform cell, int? standInstanceId)
        {
            var target = cell.gameObject.AddComponent<DropTarget>();
            target.StandInstanceId = standInstanceId;
            target.Dropped = (marketIndex, standId) => OnMarketDrop?.Invoke(marketIndex, standId);
            target.HoverChanged = OnDropTargetHover;

            // Double layer at full alpha so the highlight reads instantly against the dark panel.
            var top = new Vector2(0.5f, 1f);
            var wide = UiKit.CreateGlow(cell, top, top, new Vector2(0, 24),
                120 + 60, 168 + 60, DropGlowWide);
            wide.transform.SetAsFirstSibling();
            var hot = UiKit.CreateGlow(cell, top, top, new Vector2(0, 14),
                120 + 28, 168 + 28, DropGlowHot);
            hot.transform.SetSiblingIndex(1); // both behind the card

            _dropGlows.Add((standInstanceId, wide));
            _dropGlows.Add((standInstanceId, hot));
        }

        /// <summary>Remember which cells accept the dragged card; they glow on hover only.</summary>
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

        private void RenderHand(Game game, int humanSeat, MoveGroups groups)
        {
            UiKit.Clear(_handRow);
            var s = game.State;
            foreach (int id in s.Players[humanSeat].Hand)
            {
                string defId = s.LemonInstances[id].DefId;
                int optionCount = groups?.HandMoves.TryGetValue(id, out var moves) == true ? moves.Count : 0;
                int captured = id;
                AddCard(_handRow, _art.Lemon(defId), 140, 196,
                    optionCount > 0 ? $"PLAY ({optionCount})" : "",
                    optionCount > 0, () => OnHandCard?.Invoke(captured));
            }
        }

        private void RenderSupply(Game game, int humanSeat, MoveGroups groups)
        {
            UiKit.Clear(_supplyRow);
            foreach (var type in game.Db.StandTypes)
            {
                var supply = game.State.StandSupply[type.Id];
                bool clickable = groups?.SupplyMoves.ContainsKey(type.Id) == true;
                string caption = $"${game.StandPrice(humanSeat, type.Id)} x{supply.Count}";
                string captured = type.Id;
                // Show the shape you'd actually get: the top of the shuffled supply stack.
                var texture = supply.Count > 0
                    ? _art.Stand(type.Id, supply[0])
                    : _art.Stand(type.Id);
                AddCard(_supplyRow, texture, 92, 129, caption,
                    clickable, () => OnSupplyPile?.Invoke(captured));
            }

            int sold = game.State.BraggingRightsSold;
            var prices = game.Db.Supporting.BraggingRightsPrices;
            if (sold < prices.Count)
            {
                AddCard(_supplyRow, _art.BraggingRights(sold), 92, 129, $"${prices[sold]}", false, null);
            }
        }

        private void RenderOpponents(Game game, int humanSeat)
        {
            var s = game.State;
            var text = new StringBuilder();
            foreach (var p in s.Players)
            {
                if (p.PlayerId == humanSeat)
                {
                    continue;
                }
                text.Append(p.PlayerId == s.ActivePlayer ? "> " : "  ")
                    .Append($"{p.Name}  ${p.Money}  {p.Hand.Count} cards  {p.InGameVictoryPoints} VP");
                if (s.WhiniestBabyHolder == p.PlayerId)
                {
                    text.Append("  BABY");
                }
                if (s.SpoiledRottenHolder == p.PlayerId)
                {
                    text.Append("  SPOILED");
                }
                if (p.TantrumPile.Count > 0)
                {
                    text.Append($"  {p.TantrumPile.Count}xTANTRUM");
                }
                text.AppendLine();
                text.Append("   Turf ").Append(string.Join(",", game.PourNumbersOf(p).OrderBy(x => x)));
                foreach (int id in p.Turf.Equipped)
                {
                    text.Append(" | ").Append(game.Db.BlackMarket(s.BlackMarketInstances[id].DefId).Name);
                }
                text.AppendLine();
                foreach (var stand in p.Stands)
                {
                    var type = game.Db.StandType(stand.StandTypeId);
                    text.Append($"   {type.Name} [{string.Join(",", game.SaleNumbersOf(stand).OrderBy(x => x))}]" +
                                $" ${game.StandEarnings(p, stand)}");
                    foreach (int id in stand.Equipped)
                    {
                        text.Append(" | ").Append(game.Db.BlackMarket(s.BlackMarketInstances[id].DefId).Name);
                    }
                    text.AppendLine();
                }
                text.AppendLine();
            }
            _opponentsText.text = text.ToString();
        }

        private void RenderSide(Game game)
        {
            var s = game.State;
            var me = s.Players.Count > 0 ? s.Players[0] : null;
            var text = new StringBuilder();
            text.AppendLine($"Lemon deck: {s.LemonDeck.Count}   discard: {s.LemonDiscard.Count}");
            text.AppendLine($"BM deck: {s.BlackMarketDeck.Count}   discard: {s.BlackMarketDiscard.Count}");
            text.AppendLine();
            text.AppendLine("FIRST DIBS:");
            foreach (string titleId in s.FirstDibsRow)
            {
                var title = game.Db.Title(titleId);
                text.AppendLine($"  {title.Name} — {title.Condition}");
            }
            foreach (var p in s.Players)
            {
                foreach (string claimed in p.FirstDibsClaimed)
                {
                    text.AppendLine($"  [{p.Name}] {game.Db.Title(claimed).Name}");
                }
            }
            if (me != null && me.LemonLordKept.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("YOUR LEMON LORDS (secret):");
                foreach (string titleId in me.LemonLordKept)
                {
                    var title = game.Db.Title(titleId);
                    string met = game.MeetsLemonLord(me, titleId) ? " (MET!)" : "";
                    text.AppendLine($"  {title.Name}{met} — {title.Condition}");
                }
            }
            _sideText.text = text.ToString();
        }

        // ----------------------------------------------------------- helpers

        /// <summary>A card image with caption badge; hover previews, optional click.</summary>
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

        /// <summary>Names of equipped Black Market cards under a board cell.</summary>
        private void AddEquipList(RectTransform cell, Game game, List<int> equipped)
        {
            foreach (int id in equipped)
            {
                var instance = game.State.BlackMarketInstances[id];
                var badge = UiKit.CreateBadge(cell, game.Db.BlackMarket(instance.DefId).Name, 12,
                    new Color(0.20f, 0.32f, 0.24f, 0.9f));
                _preview.Attach(badge.transform.parent.gameObject,
                    _art.BlackMarket(instance.DefId, instance.Shape));
            }
        }
    }
}
