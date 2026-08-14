using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;

namespace LemonadeWars.Unity
{
    // The two title fans flanking the hand: First Dibs (bottom-left) and your secret
    // Lemon Lords plus held special titles (bottom-right), with the landing-spot math
    // the claim-reveal flights aim at.
    public sealed partial class TableView
    {
        private RectTransform _lordHost;
        private RectTransform _dibsHost;
        // Last-rendered fingerprints for the skip of unchanged rebuilds — same reason
        // as the hand's _handSignature: rebuilding a hovered fan drops the raise.
        private string _dibsSignature;
        private string _lordSignature;

        /// <summary>While true, the Whiniest Baby card stays OUT of the lord fan even
        /// though the view says we hold it — set for the span of the claim reveal's
        /// flight, lifted when the card lands (see LemonadeWarsApp).</summary>
        public bool SuppressWhiniestBaby;
        /// <summary>Spoiled Rotten's twin of SuppressWhiniestBaby.</summary>
        public bool SuppressSpoiledRotten;
        /// <summary>Whether the rendered fan currently includes the baby card — the
        /// Spoiled Rotten flight lands one slot further right when it does.</summary>
        private bool _lordFanHasBaby;

        /// <summary>
        /// First Dibs titles: overlapped peek cards, bottom-left. Unclaimed titles from
        /// the row first, then claimed ones wearing their claimant's name chip.
        /// </summary>
        private void RenderFirstDibs(PlayerView view)
        {
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

            // Same skip as the hand: don't tear down a hovered fan over a re-render
            // that changed nothing here.
            string signature = Mathf.RoundToInt(_dibsHost.rect.width) + "|" +
                string.Join(",", entries.Select(e => e.TitleId + ":" + e.ClaimedBy));
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
                        TextAnchor.MiddleCenter, UiKit.ButtonTextColor, body: true);
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

        /// <summary>
        /// Your two secret Lemon Lord titles: overlapped peek cards, bottom-right.
        /// The Whiniest Baby card joins on the LEFT while you hold it — a card in your
        /// zone beats a line of status text for "you are the baby".
        /// </summary>
        private void RenderLords(PlayerView view)
        {
            // Suppressed while a claim reveal is mid-flight: the table renders the
            // instant the state changes, but the card should only exist in the fan
            // once the animation has delivered it (the app lifts this on arrival).
            bool baby = view.WhiniestBabyHolder == view.ViewerId && !SuppressWhiniestBaby;
            bool rotten = view.SpoiledRottenHolder == view.ViewerId && !SuppressSpoiledRotten;
            _lordFanHasBaby = baby;

            // Same skip as the hand: don't tear down a hovered fan over a re-render
            // that changed nothing here.
            string signature = (baby ? "baby|" : "|") + (rotten ? "rotten|" : "|") +
                string.Join(",", view.LemonLordStatus.Select(l => l.TitleId + ":" + l.Met));
            if (signature == _lordSignature)
            {
                return;
            }
            _lordSignature = signature;

            UiKit.Clear(_lordHost);
            // Special titles first (left), the secret lords after: (art, lord-or-null).
            var cards = new List<(Texture2D Texture, PlayerView.LordStatus Lord)>();
            if (baby)
            {
                cards.Add((_art.WhiniestBaby(), null));
            }
            if (rotten)
            {
                cards.Add((_art.SpoiledRotten(), null));
            }
            cards.AddRange(view.LemonLordStatus.Select(l => (_art.Title(l.TitleId), l)));
            int count = cards.Count;
            if (count == 0)
            {
                return;
            }

            const float width = 190f;
            const float height = 266f;
            const float raisedY = 12f;
            const float peek = 158f;
            float spacing = LordSpacing(count);
            float startX = -(width + spacing * (count - 1)) / 2f + width / 2f;

            for (int i = 0; i < count; i++)
            {
                var (texture, lord) = cards[i];

                var image = UiKit.CreateCardImage(_lordHost, texture, width, height);
                var frame = (RectTransform)image.transform.parent;
                frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
                frame.pivot = new Vector2(0.5f, 0f);
                frame.sizeDelta = new Vector2(width, height);
                float restY = peek - height;
                frame.anchoredPosition = new Vector2(startX + i * spacing, restY);
                var motion = frame.gameObject.AddComponent<HandCardMotion>();
                motion.TargetY = restY;

                if (lord != null && lord.Met)
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

        /// <summary>
        /// Card overlap in the lord fan: the usual hand overlap while the zone has
        /// room, compressing (like First Dibs) once a third card — the Whiniest Baby —
        /// would spill past the screen edge. The margin keeps a sliver of table
        /// visible on the right.
        /// </summary>
        private float LordSpacing(int count)
        {
            const float width = 190f;
            const float margin = 24f;
            float available = _lordHost.rect.width - margin;
            if (available < 10f)
            {
                available = 380f; // first-frame fallback before canvas layout settles
            }
            return count > 1
                ? Mathf.Min(width * 0.72f, (available - width) / (count - 1))
                : 0f;
        }

        /// <summary>The baby joins the fan LEFT-MOST.</summary>
        public Vector3 WhiniestBabyLandingWorld() => LordLandingWorld(0);

        /// <summary>Spoiled Rotten lands after the baby when the baby is in the fan.</summary>
        public Vector3 SpoiledRottenLandingWorld() => LordLandingWorld(_lordFanHasBaby ? 1 : 0);

        /// <summary>
        /// World position of a slot in the lord fan as it will be AFTER the flying
        /// card is added (the fan hides an in-flight card, so the current children
        /// don't include it yet). Mirrors RenderLords' layout math.
        /// </summary>
        private Vector3 LordLandingWorld(int slot)
        {
            const float width = 190f;
            const float height = 266f;
            const float peek = 158f;
            int futureCount = _lordHost.childCount + 1;
            float spacing = LordSpacing(futureCount);
            float startX = -(width + spacing * (futureCount - 1)) / 2f + width / 2f;
            // Children hang from the host's bottom-center pivot; card center is half a
            // card above its bottom-anchored rest position.
            return _lordHost.TransformPoint(
                new Vector3(startX + slot * spacing, peek - height + height / 2f, 0));
        }
    }
}
