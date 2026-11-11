using System.Collections.Generic;
using UnityEngine;

namespace LemonadeWars.Unity
{
    // Hand gestures: a press on a card plays it, a drag out of the band aims it, a
    // sideways drag rearranges the fan, and the band's outer edges scroll a wide hand.
    public sealed partial class TableView
    {
        private const float HandEdgeScrollZone = 0.2f;
        private const float HandEdgeScrollPxPerSecond = 900f;

        private int _reorderCardId = -1;
        private int _reorderSlot = -1;
        /// <summary>Card held raised after a reorder drop until the pointer leaves it; -1 = none.</summary>
        private int _raisedAfterDropCardId = -1;
        /// <summary>This pointer gesture turned into an aim or a reorder, so its release is not a click.</summary>
        private bool _handGestureBecameDrag;
        private Vector2 _handDragStart;

        /// <summary>Sideways travel that turns a press into a reorder: 5% of the hand band, at least 20px.</summary>
        private float ReorderThresholdPx() =>
            Mathf.Max(20f, _handHost.rect.width * 0.05f);

        /// <summary>Arranging your hand is not a move, so dragging works on anyone's turn; only arming an aim needs a legal play.</summary>
        private void WireHandCardDrag(GameObject cardFace, int cardId, bool isPlayable,
            RectTransform frame, HandCardMotion motion)
        {
            var drag = cardFace.AddComponent<DragRelay>();
            drag.Began = BeginHandCardDrag;
            drag.Moved = position => MoveHandCardDrag(cardId, isPlayable, frame, motion, position);
            drag.Ended = position => EndHandCardDrag(cardId, isPlayable, position);
        }

        private void BeginHandCardDrag(Vector2 position)
        {
            _attackDragAborted = false;
            _handDragStart = position;
            _handGestureBecameDrag = false;
            _preview.SetDragging(true);
        }

        private void MoveHandCardDrag(int cardId, bool isPlayable, RectTransform frame,
            HandCardMotion motion, Vector2 position)
        {
            bool isInsideHandBand =
                RectTransformUtility.RectangleContainsScreenPoint(_handHost, position);
            if (isPlayable && _attackCardId < 0 && _reorderCardId < 0 &&
                !_attackDragAborted && !isInsideHandBand)
            {
                ArmAimFromHandDrag(cardId);
                return;
            }
            if (_attackCardId < 0 && isInsideHandBand)
            {
                DragHandCardSideways(cardId, frame, motion, position);
            }
        }

        private void ArmAimFromHandDrag(int cardId)
        {
            var shelf = MarketTargetsFor?.Invoke(cardId);
            if (shelf != null && shelf.Count > 0)
            {
                _handGestureBecameDrag = true;
                BeginAttackTargeting(cardId, shelf, dragMode: true,
                    AimTarget.MarketCards);
                return;
            }
            var targets = AttackTargetsFor?.Invoke(cardId);
            if (targets != null && targets.Count > 0)
            {
                _handGestureBecameDrag = true;
                BeginAttackTargeting(cardId, targets, dragMode: true);
            }
        }

