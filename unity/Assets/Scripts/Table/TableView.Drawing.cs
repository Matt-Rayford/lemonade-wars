using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Dashed outlines, shared by the hand's draw slot and the board's empty VP placeholder.
    public sealed partial class TableView
    {
        /// <summary>
        /// Dashes walk the whole rounded-rect path, corners included, each turned to its
        /// local tangent, at an even spacing fitted to the perimeter.
        /// </summary>
        private static void AddDashedRoundedOutline(RectTransform host, float width,
            float height, float radius, Color color)
        {
            const float thickness = 3f;
            const float dashLength = 13f;
            const float targetSpacing = 24f; // dash + gap, before fitting

            float straightWidth = width - 2f * radius;
            float straightHeight = height - 2f * radius;
            float arc = radius * Mathf.PI / 2f;
            float perimeter = 2f * (straightWidth + straightHeight) + 4f * arc;
            int count = Mathf.Max(8, Mathf.RoundToInt(perimeter / targetSpacing));
            float step = perimeter / count;

            for (int i = 0; i < count; i++)
            {
                var (position, angle) = PointOnRoundedRect(i * step, straightWidth, straightHeight, radius);
                AddDash(host, position, new Vector2(dashLength, thickness), color, angle);
            }
        }

        /// <summary>
        /// Position and tangent angle at a distance along a clockwise rounded rect centred
        /// on the origin, starting at the left end of its top edge.
        /// </summary>
        private static (Vector2 Position, float Angle) PointOnRoundedRect(
            float distance, float straightWidth, float straightHeight, float radius)
        {
            float arc = radius * Mathf.PI / 2f;
            float halfWidth = straightWidth / 2f;
            float halfHeight = straightHeight / 2f;

            (Vector2, float) OnArc(Vector2 center, float startDegrees, float progress)
            {
                float theta = startDegrees - 90f * progress;
                var direction = new Vector2(
                    Mathf.Cos(theta * Mathf.Deg2Rad), Mathf.Sin(theta * Mathf.Deg2Rad));
                return (center + radius * direction, theta - 90f);
            }

            if (distance < straightWidth) // top edge, left to right
            {
                return (new Vector2(-halfWidth + distance, halfHeight + radius), 0f);
            }
            distance -= straightWidth;
            if (distance < arc)
            {
                return OnArc(new Vector2(halfWidth, halfHeight), 90f, distance / arc);
            }
            distance -= arc;
            if (distance < straightHeight) // right edge, downward
            {
                return (new Vector2(halfWidth + radius, halfHeight - distance), 90f);
            }
            distance -= straightHeight;
            if (distance < arc)
            {
                return OnArc(new Vector2(halfWidth, -halfHeight), 0f, distance / arc);
            }
            distance -= arc;
            if (distance < straightWidth) // bottom edge, right to left
            {
                return (new Vector2(halfWidth - distance, -halfHeight - radius), 0f);
            }
            distance -= straightWidth;
            if (distance < arc)
            {
                return OnArc(new Vector2(-halfWidth, -halfHeight), -90f, distance / arc);
            }
            distance -= arc;
            if (distance < straightHeight) // left edge, upward
            {
                return (new Vector2(-halfWidth - radius, -halfHeight + distance), 90f);
            }
            distance -= straightHeight;
            return OnArc(new Vector2(-halfWidth, halfHeight), 180f, Mathf.Clamp01(distance / arc));
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
