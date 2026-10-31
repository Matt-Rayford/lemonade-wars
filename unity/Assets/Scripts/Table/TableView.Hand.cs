using System.Collections.Generic;
using System.Linq;
using System.Text;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The hand fan: Dune Imperium peek-and-rise cards along the bottom edge, with
    // click-to-play, drag-out-to-aim, drag-sideways-to-reorder, the draw slot, and
    // edge scrolling when the fan outgrows its band.
    public sealed partial class TableView
    {
        public System.Action<int> OnHandCard;      // lemon instance id

        /// <summary>Sentinel hand-frame id for the draw slot — never a real instance.</summary>
        private const int DrawSlotId = int.MinValue;

        private RectTransform _handHost;
        // Hand arrangement: the player's preferred order, kept across renders.
        private readonly List<int> _handOrder = new List<int>();
        // Last-rendered fingerprint for the skip of unchanged rebuilds (see
        // RenderHand): rebuilding a hovered fan drops the raise and the preview.
        private string _handSignature;
        private int _reorderCardId = -1;
        private int _reorderSlot = -1;
        /// <summary>Card held raised after a drop until the pointer actually leaves it.</summary>
        private int _stickyRaisedId = -1;
        /// <summary>Where a card's type sits in the fan: plans left, attacks middle, reactions right.</summary>
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

        /// <summary>How far toward fully raised a playable reaction rests while its window is open.</summary>
        private const float ReactionRestLift = 0.5f;
        private const float ReactionGlowPx = 26f;
        /// <summary>This pointer gesture became a real drag (aim or reorder), not a click.</summary>
        private bool _handGestureArmed;

        /// <summary>
        /// Sideways travel that turns a press into a reorder: 5% of the hand band —
        /// enough to be deliberate, short enough to feel immediate. Anything less
        /// stays a click on the card.
        /// </summary>
        private float ReorderThreshold() =>
            Mathf.Max(20f, _handHost.rect.width * 0.05f);
        private Vector2 _handDragStart;
        private float _handStartX;
        private float _handSpacing;

        // Hand fan scrolling (when the hand outgrows the center band).
        private float _handScroll;
        private float _handMaxScroll;

        private void RenderHand(PlayerView view, MoveGroups groups)
        {
            // The deck sits at the right end of the fan as a card-shaped slot, so
            // drawing reads as taking a card rather than pressing a bar button.
            bool canDraw = CanDrawLemon?.Invoke() == true;

            // While the Whiniest Baby "drew 2, keep 1" overlay is up, the fresh
            // draws live in the OVERLAY only: showing them in the fan underneath
            // reads as cards you already own (a phantom Finders Keepers fooled a
            // real playtest). Hide exactly the decision's eligible instances — the
            // kept card joins the fan when the decision resolves.
            // Whispers of Fate's "drew 2, discard 1 of them" is the same shape.
            var drawnPickDecision = view.MyDecisions.FirstOrDefault(d =>
                (d.Kind == DecisionKind.WhiniestBabyDiscard || d.Kind == DecisionKind.AbilityDiscard) &&
                d.EligibleCardIds != null);
            var hiddenIds = drawnPickDecision?.EligibleCardIds;

            // Plans, then attacks, then reactions, left to right, alphabetical within
            // each group so duplicates sit together — a hand you can read at a glance.
            // The fan keeps this order on its own; a sideways drag only lasts until
            // the next render.
            var hand = view.Hand
                .Where(c => hiddenIds == null || !hiddenIds.Contains(c.InstanceId))
                .OrderBy(c => HandGroupRank(c.DefId))
                .ThenBy(c => _db.Lemon(c.DefId).Name)
                .ThenBy(c => c.InstanceId)
                .ToList();
            int count = hand.Count;

            // Skip the rebuild when nothing the hand shows has changed (bot turns
            // re-render constantly): tearing the fan down mid-hover drops the raised
            // card and kills an open preview for no reason. The signature covers the
            // order, each card's move count (bindings bake it in), the draw slot, the
            // band width the layout was computed against, and the hidden-draw count
            // (so the fan re-renders the moment the Whiniest Baby decision resolves).
            var signatureBuilder = new StringBuilder(canDraw ? "draw|" : "|");
            signatureBuilder.Append("h").Append(hiddenIds?.Count ?? 0).Append('|');
            signatureBuilder.Append(Mathf.RoundToInt(_handHost.rect.width)).Append('|');
            foreach (var card in hand)
            {
                int moveCount = groups?.HandMoves.TryGetValue(card.InstanceId, out var moves) == true
                    ? moves.Count
                    : 0;
                // DefId too: instance ids restart each game, so a fresh game's opening
                // hand could otherwise collide with the old one's and keep stale cards.
                signatureBuilder.Append(card.InstanceId).Append(':').Append(card.DefId)
                    .Append(':').Append(moveCount).Append(',');
            }
            string signature = signatureBuilder.ToString();
            if (signature == _handSignature)
            {
                return;
            }
            _handSignature = signature;

            UiKit.Clear(_handHost);
            _handFrames.Clear();
            if (count == 0 && !canDraw)
            {
                _handOrder.Clear();
                return;
            }
            _handOrder.Clear();
            _handOrder.AddRange(hand.Select(c => c.InstanceId));

            const float width = 190f;
            const float height = 266f;
            const float raisedY = 12f;   // fully visible, floating just off the edge
            const float peek = 172f;     // ~2/3 visible at rest, one even row

            float hostWidth = _handHost.rect.width;
            if (hostWidth < 10f)
            {
                hostWidth = 1075f; // first-frame fallback before canvas layout settles
            }
            // Constant Dune Imperium overlap; a big hand scrolls instead of compressing
            // past readability. The usable width is inset well past the mask's fade
            // zones so the end cards rest comfortably inside the band at either
            // scroll extreme, not flush against its edges.
            const float edgeFade = 130f;
            float usable = hostWidth - edgeFade * 2f;
            float spacing = width * 0.72f;
            // The draw slot takes a place in the fan, so a full hand scrolls to reach it.
            int slots = count + (canDraw ? 1 : 0);
            float span = width + spacing * (slots - 1);
            _handMaxScroll = Mathf.Max(0f, span - usable);
            _handScroll = Mathf.Clamp(_handScroll, 0f, _handMaxScroll);
            float startX = _handMaxScroll > 0f
                ? -hostWidth / 2f + edgeFade + width / 2f
                : -span / 2f + width / 2f;
            _handStartX = startX;
            _handSpacing = spacing;

            for (int i = 0; i < count; i++)
            {
                var card = hand[i];
                List<GameAction> moves = null;
                groups?.HandMoves.TryGetValue(card.InstanceId, out moves);
                int optionCount = moves?.Count ?? 0;
                var texture = _art.Lemon(card.DefId);

                var image = UiKit.CreateCardImage(_handHost, texture, width, height);
                var frame = (RectTransform)image.transform.parent;
                frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
                frame.pivot = new Vector2(0.5f, 0f);
                frame.sizeDelta = new Vector2(width, height);
                float restY = peek - height;
                // A card that can answer the open window rests half-raised and glows:
                // an invitation you can see without a panel, still rising fully on hover.
                if (moves != null && moves.Any(m => m is RespondToWindow))
                {
                    restY += (raisedY - restY) * ReactionRestLift;
                    var glow = UiKit.CreateGlow(frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        Vector2.zero, width + ReactionGlowPx * 2f, height + ReactionGlowPx * 2f,
                        ReactionGlowColor);
                    glow.transform.SetAsFirstSibling(); // behind the art, haloing its edge
                    glow.SetActive(true);
                }
                float baseX = startX + i * spacing;
                frame.anchoredPosition = new Vector2(baseX - _handScroll, restY);
                var motion = frame.gameObject.AddComponent<HandCardMotion>();
                motion.TargetY = restY;
                if (card.InstanceId == _stickyRaisedId)
                {
                    // Just dropped under the cursor: stay lifted through the rebuild,
                    // or the card visibly falls flat and pops back up on the next move.
                    motion.TargetY = raisedY;
                    frame.anchoredPosition = new Vector2(frame.anchoredPosition.x, raisedY);
                    frame.SetAsLastSibling();
                }
                int captured = card.InstanceId;
                _handFrames.Add((captured, frame, baseX, motion, restY));

                // Playing the card: Connections reaches up to the shelf, attacks reach
                // across to a player, everything else opens its menu.
                void Activate()
                {
                    if (optionCount == 0)
                    {
                        return;
                    }
                    ActivateHandCard(captured);
                }

                if (optionCount > 0)
                {
                    UiKit.AddClick(image.gameObject, Activate);
                }

                // Dragging is always available — arranging your hand is not a move, so
                // it works on anyone's turn. Only the ARMING inside needs a legal play.
                var drag = image.gameObject.AddComponent<DragRelay>();
                drag.Began = position =>
                {
                    _attackDragAborted = false;
                    _handDragStart = position;
                    _handGestureArmed = false;
                    _preview.SetDragging(true);
                };
                drag.Moved = position =>
                {
                    if (optionCount > 0 && _attackCardId < 0 && _reorderCardId < 0 &&
                        !_attackDragAborted &&
                        !RectTransformUtility.RectangleContainsScreenPoint(_handHost, position))
                    {
                        var shelf = MarketTargetsFor?.Invoke(captured);
                        if (shelf != null && shelf.Count > 0)
                        {
                            _handGestureArmed = true;
                            BeginAttackTargeting(captured, shelf, dragMode: true,
                                AimTarget.MarketCards);
                            return;
                        }
                        var targets = AttackTargetsFor?.Invoke(captured);
                        if (targets != null && targets.Count > 0)
                        {
                            _handGestureArmed = true;
                            BeginAttackTargeting(captured, targets, dragMode: true);
                        }
                        return;
                    }
                    // Sideways inside the band: rearrange the hand instead. It takes a
                    // deliberate sideways push (a card-width-ish) to become a drag —
                    // below that the gesture is still a click on the card.
                    if (_attackCardId < 0 &&
                        RectTransformUtility.RectangleContainsScreenPoint(_handHost, position))
                    {
                        if (_reorderCardId < 0 &&
                            Mathf.Abs(position.x - _handDragStart.x) > ReorderThreshold())
                        {
                            _reorderCardId = captured;
                            _reorderSlot = -1; // force the first preview
                            _handGestureArmed = true;
                            frame.SetAsLastSibling();
                            motion.TargetY = raisedY;
                        }
                        if (_reorderCardId == captured)
                        {
                            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                                _handHost, position, null, out var local);
                            frame.anchoredPosition = new Vector2(
                                local.x, frame.anchoredPosition.y);
                            // Live gap: the rest of the fan opens up around the
                            // slot the card would land in, so the arrangement is
                            // visible before the drop rather than after it.
                            int slot = SlotAt(local.x);
                            if (slot != _reorderSlot)
                            {
                                _reorderSlot = slot;
                                PreviewReorder(captured, slot);
                            }
                        }
                    }
                };
                drag.Ended = position =>
                {
                    if (_reorderCardId == captured)
                    {
                        _reorderCardId = -1;
                        _reorderSlot = -1;
                        ClearReorderPreview();
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            _handHost, position, null, out var local);
                        int slot = SlotAt(local.x);
                        _handOrder.Remove(captured);
                        _handOrder.Insert(Mathf.Clamp(slot, 0, _handOrder.Count), captured);
                        _stickyRaisedId = captured; // the cursor is still on it
                        OnBoardViewChanged?.Invoke(); // force a re-render in the new order
                    }
                    else if (_attackCardId == captured && _attackDragMode)
                    {
                        FinishAttackTargeting();
                    }
                    else if (!_handGestureArmed)
                    {
                        // A twitch, not a drag. Unity already suppressed the click for
                        // this gesture, so play the card the way a click would.
                        Activate();
                    }
                    _handGestureArmed = false;
                    _preview.SetDragging(false);
                };

                int sibling = i;
                UiKit.AddHover(image.gameObject,
                    () =>
                    {
                        if (_attackCardId >= 0)
                        {
                            return; // the armed card owns the top slot while aiming
                        }
                        frame.SetAsLastSibling();
                        motion.TargetY = raisedY;
                    },
                    () =>
                    {
                        if (_attackCardId == captured)
                        {
                            return; // stays raised while aiming
                        }
                        frame.SetSiblingIndex(sibling);
                        motion.TargetY = restY;
                    });
                _preview.Attach(image.gameObject, texture);
            }

            if (canDraw)
            {
                BuildDrawSlot(startX + count * spacing, width, height, peek, raisedY, count);
            }
        }

        /// <summary>
        /// Arm a hand card exactly as clicking it would: Connections aims at the
        /// shelf, attacks aim at players, everything else opens the card's menu.
        /// Public so modal options ("Play That's Not Fair...") can hand off to the
        /// same flows instead of listing every target as its own button.
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
        /// The deck as a hand slot: a dashed card-shaped outline holding a "+" and the
        /// prompt. Lifts on hover like a real card, but nothing here is draggable — it
        /// is a place to click, not a card to arrange.
        /// </summary>
        private void BuildDrawSlot(float baseX, float width, float height, float peek,
            float raisedY, int sibling)
        {
            var frameGo = new GameObject("DrawSlot", typeof(RectTransform), typeof(Image));
            frameGo.transform.SetParent(_handHost, false);
            var frame = (RectTransform)frameGo.transform;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0f);
            frame.pivot = new Vector2(0.5f, 0f);
            frame.sizeDelta = new Vector2(width, height);
            float restY = peek - height;
            frame.anchoredPosition = new Vector2(baseX - _handScroll, restY);
            // The frame itself is invisible but raycastable: the whole card area clicks.
            frameGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            // Opaque fill running out to the dash ring itself (same rect, same corner
            // radius), so the dashes sit ON its edge rather than around a smaller slab.
            const float fillInset = 3f;
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(frame, false);
            UiKit.Anchor((RectTransform)fillGo.transform, Vector2.zero, Vector2.one,
                new Vector2(fillInset, fillInset), new Vector2(-fillInset, -fillInset));
            var fill = fillGo.GetComponent<Image>();
            fill.sprite = UiSprites.RoundedRect;
            fill.type = Image.Type.Sliced;
            fill.pixelsPerUnitMultiplier =
                14f / Mathf.Max(4f, UiKit.CardCornerRadius(width) - fillInset);
            fill.color = new Color(0.30f, 0.35f, 0.46f, 0.55f);
            fill.raycastTarget = false;

            var motion = frameGo.AddComponent<HandCardMotion>();
            motion.TargetY = restY;
            _handFrames.Add((DrawSlotId, frame, baseX, motion, restY));

            var outline = UiKit.ButtonColor;
            AddDashedRoundedOutline(frame, width - 6f, height - 6f,
                UiKit.CardCornerRadius(width), outline);

            // Stacked as one centered group, sitting in the part of the card that
            // shows while the hand is at rest (the lower third peeks off-screen).
            // Each band must clear its own line height: these texts truncate, and a
            // glyph that cannot fit vertically renders as nothing at all.
            var plus = UiKit.CreateText(frame, "+", 48, TextAnchor.MiddleCenter, outline);
            plus.raycastTarget = false;
            plus.overflowMode = TMPro.TextOverflowModes.Overflow;
            UiKit.Anchor((RectTransform)plus.transform, new Vector2(0, 0.62f), new Vector2(1, 0.88f));

            var label = UiKit.CreateText(frame, "DRAW A\nLEMON CARD", 18,
                TextAnchor.MiddleCenter, outline);
            label.raycastTarget = false;
            label.overflowMode = TMPro.TextOverflowModes.Overflow;
            UiKit.Anchor((RectTransform)label.transform, new Vector2(0, 0.40f), new Vector2(1, 0.62f));

            UiKit.AddHover(frameGo,
                () =>
                {
                    if (_attackCardId >= 0)
                    {
                        return; // the armed card owns the top slot while aiming
                    }
                    frame.SetAsLastSibling();
                    motion.TargetY = raisedY;
                },
                () =>
                {
                    frame.SetSiblingIndex(sibling);
                    motion.TargetY = restY;
                });
            UiKit.AddClick(frameGo, () => OnDrawCard?.Invoke());
        }

        /// <summary>Which slot a local X lands the dragged card in.</summary>
        private int SlotAt(float localX) => _handSpacing > 0f
            ? Mathf.RoundToInt((localX + _handScroll - _handStartX) / _handSpacing)
            : 0;

        /// <summary>Slide every OTHER card to where it would sit if the drop landed now.</summary>
        private void PreviewReorder(int draggedId, int slot)
        {
            var order = new List<int>(_handOrder);
            order.Remove(draggedId);
            order.Insert(Mathf.Clamp(slot, 0, order.Count), draggedId);
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] == draggedId)
                {
                    continue; // that one follows the cursor
                }
                foreach (var entry in _handFrames)
                {
                    if (entry.Id == order[i] && entry.Motion != null)
                    {
                        entry.Motion.TargetX = _handStartX + i * _handSpacing - _handScroll;
                    }
                }
            }
        }

        private void ClearReorderPreview()
        {
            foreach (var entry in _handFrames)
            {
                if (entry.Motion != null)
                {
                    entry.Motion.TargetX = null;
                }
            }
        }

        /// <summary>
        /// Hover-based fan scrolling: with the cursor inside the hand zone, the outer
        /// 20% on each side auto-scrolls that direction, faster nearer the edge.
        /// Call every frame.
        /// </summary>
        public void TickHandScroll(Vector2 screenPosition)
        {
            // Drop the post-drop lift once the pointer really leaves the card. Checked
            // here rather than trusting a pointer-exit: the card the cursor sits on was
            // destroyed and rebuilt mid-gesture, so those events cannot be relied on.
            if (_stickyRaisedId >= 0)
            {
                for (int i = 0; i < _handFrames.Count; i++)
                {
                    var entry = _handFrames[i];
                    if (entry.Id != _stickyRaisedId)
                    {
                        continue;
                    }
                    if (entry.Frame == null || !RectTransformUtility.RectangleContainsScreenPoint(
                            entry.Frame, screenPosition))
                    {
                        if (entry.Motion != null)
                        {
                            entry.Motion.TargetY = entry.RestY;
                        }
                        entry.Frame?.SetSiblingIndex(i);
                        _stickyRaisedId = -1;
                    }
                    break;
                }
            }
            if (_reorderCardId >= 0)
            {
                return; // a reorder owns the fan's X positions right now
            }
            if (_handMaxScroll <= 0f ||
                !RectTransformUtility.RectangleContainsScreenPoint(_handHost, screenPosition))
            {
                return;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _handHost, screenPosition, null, out var local);
            var rect = _handHost.rect;
            float fraction = (local.x - rect.xMin) / Mathf.Max(1f, rect.width);

            const float zone = 0.2f;
            float velocity = 0f;
            if (fraction < zone)
            {
                velocity = -Mathf.InverseLerp(zone, 0f, fraction);
            }
            else if (fraction > 1f - zone)
            {
                velocity = Mathf.InverseLerp(1f - zone, 1f, fraction);
            }
            if (velocity == 0f)
            {
                return;
            }

            _handScroll = Mathf.Clamp(
                _handScroll + velocity * 900f * Time.deltaTime, 0f, _handMaxScroll);
            foreach (var entry in _handFrames)
            {
                if (entry.Frame != null)
                {
                    var position = entry.Frame.anchoredPosition;
                    position.x = entry.BaseX - _handScroll;
                    entry.Frame.anchoredPosition = position;
                }
            }
        }
    }
}
