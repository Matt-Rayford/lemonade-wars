using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The shelf's Black Market row: the refresh slab and the face-up cards, plus the
    // lift-and-glow card every draggable shelf cell is built from.
    public sealed partial class TableView
    {
        /// <summary>Market index: is there any legal buy for that card?</summary>
        public System.Func<int, bool> CanBuyMarket;
        public System.Func<bool> CanRefreshMarket;
        public System.Action OnRefreshMarket;
        public System.Action<int> OnMarketDragStart;
        public System.Action OnMarketDragEnd;
        /// <summary>Market index: buy without dragging.</summary>
        public System.Action<int> OnMarketDoubleClick;

        /// <summary>
        /// True while the player drags a shelf card or stand; the app holds renders on it, because a
        /// rebuild would destroy the drag source under the cursor. The mouse check is a failsafe:
        /// a wedged counter must never freeze rendering.
        /// </summary>
        public bool HasLiveShelfDrag => _liveShelfDrags > 0 && Input.GetMouseButton(0);
        private int _liveShelfDrags;

        private void ShelfDragStarted() => _liveShelfDrags++;

        private void ShelfDragEnded() => _liveShelfDrags = Mathf.Max(0, _liveShelfDrags - 1);

        private const float ShelfCardWidthPx = 154f;
        private const float ShelfCardHeightPx = 216f;
        private static readonly Color GlowInnerColor = new Color(1f, 0.97f, 0.88f, 1f);
        private static readonly Color GlowOuterColor = new Color(1f, 0.96f, 0.82f, 0.80f);

        private RectTransform _marketRow;

        /// <summary>
        /// While true for an instance id, that Black Market card renders as a placeholder of the same
        /// size: a refilled card appears only when the purchase's theatre delivers it. Keyed by
        /// instance id, not slot, so batched buys that shift the row still hide the right cards.
        /// </summary>
        public System.Func<int, bool> MarketCardHidden;
        /// <summary>
        /// For a hidden card: the art of the card that USED to sit in its slot, drawn inert until the
        /// theatre sweeps it away, so the row does not blink empty. Null for a plain empty socket.
        /// </summary>
        public System.Func<int, Texture2D> MarketStandIn;

        private void RenderMarket(PlayerView view, CardDatabase db, MoveGroups groups)
        {
            UiKit.Clear(_marketRow);
            _marketCells.Clear();
            ForgetShelfAnchors();
            BuildDiscardPile(view, blackMarket: false);
            BuildDiscardPile(view, blackMarket: true);
            AddRowGap();
            // Always present so the row never reflows; it goes dark when it is not yours to press.
            BuildRefreshCell(db.Config.BlackMarketRefreshCost, CanRefreshMarket?.Invoke() == true);
            for (int i = 0; i < view.Market.Count; i++)
            {
                if (view.MarketGapIndex == i)
                {
                    // A contested purchase left this slot: it stays empty (the row is one
                    // short) until the purchase resolves and the refill is dealt.
                    BuildEmptyMarketCell(i, registerAnchor: false);
                }
                var card = view.Market[i];
                if (MarketCardHidden?.Invoke(card.InstanceId) == true)
                {
                    BuildHiddenMarketSlot(i, card.InstanceId);
                    continue;
                }
                var texture = _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square);
                BuildMarketCell(i, texture);
            }
            if (view.MarketGapIndex is int gap && gap >= view.Market.Count)
            {
                BuildEmptyMarketCell(gap, registerAnchor: false); // the vacated slot was the last one
            }
        }

        /// <summary>The refill's theatre has not played yet: the old card stands in until it is swept,
        /// then an empty socket, so the new card arrives with its flight rather than instantly.</summary>
        private void BuildHiddenMarketSlot(int marketIndex, int hiddenInstanceId)
        {
            var standIn = MarketStandIn?.Invoke(hiddenInstanceId);
            if (standIn != null)
            {
                BuildMarketCell(marketIndex, standIn, inert: true);
            }
            else
            {
                BuildEmptyMarketCell(marketIndex);
            }
        }

        /// <summary>A faint empty socket with a card cell's exact footprint, so the shelf does not reflow.</summary>
        /// <param name="registerAnchor">False for a contested purchase's gap: slot anchors are keyed by
        /// market index, and that index belongs to the card now shifted past the gap.</param>
        private void BuildEmptyMarketCell(int marketIndex, bool registerAnchor = true)
        {
            var cell = AddShelfCell("EmptySlot", ShelfCardWidthPx);

            var socket = UiKit.CreatePanel(cell, "Socket", new Color(0, 0, 0, 0.30f));
            socket.GetComponent<Image>().sprite = UiSprites.RoundedRect;
            socket.GetComponent<Image>().type = Image.Type.Sliced;
            socket.GetComponent<Image>().raycastTarget = false;
            // Hung from the cell's bottom edge like a card, so socket and card occupy the
            // same pixels across the swap.
            socket.anchorMin = new Vector2(0.5f, 0f);
            socket.anchorMax = new Vector2(0.5f, 0f);
            socket.pivot = new Vector2(0.5f, 0f);
            socket.sizeDelta = new Vector2(ShelfCardWidthPx, ShelfCardHeightPx);
            socket.anchoredPosition = Vector2.zero;

            // The socket's rect, not the padded cell's: a flight aimed at the cell's centre
            // would land 2px off and jump as the real card takes over.
            if (registerAnchor)
            {
                _marketSlotAnchors[marketIndex] = socket;
            }
        }

        /// <summary>The slab at the head of the row: buy a whole new row.</summary>
        private void BuildRefreshCell(int price, bool enabled)
        {
            const float width = 64f;
            var cell = AddShelfCell("RefreshCell", width);

            var idle = enabled ? UiKit.ButtonColor : new Color(0.42f, 0.36f, 0.12f, 0.85f);
            var button = UiKit.CreatePanel(cell, "Refresh", idle);
            button.GetComponent<Image>().sprite = UiSprites.RoundedRect;
            button.GetComponent<Image>().type = Image.Type.Sliced;
            button.anchorMin = new Vector2(0.5f, 1f);
            button.anchorMax = new Vector2(0.5f, 1f);
            button.pivot = new Vector2(0.5f, 1f);
            button.sizeDelta = new Vector2(width, ShelfCardHeightPx);
            button.anchoredPosition = Vector2.zero;

            // Runs up the thin slab: the label's rect is laid out along the button's
            // HEIGHT, then turned a quarter turn to read bottom-to-top.
            var label = UiKit.CreateText(button, $"REFRESH (${price})", 18,
                TextAnchor.MiddleCenter,
                enabled ? UiKit.ButtonTextColor : new Color(0.20f, 0.18f, 0.10f, 0.75f));
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(ShelfCardHeightPx - 16f, width - 8f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.localRotation = Quaternion.Euler(0, 0, 90f);

            if (!enabled)
            {
                return;
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

        /// <param name="inert">A stand-in for a card already gone: looks the part, does nothing.</param>
        private void BuildMarketCell(int marketIndex, Texture2D texture, bool inert = false)
        {
            var cell = AddShelfCell("MarketCell", ShelfCardWidthPx);
            var card = AddShelfCard(cell, texture);

            if (inert)
            {
                card.Image.raycastTarget = false;
                _marketSlotAnchors[marketIndex] = card.Lift;
                return;
            }
            _preview.Attach(card.Image.gameObject, texture);
            var drag = AddShelfDragSource(card.Image, DragKind.MarketCard, texture,
                card.GlowOuter, card.GlowInner);
            drag.MarketIndex = marketIndex;
            drag.CanAct = () => !ShelfHeld && CanBuyMarket?.Invoke(marketIndex) == true;
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
            drag.DoubleClicked = () => OnMarketDoubleClick?.Invoke(marketIndex);
            _marketCells.Add((marketIndex, cell, card.GlowOuter, card.GlowInner));
            _marketSlotAnchors[marketIndex] = card.Lift; // the card's own rect: flights land exactly on it
        }

        private RectTransform AddShelfCell(string name, float contentWidth)
        {
            var cell = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            cell.transform.SetParent(_marketRow, false);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = contentWidth + 8;
            cellElement.preferredHeight = ShelfCardHeightPx + 4;
            cellElement.flexibleWidth = 0;
            cellElement.flexibleHeight = 0;
            return (RectTransform)cell.transform;
        }

        /// <summary>A shelf card hung from its cell's bottom edge, over the two glow layers a
        /// <see cref="DragSource"/> lights on hover.</summary>
        private static (RectTransform Lift, GameObject GlowOuter, GameObject GlowInner, RawImage Image,
            RectTransform Frame) AddShelfCard(RectTransform cell, Texture2D texture)
        {
            var liftGo = new GameObject("Lift", typeof(RectTransform));
            liftGo.transform.SetParent(cell, false);
            var lift = (RectTransform)liftGo.transform;
            lift.anchorMin = new Vector2(0.5f, 0f);
            lift.anchorMax = new Vector2(0.5f, 0f);
            lift.pivot = new Vector2(0.5f, 0f);
            lift.sizeDelta = new Vector2(ShelfCardWidthPx, ShelfCardHeightPx);
            lift.anchoredPosition = Vector2.zero;

            // Glow margins split evenly above and below: centred on the card.
            var top = new Vector2(0.5f, 1f);
            var glowOuter = UiKit.CreateGlow(lift, top, top, new Vector2(0, 22),
                ShelfCardWidthPx + 44, ShelfCardHeightPx + 44, GlowOuterColor);
            var glowInner = UiKit.CreateGlow(lift, top, top, new Vector2(0, 10),
                ShelfCardWidthPx + 20, ShelfCardHeightPx + 20, GlowInnerColor);

            var image = UiKit.CreateCardImage(lift, texture, ShelfCardWidthPx, ShelfCardHeightPx);
            var frame = (RectTransform)image.transform.parent;
            frame.anchorMin = top;
            frame.anchorMax = top;
            frame.pivot = top;
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(ShelfCardWidthPx, ShelfCardHeightPx);
            return (lift, glowOuter, glowInner, image, frame);
        }

        private DragSource AddShelfDragSource(RawImage cardImage, DragKind kind, Texture2D texture,
            GameObject glowOuter, GameObject glowInner)
        {
            var drag = cardImage.gameObject.AddComponent<DragSource>();
            drag.Kind = kind;
            drag.Texture = texture;
            drag.CanvasRoot = _canvasRoot;
            drag.GlowInner = glowInner;
            drag.GlowOuter = glowOuter;
            return drag;
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
