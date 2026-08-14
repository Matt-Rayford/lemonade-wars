using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Dashed-outline drawing shared by the hand's draw slot and the board's empty
    // VP placeholder.
    public sealed partial class TableView
    {
        /// <summary>
        /// Dashed outline matching the cards' rounded-corner geometry: dashes walk the
        /// full rounded-rect path (corners included) at an even spacing computed to fit
        /// the perimeter exactly, each rotated to its local tangent.
        /// </summary>
        private static void AddDashedRoundedOutline(RectTransform host, float width,
            float height, float radius, Color color)
        {
            const float thickness = 3f;
            const float dashLength = 13f;
            const float targetSpacing = 24f; // dash + gap, before fitting

            // Straight-edge lengths on the dash centerline.
            float straightW = width - 2f * radius;
            float straightH = height - 2f * radius;
            float arc = radius * Mathf.PI / 2f;
            float perimeter = 2f * (straightW + straightH) + 4f * arc;
            int count = Mathf.Max(8, Mathf.RoundToInt(perimeter / targetSpacing));
            float step = perimeter / count;

            for (int i = 0; i < count; i++)
            {
                var (position, angle) = PointOnRoundedRect(i * step, straightW, straightH, radius);
                AddDash(host, position, new Vector2(dashLength, thickness), color, angle);
            }
        }

        /// <summary>Position + tangent angle at the given distance along a clockwise rounded
        /// rect (starting at the top edge's left end), centered on the origin.</summary>
        private static (Vector2 Position, float Angle) PointOnRoundedRect(
            float distance, float straightW, float straightH, float radius)
        {
            float arc = radius * Mathf.PI / 2f;
            float hw = straightW / 2f;
            float hh = straightH / 2f;

            (Vector2, float) OnArc(Vector2 center, float startDegrees, float t)
            {
                float theta = startDegrees - 90f * t;
                var direction = new Vector2(
                    Mathf.Cos(theta * Mathf.Deg2Rad), Mathf.Sin(theta * Mathf.Deg2Rad));
                return (center + radius * direction, theta - 90f);
            }

            if (distance < straightW) // top edge, left to right
            {
                return (new Vector2(-hw + distance, hh + radius), 0f);
            }
            distance -= straightW;
            if (distance < arc)
            {
                return OnArc(new Vector2(hw, hh), 90f, distance / arc);
            }
            distance -= arc;
            if (distance < straightH) // right edge, downward
            {
                return (new Vector2(hw + radius, hh - distance), 90f);
            }
            distance -= straightH;
            if (distance < arc)
            {
                return OnArc(new Vector2(hw, -hh), 0f, distance / arc);
            }
            distance -= arc;
            if (distance < straightW) // bottom edge, right to left
            {
                return (new Vector2(hw - distance, -hh - radius), 0f);
            }
            distance -= straightW;
            if (distance < arc)
            {
                return OnArc(new Vector2(-hw, -hh), -90f, distance / arc);
            }
            distance -= arc;
            if (distance < straightH) // left edge, upward
            {
                return (new Vector2(-hw - radius, -hh + distance), 90f);
            }
            distance -= straightH;
            return OnArc(new Vector2(-hw, hh), 180f, Mathf.Clamp01(distance / arc));
        }

        private static void AddDash(RectTransform host, Vector2 position, Vector2 size,
            Color color, float angleDegrees = 0f)
        {
            var go = new GameObject("Dash", typeof(RectTransform), typeof(Image),
                typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            go.transform.SetParent(host, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localEulerAngles = new Vector3(0, 0, angleDegrees);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }
    }
}
