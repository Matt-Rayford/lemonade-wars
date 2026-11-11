using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public sealed partial class PlayTheatre
    {
        private const float BadgeDiameterPx = 41f;
        /// <summary>The badge's centre sits this far inside the card's top-left corner: overlapping the edge, not hiding it.</summary>
        private const float BadgeInsetPx = 10f;
        private const float GrabIconSizePx = 75f;
        /// <summary>The hand's centre above the loot's top edge: high enough that only the thumb tip overlaps the card.</summary>
        private const float GrabIconLiftPx = 10f;

        /// <summary>Builds the staged play, the loot and every pile card alike; the caller reads the CanvasGroup off the chip.</summary>
        private RectTransform BuildChip(Texture2D art)
        {
            var image = UiKit.CreateCardImage(_layer, art, CardWidth, CardHeight);
            var chip = (RectTransform)image.transform.parent;
            foreach (var graphic in chip.GetComponentsInChildren<Graphic>())
            {
                graphic.raycastTarget = false;
            }
            // The art itself catches the pointer, so the card reads at full size while
            // staged: attacks carry text people need to see.
            image.raycastTarget = true;
            _preview.Attach(image.gameObject, art);
            chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0.5f);
            chip.pivot = new Vector2(0.5f, 0.5f);
            chip.sizeDelta = new Vector2(CardWidth, CardHeight);
            var group = chip.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = true;
            return chip;
        }

        private RectTransform BuildLoot(Texture2D lootArt)
        {
            var hand = _grabIcon != null ? BuildGrabbingHand() : null;
            var chip = BuildChip(lootArt);
            _lootGroup = chip.GetComponent<CanvasGroup>();
            if (hand != null)
            {
                // Child of the chip so it moves, scales and fades with it; last in the
                // hierarchy so it draws over the card.
                hand.SetParent(chip, false);
                hand.SetAsLastSibling();
                hand.anchoredPosition = new Vector2(0, CardHeight / 2f + GrabIconLiftPx);
            }
            return chip;
        }

        private RectTransform BuildGrabbingHand()
        {
            var handGo = new GameObject("GrabbingHand", typeof(RectTransform), typeof(RawImage));
            handGo.transform.SetParent(_layer, false);
            var hand = (RectTransform)handGo.transform;
            hand.anchorMin = hand.anchorMax = new Vector2(0.5f, 0.5f);
            hand.pivot = new Vector2(0.5f, 0.5f);
            hand.sizeDelta = new Vector2(GrabIconSizePx, GrabIconSizePx);
            var handImage = handGo.GetComponent<RawImage>();
            handImage.texture = _grabIcon;
            handImage.raycastTarget = false;
            return hand;
        }

        /// <param name="isStagedPlay">The staged play's badge is replaced when the card changes hands; a pile card keeps its own.</param>
        private void PinPlayerBadge(RectTransform chip, int playerId, bool isStagedPlay = true)
        {
            if (isStagedPlay && _badge != null)
            {
                Object.Destroy(_badge.gameObject);
            }
            var badge = TableView.CreatePlayerBadge(chip, playerId, _nameOf(playerId), BadgeDiameterPx);
            badge.anchorMin = badge.anchorMax = new Vector2(0f, 1f);
            badge.pivot = new Vector2(0.5f, 0.5f);
            badge.anchoredPosition = new Vector2(BadgeInsetPx, -BadgeInsetPx);
            if (isStagedPlay)
            {
                _badge = badge;
            }
        }

        private void DestroyLoot()
        {
            if (_loot != null)
            {
                Object.Destroy(_loot.gameObject);
            }
            _loot = null;
            _lootGroup = null;
        }
    }
}
