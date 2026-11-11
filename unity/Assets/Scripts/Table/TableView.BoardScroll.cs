using UnityEngine;

namespace LemonadeWars.Unity
{
    // Edge scrolling for a board too wide for its band (six or more stands), like the hand's.
    public sealed partial class TableView
    {
        private const int BoardFadePx = 70;
        /// <summary>The share of the band's width, at each end, where hovering scrolls the row.</summary>
        private const float BoardScrollEdgeFraction = 0.15f;
        private const float BoardScrollPxPerSecond = 700f;

        private float _boardScroll;

        public void TickBoardScroll(Vector2 screenPosition)
        {
            if (_boardPanel == null || _boardRow == null)
            {
                return;
            }
            float overflow = _boardRow.rect.width + 12f - _boardPanel.rect.width;
            FadeBoardEdges(overflow > 0f);
            if (overflow <= 0f)
            {
                if (_boardScroll != 0f)
                {
                    _boardScroll = 0f;
                    ApplyBoardScroll();
                }
                return;
            }
            _boardScroll = Mathf.Clamp(_boardScroll, 0f, overflow);
            if (!RectTransformUtility.RectangleContainsScreenPoint(_boardPanel, screenPosition))
            {
                return;
            }
            float velocity = BoardEdgeScrollVelocity(screenPosition);
            if (velocity == 0f)
            {
                return;
            }
            _boardScroll = Mathf.Clamp(
                _boardScroll + velocity * BoardScrollPxPerSecond * Time.deltaTime, 0f, overflow);
            ApplyBoardScroll();
        }

        /// <summary>The faded edge is the "this scrolls" cue, so it shows only while the row overflows.</summary>
        private void FadeBoardEdges(bool overflowing)
        {
            var softness = overflowing ? new Vector2Int(BoardFadePx, 0) : Vector2Int.zero;
            if (_boardMask.softness != softness)
            {
                _boardMask.softness = softness;
            }
            // Until the row has moved nothing needs clipping on the left, so the clip edge is pushed
            // out past the fade: the Victory Points outline stays crisp at rest.
            var padding = new Vector4(_boardScroll > 0f ? 0f : -BoardFadePx, 0f, 0f, 0f);
            if (_boardMask.padding != padding)
            {
                _boardMask.padding = padding;
            }
        }

        /// <summary>-1..1: how hard the pointer pushes the row, from its depth into either edge zone.</summary>
        private float BoardEdgeScrollVelocity(Vector2 screenPosition)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _boardPanel, screenPosition, null, out var local);
            var rect = _boardPanel.rect;
            float fraction = (local.x - rect.xMin) / Mathf.Max(1f, rect.width);
            if (fraction < BoardScrollEdgeFraction)
            {
                return -Mathf.InverseLerp(BoardScrollEdgeFraction, 0f, fraction);
            }
            if (fraction > 1f - BoardScrollEdgeFraction)
            {
                return Mathf.InverseLerp(1f - BoardScrollEdgeFraction, 1f, fraction);
            }
            return 0f;
        }

        private void ApplyBoardScroll()
        {
            var position = _boardRow.anchoredPosition;
            position.x = 6f - _boardScroll;
            _boardRow.anchoredPosition = position;
        }
    }
}
