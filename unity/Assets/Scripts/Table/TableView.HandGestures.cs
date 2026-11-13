using UnityEngine;

namespace LemonadeWars.Unity
{
    // Hand gestures: a press on a card plays it, a drag out of the band aims it, and the
    // band's outer edges scroll a wide hand.
    public sealed partial class TableView
    {
        private const float HandEdgeScrollZone = 0.2f;
        private const float HandEdgeScrollPxPerSecond = 900f;

        /// <summary>This pointer gesture became an aim or wandered off its card, so its release is not a click.</summary>
        private bool _handGestureBecameDrag;
        private Vector2 _handDragStart;

        /// <summary>Sideways travel beyond which a press is no longer a click: 5% of the hand band, at least 20px.</summary>
        private float HandClickSlopPx() =>
            Mathf.Max(20f, _handHost.rect.width * 0.05f);

        /// <summary>On every card, playable or not, so any drag hides the hover preview; only arming an aim needs a legal play.</summary>
        private void WireHandCardDrag(GameObject cardFace, int cardId, bool isPlayable)
        {
            var drag = cardFace.AddComponent<DragRelay>();
            drag.CanStartDrag = () => isPlayable && _attackCardId < 0 && CanAimFromHand(cardId);
            drag.Began = BeginHandCardDrag;
            drag.Moved = position => MoveHandCardDrag(cardId, isPlayable, position);
            drag.Ended = _ => EndHandCardDrag(cardId, isPlayable);
        }

        /// <summary>Dragging a hand card off the fan only does something for cards that aim: at a player, or at the shelf.</summary>
        private bool CanAimFromHand(int cardId) =>
            (MarketTargetsFor?.Invoke(cardId)?.Count ?? 0) > 0 ||
            (AttackTargetsFor?.Invoke(cardId)?.Count ?? 0) > 0;

        private void BeginHandCardDrag(Vector2 position)
        {
            _attackDragAborted = false;
            _handDragStart = position;
            _handGestureBecameDrag = false;
            _preview.SetDragging(true);
        }

        private void MoveHandCardDrag(int cardId, bool isPlayable, Vector2 position)
        {
            if (_attackCardId >= 0)
            {
                return;
            }
            if (RectTransformUtility.RectangleContainsScreenPoint(_handHost, position))
            {
                if (Mathf.Abs(position.x - _handDragStart.x) > HandClickSlopPx())
                {
                    _handGestureBecameDrag = true;
                }
            }
            else if (isPlayable && !_attackDragAborted)
            {
                ArmAimFromHandDrag(cardId);
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

        private void EndHandCardDrag(int cardId, bool isPlayable)
        {
            if (_attackCardId == cardId && _attackDragMode)
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

        /// <summary>
        /// Call every frame. With the cursor in the hand band, its outer 20% on each side
        /// scrolls the fan that way, faster nearer the edge.
        /// </summary>
        public void TickHandScroll(Vector2 screenPosition)
        {
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
