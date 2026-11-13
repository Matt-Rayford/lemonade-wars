using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The two title fans flanking the hand: First Dibs (bottom-left) and your secret
    // Lemon Lords plus held special titles (bottom-right), with the landing-spot math
    // the claim-reveal flights aim at.
    public sealed partial class TableView
    {
        private const float TitleFanCardWidthPx = 190f;
        private const float TitleFanCardHeightPx = 266f;
        /// <summary>How much of a title card shows above the screen's bottom edge at rest.</summary>
        private const float TitleFanPeekPx = 158f;
        private const float TitleFanRestY = TitleFanPeekPx - TitleFanCardHeightPx;
        private const float TitleFanRaisedY = 12f;
        /// <summary>Neighbouring cards overlap to this share of a card's width, or tighter when the zone is narrow.</summary>
        private const float TitleFanMaxSpacingFraction = 0.72f;

        private const float ClaimMarkerInsetPx = 4f;
        /// <summary>Narrow enough to clear the next card when six titles squeeze the fan to about 40px a card.</summary>
        private const float ClaimAvatarDiameterPx = 34f;
        private const float ClaimNameBarGapPx = 6f;
        private const float ClaimNameBarThicknessPx = 24f;
        /// <summary>From under the avatar to just short of the screen's edge while the card rests peeking.</summary>
        private const float ClaimNameBarLengthPx = 100f;

        private RectTransform _lordHost;
        private RectTransform _dibsHost;
        // An unchanged fan is not rebuilt: rebuilding a hovered fan drops the raise.
        private string _dibsSignature;
        private string _lordSignature;

        /// <summary>While true, the Whiniest Baby card stays OUT of the lord fan and the player bars even
        /// though the view says it is held: the app sets it for the span of the claim reveal's flight.</summary>
        public bool SuppressWhiniestBaby;
        /// <summary>Spoiled Rotten's twin of SuppressWhiniestBaby.</summary>
        public bool SuppressSpoiledRotten;
        /// <summary>The Spoiled Rotten flight lands one slot further right while the baby is in the fan.</summary>
        private bool _lordFanHasBaby;

        /// <summary>Unclaimed titles from the row first, then claimed ones wearing their claimant's name.</summary>
        private void RenderFirstDibs(PlayerView view)
        {
            var entries = FirstDibsEntries(view);
            string signature = Mathf.RoundToInt(_dibsHost.rect.width) + "|" +
                string.Join(",", entries.Select(e => e.TitleId + ":" + e.Claimant?.Name));
            if (signature == _dibsSignature)
            {
                return;
            }
            _dibsSignature = signature;

            UiKit.Clear(_dibsHost);
            if (entries.Count == 0)
            {
                return;
            }

            float available = _dibsHost.rect.width;
            if (available < 10f)
            {
                available = 390f; // first-frame fallback before canvas layout settles
            }
            float spacing = TitleFanSpacing(entries.Count, available);
            float startX = TitleFanStartX(entries.Count, spacing);

            for (int i = 0; i < entries.Count; i++)
            {
                var (titleId, claimant) = entries[i];
                var texture = _art.Title(titleId);
                var card = AddTitleFanCard(_dibsHost, texture, startX + i * spacing);
                if (claimant != null)
                {
                    AddClaimantMarkers(card.Frame, claimant);
                }
                RaiseTitleFanCardOnHover(card.Image, card.Frame, card.Motion, restingSiblingIndex: i);
                _preview.Attach(card.Image.gameObject, texture);
            }
        }

        private static List<(string TitleId, PlayerView.PlayerPanel Claimant)> FirstDibsEntries(PlayerView view)
        {
            var entries = new List<(string TitleId, PlayerView.PlayerPanel Claimant)>();
            foreach (string titleId in view.FirstDibsRow)
            {
                entries.Add((titleId, null));
            }
            foreach (var player in view.Players)
            {
                foreach (string claimed in player.FirstDibsClaimed)
                {
                    entries.Add((claimed, player));
                }
            }
            return entries;
        }

        /// <summary>
        /// Who won a First Dibs title: their avatar on the card's top-left corner and their name down the
        /// left edge. Both sit in the sliver that stays visible when the next card overlaps this one, and
        /// neither takes the pointer, or that sliver could no longer raise the card.
        /// </summary>
        private static void AddClaimantMarkers(RectTransform cardFrame, PlayerView.PlayerPanel claimant)
        {
            var avatar = CreatePlayerBadge(cardFrame, claimant.PlayerId, claimant.Name, ClaimAvatarDiameterPx);
            avatar.anchorMin = avatar.anchorMax = new Vector2(0f, 1f);
            avatar.pivot = new Vector2(0f, 1f);
            avatar.anchoredPosition = new Vector2(ClaimMarkerInsetPx, -ClaimMarkerInsetPx);

            // Built lying flat, then stood on end: the name reads bottom to top, like a book's spine.
            var nameBar = UiKit.CreatePanel(cardFrame, "ClaimNameBar", UiKit.ButtonColor);
            nameBar.GetComponent<Image>().raycastTarget = false;
            nameBar.anchorMin = nameBar.anchorMax = new Vector2(0f, 1f);
            nameBar.pivot = new Vector2(0.5f, 0.5f);
            nameBar.sizeDelta = new Vector2(ClaimNameBarLengthPx, ClaimNameBarThicknessPx);
            float avatarBottomY = ClaimMarkerInsetPx + ClaimAvatarDiameterPx;
            nameBar.anchoredPosition = new Vector2(
                ClaimMarkerInsetPx + ClaimAvatarDiameterPx / 2f,
                -(avatarBottomY + ClaimNameBarGapPx + ClaimNameBarLengthPx / 2f));
            nameBar.localEulerAngles = new Vector3(0f, 0f, 90f);

            var nameText = UiKit.CreateText(nameBar, claimant.Name.ToUpperInvariant(), 13,
                TextAnchor.MiddleCenter, UiKit.ButtonTextColor, body: true);
            nameText.raycastTarget = false;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            UiKit.Anchor((RectTransform)nameText.transform, Vector2.zero, Vector2.one,
                new Vector2(4f, 0f), new Vector2(-4f, 0f));
        }

        /// <summary>Your two secret Lemon Lords, with the Whiniest Baby and Spoiled Rotten cards joining
        /// on the LEFT while you hold them.</summary>
        private void RenderLords(PlayerView view)
        {
            // A special title joins the fan only once its claim animation has delivered it.
            bool hasBaby = view.WhiniestBabyHolder == view.ViewerId && !SuppressWhiniestBaby;
            bool hasRotten = view.SpoiledRottenHolder == view.ViewerId && !SuppressSpoiledRotten;
            _lordFanHasBaby = hasBaby;

            string signature = (hasBaby ? "baby|" : "|") + (hasRotten ? "rotten|" : "|") +
                string.Join(",", view.LemonLordStatus.Select(l => l.TitleId + ":" + l.Met));
            if (signature == _lordSignature)
            {
                return;
            }
            _lordSignature = signature;

            UiKit.Clear(_lordHost);
            var cards = new List<(Texture2D Texture, PlayerView.LordStatus Lord)>();
            if (hasBaby)
            {
                cards.Add((_art.WhiniestBaby(), null));
            }
            if (hasRotten)
            {
                cards.Add((_art.SpoiledRotten(), null));
            }
            cards.AddRange(view.LemonLordStatus.Select(l => (_art.Title(l.TitleId), l)));
            int count = cards.Count;
            if (count == 0)
            {
                return;
            }

            float spacing = LordSpacing(count);
            float startX = TitleFanStartX(count, spacing);

            for (int i = 0; i < count; i++)
            {
                var (texture, lord) = cards[i];
                var card = AddTitleFanCard(_lordHost, texture, startX + i * spacing);
                if (lord != null && lord.Met)
                {
                    AddTitleFanChip(card.Frame, "MetChip", "MET!", 74f, fontSize: 14, bodyFont: false);
                }
                RaiseTitleFanCardOnHover(card.Image, card.Frame, card.Motion, restingSiblingIndex: i);
                _preview.Attach(card.Image.gameObject, texture);
            }
        }

        private static (RawImage Image, RectTransform Frame, HandCardMotion Motion) AddTitleFanCard(
            RectTransform host, Texture2D texture, float x)
        {
            var image = UiKit.CreateCardImage(host, texture, TitleFanCardWidthPx, TitleFanCardHeightPx);
            var frame = (RectTransform)image.transform.parent;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
            frame.pivot = new Vector2(0.5f, 0f);
            frame.sizeDelta = new Vector2(TitleFanCardWidthPx, TitleFanCardHeightPx);
            frame.anchoredPosition = new Vector2(x, TitleFanRestY);
            var motion = frame.gameObject.AddComponent<HandCardMotion>();
            motion.TargetY = TitleFanRestY;
            return (image, frame, motion);
        }

        private static void AddTitleFanChip(RectTransform cardFrame, string chipName, string label,
            float widthPx, int fontSize, bool bodyFont)
        {
            var chip = UiKit.CreatePanel(cardFrame, chipName, UiKit.ButtonColor);
            UiKit.Anchor(chip, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            chip.sizeDelta = new Vector2(widthPx, 24f);
            chip.anchoredPosition = new Vector2(0, -24f);
            var chipText = UiKit.CreateText(chip, label, fontSize,
                TextAnchor.MiddleCenter, UiKit.ButtonTextColor, body: bodyFont);
            UiKit.Anchor((RectTransform)chipText.transform, Vector2.zero, Vector2.one);
        }

        private static void RaiseTitleFanCardOnHover(RawImage image, RectTransform frame,
            HandCardMotion motion, int restingSiblingIndex)
        {
            UiKit.AddHover(image.gameObject,
                () =>
                {
                    frame.SetAsLastSibling();
                    motion.TargetY = TitleFanRaisedY;
                },
                () =>
                {
                    frame.SetSiblingIndex(restingSiblingIndex);
                    motion.TargetY = TitleFanRestY;
                });
        }

        private static float TitleFanSpacing(int count, float availableWidth) =>
            count > 1
                ? Mathf.Min(TitleFanCardWidthPx * TitleFanMaxSpacingFraction,
                    (availableWidth - TitleFanCardWidthPx) / (count - 1))
                : 0f;

        /// <summary>The first card's centre, with the fan centred on its host.</summary>
        private static float TitleFanStartX(int count, float spacing) =>
            -(TitleFanCardWidthPx + spacing * (count - 1)) / 2f + TitleFanCardWidthPx / 2f;

        /// <summary>The lord fan compresses once a third card would spill past the screen edge; the margin
        /// keeps a sliver of table visible on the right.</summary>
        private float LordSpacing(int count)
        {
            const float margin = 24f;
            float available = _lordHost.rect.width - margin;
            if (available < 10f)
            {
                available = 380f; // first-frame fallback before canvas layout settles
            }
            return TitleFanSpacing(count, available);
        }

        /// <summary>The baby joins the fan LEFT-MOST.</summary>
        public Vector3 WhiniestBabyLandingWorld() => LordLandingWorld(0);

        /// <summary>Spoiled Rotten lands after the baby when the baby is in the fan.</summary>
        public Vector3 SpoiledRottenLandingWorld() => LordLandingWorld(_lordFanHasBaby ? 1 : 0);

        /// <summary>A slot's world position in the lord fan as it will be AFTER the flying card is added
        /// (the fan hides an in-flight card, so its children do not include it yet).</summary>
        private Vector3 LordLandingWorld(int slot)
        {
            int futureCount = _lordHost.childCount + 1;
            float spacing = LordSpacing(futureCount);
            float startX = TitleFanStartX(futureCount, spacing);
            // Cards hang from the host's bottom-centre pivot: a card's centre is half a card above its rest position.
            return _lordHost.TransformPoint(
                new Vector3(startX + slot * spacing, TitleFanRestY + TitleFanCardHeightPx / 2f, 0));
        }
    }
}
