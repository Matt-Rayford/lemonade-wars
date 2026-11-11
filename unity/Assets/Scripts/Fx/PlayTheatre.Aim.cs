using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class PlayTheatre
    {
        private const float ArrowRefreshThresholdSq = 4f;
        private const float ArrowEdgeGapPx = 10f;
        /// <summary>The arrowhead stops this far outside the victim's avatar rim, aimed at its centre.</summary>
        private const float ArrowTipGapPx = 3f;
        /// <summary>The arrow's last handle sits beside and below (or above) the chip: it climbs into it from the card's side.</summary>
        private const float ArrowApproachFraction = 0.55f;
        /// <summary>How far the arrow's handles reach, as a share of its span: a tall sweep under (or over) the bars, not a loop.</summary>
        private const float ArrowBulge = 0.8f;
        private static readonly Color ArrowColor = new Color(0.95f, 0.27f, 0.21f);

        private Vector2 _drawnTargetEnd;
        private bool _targetArrowDrawn;
        private int _glowingTarget = -1;

        /// <summary>
        /// Re-draws only when the arrow's endpoint moved (bars re-lay out as the game goes),
        /// never every frame: each dash is a GameObject.
        /// </summary>
        private void RedrawArrows(bool force)
        {
            var avatar = _targetId >= 0 ? _avatarWorld(_targetId) : null;
            if (avatar == null)
            {
                return; // a take points at nobody
            }
            Vector2 chipCenter = Local(avatar.Value.Center);
            float chipRadius = (Local(avatar.Value.Rim) - chipCenter).magnitude;
            bool arrowDue = _phase == Phase.Holding;
            if (!force && arrowDue == _targetArrowDrawn &&
                (chipCenter - _drawnTargetEnd).sqrMagnitude < ArrowRefreshThresholdSq)
            {
                return;
            }
            _drawnTargetEnd = chipCenter;
            _targetArrowDrawn = arrowDue;
            UiKit.Clear(_arrowHost);
            if (!arrowDue)
            {
                return;
            }
            DrawArrowToAvatar(chipCenter, chipRadius);
        }

        /// <summary>
        /// Straight up (or down) out of the card's edge facing the victim, a tall sweep under (or over)
        /// the bars, then into their avatar chip from the card's side: one bend, never across the art.
        /// </summary>
        private void DrawArrowToAvatar(Vector2 chipCenter, float chipRadius)
        {
            Vector2 card = _chip.anchoredPosition;
            float up = chipCenter.y >= card.y ? 1f : -1f;
            float side = card.x >= chipCenter.x ? 1f : -1f;
            Vector2 exit = card + new Vector2(0, up * (CardHeight / 2f + ArrowEdgeGapPx));
            float reach = (chipCenter - exit).magnitude * ArrowBulge;
            Vector2 leave = exit + new Vector2(0, up * reach);
            // On the card's side of the chip AND the side the sweep went (below when it dipped): a U, not an S.
            Vector2 approach = chipCenter + new Vector2(side, up).normalized * (reach * ArrowApproachFraction);
            Vector2 tip = chipCenter + (approach - chipCenter).normalized * (chipRadius + ArrowTipGapPx);
            UiKit.DrawCurvedArrow(_arrowHost, exit, leave, approach, tip, ArrowColor);
        }

        /// <summary>Red attack glow on the victim's bar while the card is staged; off on every exit path.</summary>
        private void GlowVictim(bool off = false)
        {
            int wanted = off ? -1 : _targetId;
            if (_glowingTarget >= 0 && _glowingTarget != wanted)
            {
                _victimGlow(_glowingTarget, false);
            }
            if (wanted >= 0)
            {
                _victimGlow(wanted, true);
            }
            _glowingTarget = wanted;
        }
    }
}
