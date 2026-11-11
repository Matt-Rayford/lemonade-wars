using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The right half of the shelf: the stand supply piles and the Bragging Rights card.
    public sealed partial class TableView
    {
        /// <summary>Stand type id: is there any legal buy for that stand?</summary>
        public System.Func<string, bool> CanBuySupply;
        /// <summary>Stand type id: pick it up, then click to place.</summary>
        public System.Action<string> OnSupplyDoubleClick;

        public System.Func<bool> CanBuyBragging;
        public System.Action OnBraggingDoubleClick;

        /// <summary>Appends to the market row, so it must run after RenderMarket (which clears it).</summary>
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
                // The art index is derived from the price: $16 base, +$2 per sale.
                int sold = (braggingPrice - 16) / 2;
                BuildBraggingCell(braggingPrice, _art.BraggingRights(sold));
            }
        }

        private void BuildSupplyCell(string standTypeId, Texture2D texture)
        {
            var cell = AddShelfCell("SupplyCell", ShelfCardWidthPx);
            var card = AddShelfCard(cell, texture);

            _preview.Attach(card.Image.gameObject, texture);
            var drag = AddShelfDragSource(card.Image, DragKind.SupplyStand, texture,
                card.GlowOuter, card.GlowInner);
            drag.SupplyTypeId = standTypeId;
            drag.CanAct = () => !ShelfHeld && CanBuySupply?.Invoke(standTypeId) == true;
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
            drag.DoubleClicked = () => OnSupplyDoubleClick?.Invoke(standTypeId);
            _supplyAnchors[standTypeId] = cell;
            _supplyArt[standTypeId] = texture;
        }

        private void BuildBraggingCell(int price, Texture2D texture)
        {
            var cell = AddShelfCell("BraggingCell", ShelfCardWidthPx);
            _braggingAnchor = cell;
            BraggingArt = texture;
            var card = AddShelfCard(cell, texture);
            _preview.Attach(card.Image.gameObject, texture);

            // In the button palette, so the price pops off the dark table; click-transparent
            // like the count chips.
            var (chip, _) = BuildBottomChip(card.Frame, "PriceChip", 30f, UiKit.ButtonColor);
            chip.GetComponent<Image>().raycastTarget = false;
            var chipText = UiKit.CreateText(chip, $"${price}", 16,
                TextAnchor.MiddleCenter, UiKit.ButtonTextColor, body: true);
            chipText.raycastTarget = false;
            UiKit.Anchor((RectTransform)chipText.transform, Vector2.zero, Vector2.one);

            var drag = AddShelfDragSource(card.Image, DragKind.BraggingRights, texture,
                card.GlowOuter, card.GlowInner);
            drag.CanAct = () => !ShelfHeld && CanBuyBragging?.Invoke() == true;
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
            drag.DoubleClicked = () => OnBraggingDoubleClick?.Invoke();
        }
    }
}
