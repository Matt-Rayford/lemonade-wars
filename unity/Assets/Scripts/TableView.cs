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
        public System.Action<int> OnMarketCard;    // market row index
        public System.Action<string> OnSupplyPile; // stand type id

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
            RenderMarket(game, groups);
            RenderBoard(game, humanSeat);
            RenderHand(game, humanSeat, groups);
            RenderSupply(game, humanSeat, groups);
            RenderOpponents(game, humanSeat);
            RenderSide(game);
        }

        private void RenderMarket(Game game, MoveGroups groups)
        {
            UiKit.Clear(_marketRow);
            var s = game.State;
            for (int i = 0; i < s.Market.Count; i++)
            {
                var instance = s.BlackMarketInstances[s.Market[i]];
                var def = game.Db.BlackMarket(instance.DefId);
                var texture = _art.BlackMarket(instance.DefId, instance.Shape);
                int optionCount = groups?.MarketMoves.TryGetValue(i, out var moves) == true ? moves.Count : 0;
                int index = i;
                AddCard(_marketRow, texture, 140, 196,
                    $"${def.Cost}" + (optionCount > 0 ? $"  BUY ({optionCount})" : ""),
                    optionCount > 0, () => OnMarketCard?.Invoke(index));
            }
        }

        private void RenderBoard(Game game, int humanSeat)
        {
            UiKit.Clear(_boardRow);
            var s = game.State;
            var me = s.Players[humanSeat];

            var turfTexture = _art.Turf(me.Turf.PowerPourNumber);
            var turfCaption = "Pours " + string.Join(",", game.PourNumbersOf(me).OrderBy(x => x));
            var turfCell = AddCard(_boardRow, turfTexture, 120, 168, turfCaption, false, null);
            AddEquipList(turfCell, game, me.Turf.Equipped);

            foreach (var stand in me.Stands)
            {
                var type = game.Db.StandType(stand.StandTypeId);
                string caption = $"[{string.Join(",", game.SaleNumbersOf(stand).OrderBy(x => x))}] " +
                                 $"${game.StandEarnings(me, stand)}";
                var cell = AddCard(_boardRow, _art.Stand(stand.StandTypeId), 120, 168, caption, false, null);
                AddEquipList(cell, game, stand.Equipped);
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
                int stock = game.State.StandSupply[type.Id].Count;
                bool clickable = groups?.SupplyMoves.ContainsKey(type.Id) == true;
                string caption = $"${game.StandPrice(humanSeat, type.Id)} x{stock}";
                string captured = type.Id;
                AddCard(_supplyRow, _art.Stand(type.Id), 92, 129, caption,
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
            cell.GetComponent<LayoutElement>().preferredWidth = width;

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
