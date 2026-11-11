using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public static partial class UiKit
    {
        private const float ArrowThicknessPx = 4f;
        private const float ArrowShaftStartPx = 18f;
        private const float ArrowHeadInsetPx = 10f;
        /// <summary>Shorter than this, the shaft-start and head insets leave no shaft to draw.</summary>
        private const float MinArrowLengthPx = 30f;

        public static void DrawArrow(RectTransform host, Vector2 from, Vector2 to, Color color)
        {
            var direction = to - from;
            float length = direction.magnitude;
            if (length < MinArrowLengthPx)
            {
                return;
            }
            var unit = direction / length;
            float angle = Mathf.Atan2(unit.y, unit.x) * Mathf.Rad2Deg;
            float shaftLength = length - ArrowShaftStartPx - ArrowHeadInsetPx;
            CreateStroke(host, from + unit * (ArrowShaftStartPx + shaftLength / 2f), angle, shaftLength, color);
            DrawArrowHead(host, to, angle, color);
        }

        /// <param name="bulge">The control point's offset perpendicular to the chord, as a
        /// fraction of its length; the sign picks the side.</param>
        public static void DrawCurvedArrow(RectTransform host, Vector2 from, Vector2 to,
            float bulge, Color color)
        {
            var chord = to - from;
            float length = chord.magnitude;
            if (length < MinArrowLengthPx)
            {
                return;
            }
            var normal = new Vector2(-chord.y, chord.x) / length;
            var control = (from + to) / 2f + normal * (length * bulge);
            // A quadratic is the cubic with both handles on the one control point.
            DrawCurvedArrow(host, from, control, control, to, color);
        }

        /// <summary>A cubic curve: <paramref name="leave"/> sets the direction it departs
        /// <paramref name="from"/>, <paramref name="approach"/> the direction it arrives at <paramref name="to"/>.</summary>
        public static void DrawCurvedArrow(RectTransform host, Vector2 from, Vector2 leave,
            Vector2 approach, Vector2 to, Color color)
        {
            if ((to - from).magnitude < MinArrowLengthPx)
            {
                return;
            }
            Vector2 Point(float u)
            {
                float v = 1 - u;
                return v * v * v * from + 3 * v * v * u * leave + 3 * v * u * u * approach + u * u * u * to;
            }
            Vector2 Tangent(float u)
            {
                float v = 1 - u;
                return 3 * v * v * (leave - from) + 6 * v * u * (approach - leave) + 3 * u * u * (to - approach);
            }

            // Short overlapping strokes along the curve read as one continuous line.
            const int samples = 64;
            float curveLength = 0f;
            for (int i = 1; i <= samples; i++)
            {
                curveLength += (Point(i / (float)samples) - Point((i - 1) / (float)samples)).magnitude;
            }
            float travelled = 0f;
            var previous = from;
            for (int i = 1; i <= samples; i++)
            {
                var point = Point(i / (float)samples);
                float step = (point - previous).magnitude;
                float midTravelled = travelled + step / 2f;
                if (midTravelled >= ArrowShaftStartPx && midTravelled <= curveLength - ArrowHeadInsetPx)
                {
                    var tangent = Tangent((i - 0.5f) / samples);
                    float angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
                    CreateStroke(host, (point + previous) / 2f, angle, step + 1.5f, color);
                }
                travelled += step;
                previous = point;
            }
            var endTangent = Tangent(1f);
            DrawArrowHead(host, to, Mathf.Atan2(endTangent.y, endTangent.x) * Mathf.Rad2Deg, color);
        }

        private static void DrawArrowHead(RectTransform host, Vector2 tip, float angleDegrees, Color color)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float wingAngle = angleDegrees + sign * 145f;
                var wingDir = new Vector2(
                    Mathf.Cos(wingAngle * Mathf.Deg2Rad), Mathf.Sin(wingAngle * Mathf.Deg2Rad));
                CreateStroke(host, tip + wingDir * 11f, wingAngle, 22f, color);
            }
        }

        private static void CreateStroke(RectTransform host, Vector2 center, float angleDegrees,
            float length, Color color)
        {
            var go = new GameObject("Stroke", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(length, ArrowThicknessPx);
            rect.anchoredPosition = center;
            rect.localEulerAngles = new Vector3(0, 0, angleDegrees);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }
    }
}
