using System.Collections.Generic;
using System.Linq;
using System.Text;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The hand fan along the bottom edge: cards peek at rest and rise on hover, a hand
    // too wide for its band scrolls, and the deck sits at the right end as a draw slot.
    public sealed partial class TableView
    {
        /// <summary>A hand card staged on the table before it is played (Rummage Sale): the fan leaves it out.</summary>
        public int? StagedHandCardId;
        /// <summary>True for a card still in flight from the pile: it holds its fan slot, drawn invisible.</summary>
        public System.Func<int, bool> HandCardInvisible;

        public System.Action<int> OnHandCard;      // lemon instance id

        private const float HandCardWidthPx = 190f;
        private const float HandCardHeightPx = 266f;
        /// <summary>Anchored Y of a raised card: fully visible, floating just off the edge.</summary>
        private const float HandRaisedY = 12f;
        /// <summary>Card height showing at rest, about two thirds of it.</summary>
        private const float HandPeekPx = 172f;
        private const float HandRestY = HandPeekPx - HandCardHeightPx;
        /// <summary>The fan stops this far inside each end of the band, clear of the mask's fade zones.</summary>
        private const float HandEdgeFadePx = 130f;
        /// <summary>How far toward fully raised a playable reaction rests while its window is open.</summary>
        private const float ReactionRestLift = 0.5f;
        private const float ReactionGlowPx = 26f;
        /// <summary>Hand-frame id of the draw slot; never a real instance.</summary>
        private const int DrawSlotId = int.MinValue;

        private RectTransform _handHost;
        /// <summary>What the fan last showed: an unchanged hand is not rebuilt (see RenderHand).</summary>
        private string _handSignature;
        private float _handStartX;
        private float _handSpacing;
        private float _handScroll;
        private float _handMaxScroll;

        private void RenderHand(PlayerView view, MoveGroups groups)
        {
            bool canDraw = CanDrawLemon?.Invoke() == true;
            var hiddenIds = DrawsStillBeingChosen(view);
            var hand = HandInFanOrder(view, hiddenIds);

            // Bot turns re-render constantly, and rebuilding an unchanged fan mid-hover
            // drops the raised card and its open preview.
            string signature = HandSignature(hand, groups, canDraw, hiddenIds?.Count ?? 0);
            if (signature == _handSignature)
            {
                return;
            }
            _handSignature = signature;

            UiKit.Clear(_handHost);
            _handFrames.Clear();
            if (hand.Count == 0 && !canDraw)
            {
                return;
            }

            LayOutHandFan(slotCount: hand.Count + (canDraw ? 1 : 0));
            for (int i = 0; i < hand.Count; i++)
            {
                BuildHandCard(hand[i], i, groups);
            }
            if (canDraw)
            {
                BuildDrawSlot(_handStartX + hand.Count * _handSpacing, hand.Count);
            }
        }

        /// <summary>
        /// Fresh draws still being chosen between in a "drew 2, keep 1" overlay (Whiniest
        /// Baby, Whispers of Fate): shown in the fan too, they read as cards you already own.
        /// </summary>
        private static List<int> DrawsStillBeingChosen(PlayerView view)
        {
            var drawnPickDecision = view.MyDecisions.FirstOrDefault(decision =>
                (decision.Kind == DecisionKind.WhiniestBabyDiscard ||
                 decision.Kind == DecisionKind.AbilityDiscard) &&
                decision.EligibleCardIds != null);
            return drawnPickDecision?.EligibleCardIds;
        }

        /// <summary>Plans, attacks, then reactions, by name within each so duplicates sit together.</summary>
        private List<PlayerView.CardInfo> HandInFanOrder(PlayerView view, List<int> hiddenIds) =>
            view.Hand
                .Where(card => hiddenIds == null || !hiddenIds.Contains(card.InstanceId))
                .Where(card => card.InstanceId != StagedHandCardId)
                .OrderBy(card => HandGroupRank(card.DefId))
                .ThenBy(card => _db.Lemon(card.DefId).Name)
                .ThenBy(card => card.InstanceId)
                .ToList();

        private int HandGroupRank(string defId)
        {
            switch (_db.Lemon(defId).Type)
            {
                case LemonCardType.Plan: return 0;
                case LemonCardType.Attack: return 1;
                case LemonCardType.Instant: return 2;
                default: return 3;
            }
        }

        /// <summary>
        /// Everything the fan shows: draw slot, hidden and staged cards, band width, and each
        /// card's identity, move count (bindings bake it in) and in-flight state.
        /// </summary>
        private string HandSignature(List<PlayerView.CardInfo> hand, MoveGroups groups,
            bool canDraw, int hiddenCount)
        {
            var signatureBuilder = new StringBuilder(canDraw ? "draw|" : "|");
            signatureBuilder.Append("h").Append(hiddenCount).Append('|');
            signatureBuilder.Append("s").Append(StagedHandCardId ?? -1).Append('|');
            signatureBuilder.Append(Mathf.RoundToInt(_handHost.rect.width)).Append('|');
            foreach (var card in hand)
            {
                int moveCount = groups?.HandMoves.TryGetValue(card.InstanceId, out var moves) == true
                    ? moves.Count
                    : 0;
                // DefId too: instance ids restart each game, so a new game's opening hand
                // could otherwise match the old one's.
                signatureBuilder.Append(card.InstanceId).Append(':').Append(card.DefId)
                    .Append(':').Append(moveCount)
                    .Append(HandCardInvisible?.Invoke(card.InstanceId) == true ? ":i," : ",");
            }
            return signatureBuilder.ToString();
        }

        /// <summary>Constant overlap: a big hand scrolls instead of compressing past readability.</summary>
        private void LayOutHandFan(int slotCount)
        {
            float hostWidth = _handHost.rect.width;
            if (hostWidth < 10f)
            {
                hostWidth = 1075f; // first-frame fallback before canvas layout settles
            }
            float usable = hostWidth - HandEdgeFadePx * 2f;
            float spacing = HandCardWidthPx * 0.72f;
            float span = HandCardWidthPx + spacing * (slotCount - 1);
            _handMaxScroll = Mathf.Max(0f, span - usable);
            _handScroll = Mathf.Clamp(_handScroll, 0f, _handMaxScroll);
            _handStartX = _handMaxScroll > 0f
                ? -hostWidth / 2f + HandEdgeFadePx + HandCardWidthPx / 2f
                : -span / 2f + HandCardWidthPx / 2f;
            _handSpacing = spacing;
        }

        private void BuildHandCard(PlayerView.CardInfo card, int fanIndex, MoveGroups groups)
        {
            List<GameAction> moves = null;
            groups?.HandMoves.TryGetValue(card.InstanceId, out moves);
            bool isPlayable = (moves?.Count ?? 0) > 0;
            var texture = _art.Lemon(card.DefId);

            var image = UiKit.CreateCardImage(_handHost, texture, HandCardWidthPx, HandCardHeightPx);
            var frame = (RectTransform)image.transform.parent;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
            frame.pivot = new Vector2(0.5f, 0f);
            frame.sizeDelta = new Vector2(HandCardWidthPx, HandCardHeightPx);
            float restY = HandRestY;
            if (HandCardInvisible?.Invoke(card.InstanceId) == true)
            {
                image.color = new Color(1f, 1f, 1f, 0f); // its flight is the card for now
            }
            else if (AnswersOpenWindow(moves, groups))
            {
                restY += (HandRaisedY - restY) * ReactionRestLift;
                AddReactionGlow(frame);
            }
            float baseX = _handStartX + fanIndex * _handSpacing;
            frame.anchoredPosition = new Vector2(baseX - _handScroll, restY);
            var motion = frame.gameObject.AddComponent<HandCardMotion>();
            motion.TargetY = restY;
            int cardId = card.InstanceId;
            _handFrames.Add((cardId, frame, baseX, motion, restY));

            if (isPlayable)
            {
                UiKit.AddClick(image.gameObject, () => ActivateHandCard(cardId));
            }
            WireHandCardDrag(image.gameObject, cardId, isPlayable);
            WireHandCardHover(image.gameObject, cardId, frame, motion, fanIndex, restY);
            _preview.Attach(image.gameObject, texture);
        }

        /// <summary>A reaction, or an attack a Bouncer lets you strike back with: it rests half-raised and glows.</summary>
        private static bool AnswersOpenWindow(List<GameAction> moves, MoveGroups groups) =>
            moves != null &&
            (moves.Any(move => move is RespondToWindow) ||
             (groups != null && groups.IsModal && moves.Any(move => move is PlayLemonCard)));

        private static void AddReactionGlow(RectTransform frame)
        {
            var glow = UiKit.CreateGlow(frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, HandCardWidthPx + ReactionGlowPx * 2f, HandCardHeightPx + ReactionGlowPx * 2f,
                ReactionGlowColor);
            glow.transform.SetAsFirstSibling(); // behind the art, haloing its edge
            glow.SetActive(true);
        }

        /// <summary>
        /// Plays a hand card as a click does: Connections aims at the shelf, attacks aim at
        /// players, everything else opens the card's menu. Modal options hand off to this too.
        /// </summary>
        public void ActivateHandCard(int cardInstanceId)
        {
            var shelf = MarketTargetsFor?.Invoke(cardInstanceId);
            if (shelf != null && shelf.Count > 0)
            {
                BeginAttackTargeting(cardInstanceId, shelf, dragMode: false, AimTarget.MarketCards);
                return;
            }
            var targets = AttackTargetsFor?.Invoke(cardInstanceId);
            if (targets != null && targets.Count > 0)
            {
                BeginAttackTargeting(cardInstanceId, targets, dragMode: false);
            }
            else
            {
                OnHandCard?.Invoke(cardInstanceId);
            }
        }

        /// <summary>
        /// The deck as a hand slot, so drawing reads as taking a card: a dashed card outline
        /// that lifts on hover and clicks, but cannot be dragged.
        /// </summary>
        private void BuildDrawSlot(float baseX, int restingSiblingIndex)
        {
            var frameGo = new GameObject("DrawSlot", typeof(RectTransform), typeof(Image));
            frameGo.transform.SetParent(_handHost, false);
            var frame = (RectTransform)frameGo.transform;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
            frame.pivot = new Vector2(0.5f, 0f);
            frame.sizeDelta = new Vector2(HandCardWidthPx, HandCardHeightPx);
            frame.anchoredPosition = new Vector2(baseX - _handScroll, HandRestY);
            // Invisible but raycastable: the whole card area clicks.
            frameGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            BuildDrawSlotFill(frame);

            var motion = frameGo.AddComponent<HandCardMotion>();
            motion.TargetY = HandRestY;
            _handFrames.Add((DrawSlotId, frame, baseX, motion, HandRestY));

            var outline = UiKit.ButtonColor;
            AddDashedRoundedOutline(frame, HandCardWidthPx - 6f, HandCardHeightPx - 6f,
                UiKit.CardCornerRadius(HandCardWidthPx), outline);
            BuildDrawSlotPrompt(frame, outline);

            WireHandCardHover(frameGo, DrawSlotId, frame, motion, restingSiblingIndex, HandRestY);
            UiKit.AddClick(frameGo, () => OnDrawCard?.Invoke());
        }

        /// <summary>Runs out to the dash ring (same rect, same corner radius), so the dashes sit on its edge.</summary>
        private static void BuildDrawSlotFill(RectTransform frame)
        {
            const float fillInset = 3f;
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(frame, false);
            UiKit.Anchor((RectTransform)fillGo.transform, Vector2.zero, Vector2.one,
                new Vector2(fillInset, fillInset), new Vector2(-fillInset, -fillInset));
            var fill = fillGo.GetComponent<Image>();
            fill.sprite = UiSprites.RoundedRect;
            fill.type = Image.Type.Sliced;
            fill.pixelsPerUnitMultiplier =
                14f / Mathf.Max(4f, UiKit.CardCornerRadius(HandCardWidthPx) - fillInset);
            fill.color = new Color(0.30f, 0.35f, 0.46f, 0.55f);
            fill.raycastTarget = false;
        }

        /// <summary>
        /// Sits in the part of the card that shows at rest. Each band must clear its text's
        /// line height: a glyph that cannot fit vertically renders as nothing at all.
        /// </summary>
        private static void BuildDrawSlotPrompt(RectTransform frame, Color color)
        {
            var plus = UiKit.CreateText(frame, "+", 48, TextAnchor.MiddleCenter, color);
            plus.raycastTarget = false;
            plus.overflowMode = TMPro.TextOverflowModes.Overflow;
            UiKit.Anchor((RectTransform)plus.transform, new Vector2(0, 0.62f), new Vector2(1, 0.88f));

            var label = UiKit.CreateText(frame, "DRAW A\nLEMON CARD", 18,
                TextAnchor.MiddleCenter, color);
            label.raycastTarget = false;
            label.overflowMode = TMPro.TextOverflowModes.Overflow;
            UiKit.Anchor((RectTransform)label.transform, new Vector2(0, 0.40f), new Vector2(1, 0.62f));
        }

        /// <summary>A hand card's frame centre in world space and its size: where a drawn card's flight lands.</summary>
        public (Vector3 Center, float Width, float Height)? HandCardPlacement(int instanceId)
        {
            foreach (var entry in _handFrames)
            {
                if (entry.Id == instanceId && entry.Frame != null)
                {
                    var frame = entry.Frame;
                    return (frame.TransformPoint(frame.rect.center), frame.rect.width, frame.rect.height);
                }
            }
            return null;
        }
    }
}
