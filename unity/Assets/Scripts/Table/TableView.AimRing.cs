using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The aim ring on a tucked card's peeking strip: That's Not Fair and Finders Keepers
    // can hit an equipped card itself, not just its owner's bar.
    public sealed partial class TableView
    {
        /// <summary>How far an aim ring may show past a tucked strip's sides and top.</summary>
        private const float AimRingReachPx = 28f;
        /// <summary>The ring's rim sits this far outside the card's edge.</summary>
        private const float AimRingOutsetPx = 2f;
        /// <summary>How far below the strip the ring may show: enough for its rounded bottom corners, not its side glow.</summary>
        private const float AimRingCornerReachPx = 8f;

        private void AddAimStrip(RectTransform tuckedCard, int equippedInstanceId)
        {
            var stripGo = new GameObject("AimStrip", typeof(RectTransform));
            stripGo.transform.SetParent(tuckedCard, false);
            var strip = (RectTransform)stripGo.transform;
            UiKit.Anchor(strip, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -TuckPeek), new Vector2(0, 0));
            var glow = AddClippedAimRing(strip);
            _equipCells.Add((equippedInstanceId, strip, glow));
        }

        /// <summary>A full rounded ring clipped just below the strip, so its bottom corners round off
        /// into the card tucked in front and the glow stops at the strip's shoulders.</summary>
        private static GameObject AddClippedAimRing(RectTransform strip)
        {
            var maskGo = new GameObject("AimGlowMask", typeof(RectTransform), typeof(RectMask2D));
            maskGo.transform.SetParent(strip, false);
            var mask = (RectTransform)maskGo.transform;
            UiKit.Anchor(mask, Vector2.zero, Vector2.one,
                new Vector2(-AimRingReachPx, -AimRingCornerReachPx),
                new Vector2(AimRingReachPx, AimRingReachPx));
            // Sized so the rim sits just outside the card's edge and glows outward.
            float rimOut = UiSprites.GlowRingRimInset + AimRingOutsetPx;
            var glow = UiKit.CreateGlow(mask, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -(AimRingReachPx - AimRingCornerReachPx) / 2f),
                BoardCardWidthPx + 2f * rimOut, TuckPeek + 2f * rimOut, AttackGlowColor);
            // A ring, not a fill: a solid glow would paint over the card face being aimed at.
            glow.GetComponent<Image>().sprite = UiSprites.GlowRing;
            glow.SetActive(false);
            return glow;
        }
    }
}
