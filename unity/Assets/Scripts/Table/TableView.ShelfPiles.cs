using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The two piles at the head of the shelf (each card back stands for its deck and, on a
    // click, its discards), and the chips that sit on a shelf card's bottom edge (the Lemon
    // pile's DRAW chip, the Bragging Rights price).
    public sealed partial class TableView
    {
        private const int DeckCountFontSize = 20;
        private const float DeckCountHeightPx = 24f;
        /// <summary>Below the card's bottom edge, clear of the DRAW chip's 2px overhang.</summary>
        private const float DeckCountDropPx = 5f;
        private static readonly Color DeckCountColor = new Color(0.86f, 0.86f, 0.90f);

        /// <summary>Shows its count on hover; click to browse.</summary>
        private void BuildDiscardPile(PlayerView view, bool blackMarket)
        {
            var back = _art.Back(blackMarket ? "blackMarket" : "lemon");
            var image = UiKit.CreateCardImage(_marketRow, back, ShelfCardWidthPx, ShelfCardHeightPx);
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
            // cursor and hover enters and exits in an endless flicker.
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
                _lemonPileAnchor = frame;
                BuildDrawChip(frame);
            }
            AddDeckCount(frame, blackMarket ? view.BlackMarketDeckCount : view.LemonDeckCount);
        }

        /// <summary>How many cards are left to draw, under the pile on the bare table.</summary>
        private static void AddDeckCount(RectTransform pileFrame, int cardsLeft)
        {
            var count = UiKit.CreateText(pileFrame, cardsLeft.ToString(), DeckCountFontSize,
                TextAnchor.MiddleCenter, DeckCountColor);
            count.raycastTarget = false;
            UiKit.AddTextShadow(count);
            var countRect = (RectTransform)count.transform;
            countRect.anchorMin = new Vector2(0f, 0f);
            countRect.anchorMax = new Vector2(1f, 0f);
            countRect.pivot = new Vector2(0.5f, 1f);
            countRect.offsetMin = new Vector2(0f, -DeckCountDropPx - DeckCountHeightPx);
            countRect.offsetMax = new Vector2(0f, -DeckCountDropPx);
        }

        /// <summary>A full-width chip on a card's bottom edge, its bottom corners tracing the card's rounding: a mask
        /// clips a taller rounded fill and overhangs the card by 2px, so the white border cannot peek out.</summary>
        private static (RectTransform Chip, Image Fill) BuildBottomChip(
            RectTransform cardFrame, string name, float height, Color color)
        {
            float radius = UiKit.CardCornerRadius(ShelfCardWidthPx);
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

        /// <summary>A second draw affordance beside the hand's draw slot; dark and inert when drawing is not legal.</summary>
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
                chip.GetComponent<Image>().raycastTarget = false; // the pile's browse-click passes through
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
    }
}
