using System.Collections.Generic;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The shelf: Black Market row, discard piles, refresh slab, stand supply and the
    // Bragging Rights card — every drag SOURCE along the top band, plus the world-space
    // anchors effects use as flight origins.
    public sealed partial class TableView
    {
        // Market drag & drop.
        public System.Func<int, bool> CanBuyMarket;         // market index -> any legal buy?
        public System.Func<bool> CanRefreshMarket;
        public System.Action OnRefreshMarket;
        public System.Action<int> OnMarketDragStart;        // market index
        public System.Action OnMarketDragEnd;

        // Supply drag & drop (stand purchases with row-position insert).
        public System.Func<string, bool> CanBuySupply;      // stand type -> any legal buy?

        // Bragging Rights drag & drop (shelf card -> your VP column).
        public System.Func<bool> CanBuyBragging;

        /// <summary>
        /// True while the player drags a shelf card/stand; the app holds renders on it
        /// (a rebuild would destroy the drag source under the cursor). The mouse check
        /// is a failsafe: a wedged counter must never freeze rendering.
        /// </summary>
        public bool HasLiveShelfDrag => _liveShelfDrags > 0 && Input.GetMouseButton(0);
        private int _liveShelfDrags;

        private void ShelfDragStarted() => _liveShelfDrags++;

        private void ShelfDragEnded() => _liveShelfDrags = Mathf.Max(0, _liveShelfDrags - 1);

        private static readonly Color GlowInnerColor = new Color(1f, 0.97f, 0.88f, 1f);
        private static readonly Color GlowOuterColor = new Color(1f, 0.96f, 0.82f, 0.80f);

        private RectTransform _marketRow;

        /// <summary>Market-row twin of SuppressWhiniestBaby, per card: while true for an
        /// instance id, that Black Market card renders as an EMPTY placeholder (same
        /// size, so the shelf never reflows) — a refilled card appears only when the
        /// purchase drift delivers and the app lifts the hold (see LemonadeWarsApp).
        /// Keyed by instance id, not slot, so batched buys that shift the row still
        /// hide exactly the cards the viewer has not been shown.</summary>
        public System.Func<int, bool> MarketCardHidden;
        /// <summary>
        /// For a hidden market slot: the art of the card that USED to sit there, drawn
        /// inert in its place until the theatre sweeps it away — the row must not
        /// blink empty between the engine's refill and the effect that shows it.
        /// Null = plain empty socket.
        /// </summary>
        public System.Func<int, Texture2D> MarketStandIn;
        /// <summary>Shelf cells by market slot index — flight-origin anchors; includes
        /// suppressed placeholder cells so a slot's spot is known while empty.</summary>
        private readonly Dictionary<int, RectTransform> _marketSlotAnchors =
            new Dictionary<int, RectTransform>();
        /// <summary>Stand-supply piles by stand type id — flight-origin anchors.</summary>
        private readonly Dictionary<string, RectTransform> _supplyAnchors =
            new Dictionary<string, RectTransform>();
        /// <summary>The Bragging Rights shelf cell, null while sold out.</summary>
        private RectTransform _braggingAnchor;
        /// <summary>The Black Market deck back on the shelf — where swept cards go and dealt cards come from.</summary>
        private RectTransform _blackMarketDeckAnchor;

        private void RenderMarket(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            UiKit.Clear(_marketRow);
            _marketCells.Clear();
            // Everything in the row is being destroyed — drop the flight anchors too
            // (RenderSupply repopulates the supply/bragging ones right after).
            _marketSlotAnchors.Clear();
            _supplyAnchors.Clear();
            _braggingAnchor = null;
            _blackMarketDeckAnchor = null;
            // The discard piles form their own group: Lemon, then Black Market,
            // separated from the face-up market cards.
            BuildDiscardPile(view, blackMarket: false);
            BuildDiscardPile(view, blackMarket: true);
            AddRowGap();
            // The refresh belongs to the shelf it refreshes, not to the action bar.
            // Always present so the row never reflows; it just goes dark when it
            // isn't yours to press.
            BuildRefreshCell(db.Config.BlackMarketRefreshCost, CanRefreshMarket?.Invoke() == true);
            for (int i = 0; i < view.Market.Count; i++)
            {
                var card = view.Market[i];
                if (MarketCardHidden?.Invoke(card.InstanceId) == true)
                {
                    // The refill's theatre has not played yet: show the old card as a
                    // stand-in until it is swept, or an empty socket once it has gone,
                    // so the new card arrives with the flight rather than instantly.
                    var standIn = MarketStandIn?.Invoke(card.InstanceId);
                    if (standIn != null)
                    {
                        BuildMarketCell(i, standIn, inert: true);
                    }
                    else
                    {
                        BuildEmptyMarketCell(i);
                    }
                    continue;
                }
                var texture = _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square);
                BuildMarketCell(i, texture);
            }
        }

        /// <summary>
        /// Placeholder for a suppressed market slot: same layout footprint as a card
        /// cell (the shelf must not reflow), drawn as a faint empty socket.
        /// </summary>
        private void BuildEmptyMarketCell(int marketIndex)
        {
            const float width = 154f;
            const float height = 216f;

            var cell = new GameObject("EmptySlot", typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_marketRow, false);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width + 8;
            cellElement.preferredHeight = height + 4;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var socket = UiKit.CreatePanel(cell.transform, "Socket", new Color(0, 0, 0, 0.30f));
            socket.GetComponent<Image>().sprite = UiSprites.RoundedRect;
            socket.GetComponent<Image>().type = Image.Type.Sliced;
            socket.GetComponent<Image>().raycastTarget = false;
            // Card cells hang their card from the cell's bottom edge — match exactly,
            // so socket and card occupy the same pixels across the swap.
            socket.anchorMin = new Vector2(0.5f, 0f);
            socket.anchorMax = new Vector2(0.5f, 0f);
            socket.pivot = new Vector2(0.5f, 0f);
            socket.sizeDelta = new Vector2(width, height);
            socket.anchoredPosition = Vector2.zero;

            // The CARD's rect, not the cell's: the cell is padded, so a flight aimed at
            // its centre would land 2px off and jump as the real card takes over.
            _marketSlotAnchors[marketIndex] = socket;
        }

        /// <summary>
        /// Solid lemonade-yellow slab at the head of the shelf: buy a whole new row.
        /// Card-height so it reads as part of the market, not as chrome laid over it.
        /// </summary>
        private void BuildRefreshCell(int price, bool enabled)
        {
            const float width = 64f;
            const float height = 216f;

            var cell = new GameObject("RefreshCell", typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_marketRow, false);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width + 8;
            cellElement.preferredHeight = height + 4;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;

            var idle = enabled ? UiKit.ButtonColor : new Color(0.42f, 0.36f, 0.12f, 0.85f);
            var button = UiKit.CreatePanel(cell.transform, "Refresh", idle);
            button.GetComponent<Image>().sprite = UiSprites.RoundedRect;
            button.GetComponent<Image>().type = Image.Type.Sliced;
            var buttonRect = button;
            buttonRect.anchorMin = new Vector2(0.5f, 1f);
            buttonRect.anchorMax = new Vector2(0.5f, 1f);
            buttonRect.pivot = new Vector2(0.5f, 1f);
            buttonRect.sizeDelta = new Vector2(width, height);
            buttonRect.anchoredPosition = Vector2.zero;

            // Turned on its side to run up the thin slab: the label's own rect is laid
            // out along the button's HEIGHT, then rotated a quarter turn.
            var label = UiKit.CreateText(button, $"REFRESH (${price})", 18,
                TextAnchor.MiddleCenter,
                enabled ? UiKit.ButtonTextColor : new Color(0.20f, 0.18f, 0.10f, 0.75f));
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(height - 16f, width - 8f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.localRotation = Quaternion.Euler(0, 0, 90f); // reads bottom-to-top

            if (!enabled)
            {
                return; // dark and inert: no hover, nothing to submit
            }
            var image = button.GetComponent<Image>();
            UiKit.AddHover(button.gameObject,
                () => image.color = new Color(1f, 0.92f, 0.45f),
                () => image.color = idle);
            UiKit.AddClick(button.gameObject, () =>
            {
                Sfx.Play(Sfx.RefreshMarket); // the shelf being swept, not a UI click
                OnRefreshMarket?.Invoke();
            });
        }

        /// <summary>A market card: hover glows it; drag it onto your turf/stands to buy.</summary>
        /// <param name="inert">A stand-in for a card already gone: looks the part, does nothing.</param>
        private void BuildMarketCell(int marketIndex, Texture2D texture, bool inert = false)
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

            if (inert)
            {
                image.raycastTarget = false;
                _marketSlotAnchors[marketIndex] = lift;
                return;
            }
            _preview.Attach(image.gameObject, texture);
            var drag = image.gameObject.AddComponent<DragSource>();
            drag.Kind = DragKind.MarketCard;
            drag.MarketIndex = marketIndex;
            drag.Texture = texture;
            drag.CanvasRoot = _canvasRoot;
            drag.GlowInner = glowInner;
            drag.GlowOuter = glowOuter;
            drag.CanAct = () => CanBuyMarket?.Invoke(marketIndex) == true;
            drag.DragStarted = () =>
            {
                ShelfDragStarted();
                OnMarketDragStart?.Invoke(marketIndex);
            };
            drag.DragEnded = () =>
            {
                ShelfDragEnded();
                OnMarketDragEnd?.Invoke();
            };
            _marketCells.Add((marketIndex, (RectTransform)cell.transform, glowOuter, glowInner));
            _marketSlotAnchors[marketIndex] = lift; // the card's own rect: flights land exactly on it
        }

        /// <summary>
        /// World position of a Black Market slot on the shelf — purchase-drift origin.
        /// Valid for card AND suppressed-placeholder cells; null when the slot does not
        /// exist (market shrank with an empty deck). Forces the row's layout, because
        /// effects resolve origins the same frame the shelf is rebuilt, before the
        /// layout group has placed its children.
        /// </summary>
        public Vector3? MarketSlotWorld(int index) =>
            _marketSlotAnchors.TryGetValue(index, out var cell) ? CellCenterWorld(cell) : null;

        /// <summary>World position of a stand-supply pile on the shelf.</summary>
        public Vector3? StandSupplyWorld(string standTypeId) =>
            _supplyAnchors.TryGetValue(standTypeId, out var cell) ? CellCenterWorld(cell) : null;

        /// <summary>World position of the Bragging Rights shelf card; null once sold out.</summary>
        public Vector3? BraggingShelfWorld() => CellCenterWorld(_braggingAnchor);

        /// <summary>World position of the Black Market deck back on the shelf.</summary>
        public Vector3? BlackMarketDeckWorld() => CellCenterWorld(_blackMarketDeckAnchor);

        private Vector3? CellCenterWorld(RectTransform cell)
        {
            if (cell == null) // also true for a destroyed cell mid-teardown
            {
                return null;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_marketRow);
            return cell.TransformPoint(cell.rect.center);
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
                BuildBraggingCell(braggingPrice, _art.BraggingRights(sold));
            }
        }

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
                ShelfDragStarted();
                _supplyDragActive = true;
                _preview.SetDragging(true);
            };
            drag.DragEnded = () =>
            {
                ShelfDragEnded();
                _supplyDragActive = false;
                _preview.SetDragging(false);
                HideInsertPreview();
            };
            _supplyAnchors[standTypeId] = (RectTransform)cell.transform;
        }

        /// <summary>
        /// The Bragging Rights shelf card: wears its price, and drags onto your VP
        /// column to buy — the same lift-glow-ghost language as the market cards.
        /// </summary>
        private void BuildBraggingCell(int price, Texture2D texture)
        {
            const float width = 154f;
            const float height = 216f;

            var cell = new GameObject("BraggingCell", typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_marketRow, false);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = width + 8;
            cellElement.preferredHeight = height + 4;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;
            _braggingAnchor = (RectTransform)cell.transform;

            var liftGo = new GameObject("Lift", typeof(RectTransform));
            liftGo.transform.SetParent(cell.transform, false);
            var lift = (RectTransform)liftGo.transform;
            lift.anchorMin = new Vector2(0.5f, 0f);
            lift.anchorMax = new Vector2(0.5f, 0f);
            lift.pivot = new Vector2(0.5f, 0f);
            lift.sizeDelta = new Vector2(width, height);
            lift.anchoredPosition = Vector2.zero;

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

            // Price chip along the bottom edge; click-transparent like the count chips.
            // Full lemonade yellow with black text — the button palette — so the price
            // pops off the dark table instead of dissolving into it.
            var (chip, _) = BuildBottomChip(frame, "PriceChip", 30f, UiKit.ButtonColor);
            chip.GetComponent<Image>().raycastTarget = false;
            var chipText = UiKit.CreateText(chip, $"${price}", 16,
                TextAnchor.MiddleCenter, UiKit.ButtonTextColor, body: true);
            chipText.raycastTarget = false;
            UiKit.Anchor((RectTransform)chipText.transform, Vector2.zero, Vector2.one);

            var drag = image.gameObject.AddComponent<DragSource>();
            drag.Kind = DragKind.BraggingRights;
            drag.Texture = texture;
            drag.CanvasRoot = _canvasRoot;
            drag.GlowInner = glowInner;
            drag.GlowOuter = glowOuter;
            drag.CanAct = () => CanBuyBragging?.Invoke() == true;
            drag.DragStarted = () =>
            {
                ShelfDragStarted();
                _preview.SetDragging(true);
                if (_vpGlow != null)
                {
                    _vpGlow.SetActive(true);
                }
            };
            drag.DragEnded = () =>
            {
                ShelfDragEnded();
                _preview.SetDragging(false);
                if (_vpGlow != null)
                {
                    _vpGlow.SetActive(false);
                }
            };
        }

        /// <summary>A discard pile: card back, count on hover, click to browse.</summary>
        private void BuildDiscardPile(PlayerView view, bool blackMarket)
        {
            var back = _art.Back(blackMarket ? "blackMarket" : "lemon");
            var image = UiKit.CreateCardImage(_marketRow, back, 154, 216);
            var frame = (RectTransform)image.transform.parent;
            if (blackMarket)
            {
                _blackMarketDeckAnchor = frame;
            }

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
                TextAnchor.MiddleCenter, Color.white, body: true);
            chipText.raycastTarget = false;
            UiKit.Anchor((RectTransform)chipText.transform, Vector2.zero, Vector2.one);
            chip.gameObject.SetActive(false);

            UiKit.AddHover(image.gameObject,
                () => chip.gameObject.SetActive(true),
                () => chip.gameObject.SetActive(false));
            UiKit.AddClick(image.gameObject, () => OpenDiscardViewer(
                blackMarket ? "BLACK MARKET DISCARDS" : "LEMON DISCARDS",
                discards, blackMarket));

            if (!blackMarket)
            {
                BuildDrawChip(frame);
            }
        }

        /// <summary>
        /// Full-width chip on a card's bottom edge: square top, bottom corners tracing
        /// the card's own rounding. Built as a MASK clipping a taller rounded fill
        /// (the fill's top corners are cut off), overhanging the card by 2px so the
        /// white border can't peek out around the curve.
        /// </summary>
        private static (RectTransform Chip, Image Fill) BuildBottomChip(
            RectTransform cardFrame, string name, float height, Color color)
        {
            float radius = UiKit.CardCornerRadius(154f);
            var chip = UiKit.CreatePanel(cardFrame, name, new Color(0, 0, 0, 0));
            UiKit.Anchor(chip, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(-2f, -2f), new Vector2(2f, height));
            chip.gameObject.AddComponent<RectMask2D>();

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(chip, false);
            UiKit.Anchor((RectTransform)fillGo.transform, Vector2.zero, Vector2.one,
                new Vector2(0, 0), new Vector2(0, radius));
            var fill = fillGo.GetComponent<Image>();
            fill.sprite = UiSprites.RoundedRect;
            fill.type = Image.Type.Sliced;
            fill.pixelsPerUnitMultiplier = 14f / radius;
            fill.raycastTarget = false;
            fill.color = color;
            return (chip, fill);
        }

        /// <summary>
        /// Always-on DRAW chip along the lemon pile's bottom edge — a second draw
        /// affordance beside the hand's draw slot, in the same dark chrome as the
        /// END TURN chip. Inset so its rounded corners nest inside the card's, and
        /// dark-and-inert (like the refresh slab) when drawing isn't legal.
        /// </summary>
        private void BuildDrawChip(RectTransform cardFrame)
        {
            bool enabled = CanDrawLemon?.Invoke() == true;
            var idle = enabled
                ? new Color(0.10f, 0.12f, 0.16f)
                : new Color(0.13f, 0.15f, 0.19f);
            var (chip, fill) = BuildBottomChip(cardFrame, "DrawChip", 34f, idle);

            var label = UiKit.CreateText(chip, "DRAW", 18, TextAnchor.MiddleCenter,
                enabled ? new Color(0.96f, 0.94f, 0.86f) : new Color(0.55f, 0.54f, 0.50f));
            label.raycastTarget = false;
            UiKit.Anchor((RectTransform)label.transform, Vector2.zero, Vector2.one);

            if (!enabled)
            {
                chip.GetComponent<Image>().raycastTarget = false; // browse-click passes through
                return;
            }
            UiKit.AddHover(chip.gameObject,
                () =>
                {
                    fill.color = new Color(0.18f, 0.21f, 0.28f);
                    label.color = UiKit.ButtonColor;
                },
                () =>
                {
                    fill.color = idle;
                    label.color = new Color(0.96f, 0.94f, 0.86f);
                });
            // No button sound: the draw's own card-draw sound answers the click.
            UiKit.AddClick(chip.gameObject, () => OnDrawCard?.Invoke());
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
    }
}