        private void DragHandCardSideways(int cardId, RectTransform frame, HandCardMotion motion,
            Vector2 position)
        {
            if (_reorderCardId < 0 &&
                Mathf.Abs(position.x - _handDragStart.x) > ReorderThresholdPx())
            {
                _reorderCardId = cardId;
                _reorderSlot = -1; // force the first preview
                _handGestureBecameDrag = true;
                frame.SetAsLastSibling();
                motion.TargetY = HandRaisedY;
            }
            if (_reorderCardId == cardId)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _handHost, position, null, out var local);
                frame.anchoredPosition = new Vector2(
                    local.x, frame.anchoredPosition.y);
                int slot = SlotAt(local.x);
                if (slot != _reorderSlot)
                {
                    _reorderSlot = slot;
                    PreviewReorder(cardId, slot);
                }
            }
        }

        private void EndHandCardDrag(int cardId, bool isPlayable, Vector2 position)
        {
            if (_reorderCardId == cardId)
            {
                DropReorderedHandCard(cardId, position);
            }
            else if (_attackCardId == cardId && _attackDragMode)
            {
                FinishAttackTargeting();
            }
            else if (!_handGestureBecameDrag && isPlayable)
            {
                // Unity suppressed the click for this twitch of a drag: play the card as a click would.
                ActivateHandCard(cardId);
            }
            _handGestureBecameDrag = false;
            _preview.SetDragging(false);
        }

        private void DropReorderedHandCard(int cardId, Vector2 position)
        {
            _reorderCardId = -1;
            _reorderSlot = -1;
            ClearReorderPreview();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _handHost, position, null, out var local);
            int slot = SlotAt(local.x);
            _handOrder.Remove(cardId);
            _handOrder.Insert(Mathf.Clamp(slot, 0, _handOrder.Count), cardId);
            _raisedAfterDropCardId = cardId; // the cursor is still on it
            OnBoardViewChanged?.Invoke(); // force a re-render in the new order
        }

        private void WireHandCardHover(GameObject hoverSurface, int cardId, RectTransform frame,
            HandCardMotion motion, int restingSiblingIndex, float restY)
        {
            UiKit.AddHover(hoverSurface,
                () =>
                {
                    if (_attackCardId >= 0)
                    {
                        return; // the armed card owns the top slot while aiming
                    }
                    frame.SetAsLastSibling();
                    motion.TargetY = HandRaisedY;
                },
                () =>
                {
                    if (_attackCardId == cardId)
                    {
                        return; // stays raised while aiming
                    }
                    frame.SetSiblingIndex(restingSiblingIndex);
                    motion.TargetY = restY;
                });
        }

        private int SlotAt(float localX) => _handSpacing > 0f
            ? Mathf.RoundToInt((localX + _handScroll - _handStartX) / _handSpacing)
            : 0;

        /// <summary>Slides every other card to where it would sit if the drop landed now.</summary>
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
        /// Call every frame. With the cursor in the hand band, its outer 20% on each side
        /// scrolls the fan that way, faster nearer the edge.
        /// </summary>
        public void TickHandScroll(Vector2 screenPosition)
        {
            LowerDroppedCardOncePointerLeaves(screenPosition);
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
            float velocity = HandEdgeScrollVelocity((local.x - rect.xMin) / Mathf.Max(1f, rect.width));
            if (velocity == 0f)
            {
                return;
            }

            _handScroll = Mathf.Clamp(
                _handScroll + velocity * HandEdgeScrollPxPerSecond * Time.deltaTime, 0f, _handMaxScroll);
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

        /// <summary>
        /// Polled, not left to pointer-exit: the card under the cursor was destroyed and
        /// rebuilt mid-gesture, so that event cannot be relied on.
        /// </summary>
        private void LowerDroppedCardOncePointerLeaves(Vector2 screenPosition)
        {
            if (_raisedAfterDropCardId < 0)
            {
                return;
            }
            for (int i = 0; i < _handFrames.Count; i++)
            {
                var entry = _handFrames[i];
                if (entry.Id != _raisedAfterDropCardId)
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
                    _raisedAfterDropCardId = -1;
                }
                break;
            }
        }

        /// <summary>-1..1 from where the pointer sits across the band (0..1); 0 outside the edge zones.</summary>
        private static float HandEdgeScrollVelocity(float fractionAcrossBand)
        {
            if (fractionAcrossBand < HandEdgeScrollZone)
            {
                return -Mathf.InverseLerp(HandEdgeScrollZone, 0f, fractionAcrossBand);
            }
            if (fractionAcrossBand > 1f - HandEdgeScrollZone)
            {
                return Mathf.InverseLerp(1f - HandEdgeScrollZone, 1f, fractionAcrossBand);
            }
            return 0f;
        }
    }
}
