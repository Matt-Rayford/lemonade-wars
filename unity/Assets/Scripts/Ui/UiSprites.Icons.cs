using UnityEngine;

namespace LemonadeWars.Unity
{
    public static partial class UiSprites
    {
        /// <summary>Icons are drawn LARGE and shown at ~22-36px: one sample per pixel crawls and sparkles
        /// when minified, so each pixel averages a 4x4 grid and the textures keep mipmaps.</summary>
        private const int IconSamplesPerAxis = 4;
        private const float IconSampleStep = 1f / IconSamplesPerAxis;

        private static readonly Color TearFill = new Color(0.608f, 0.863f, 1f);
        private static readonly Color TearRim = new Color(0.082f, 0.369f, 0.561f);
        private static readonly Color TearFleck = new Color(0.918f, 0.973f, 1f);

        private static Sprite _magnifier;
        private static Sprite _babyTears;

        /// <summary>A ring with a handle running down-right, drawn white so any tint lands
        /// exactly. Square: give it a square rect. Use Image.Type.Simple.</summary>
        public static Sprite Magnifier
        {
            get
            {
                if (_magnifier == null)
                {
                    _magnifier = GenerateMagnifier(128);
                }
                return _magnifier;
            }
        }

        /// <summary>Overlay for the Whiniest Baby's tantrum icon. Full-color (tint white) and
        /// authored in the icon's own space: give it the icon's exact rect.</summary>
        public static Sprite BabyTears
        {
            get
            {
                if (_babyTears == null)
                {
                    _babyTears = GenerateBabyTears(256);
                }
                return _babyTears;
            }
        }

        /// <summary>Ring and handle are both "distance to the centre line, minus half the stroke",
        /// which keeps the stroke weight even around the curve and along the handle.</summary>
        private static Sprite GenerateMagnifier(int size)
        {
            var texture = NewIconTexture(size);

            float halfStroke = size * 0.045f;
            var lensCenter = new Vector2(size * 0.42f, size * 0.58f);
            float lensRadius = size * 0.27f;
            // The handle runs from the lens rim at 45° down-right, out toward the corner.
            float diagonal = lensRadius * 0.7071f;
            var handleFrom = lensCenter + new Vector2(diagonal, -diagonal);
            var handleTo = new Vector2(size * 0.85f, size * 0.15f);
            var handleAxis = handleTo - handleFrom;
            float axisLengthSq = Vector2.Dot(handleAxis, handleAxis);

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float coverage = 0f;
                    for (int sy = 0; sy < IconSamplesPerAxis; sy++)
                    {
                        for (int sx = 0; sx < IconSamplesPerAxis; sx++)
                        {
                            var sample = new Vector2(x + (sx + 0.5f) * IconSampleStep, y + (sy + 0.5f) * IconSampleStep);
                            float ringDistance = Mathf.Abs((sample - lensCenter).magnitude - lensRadius) - halfStroke;
                            float alongHandle = Mathf.Clamp01(Vector2.Dot(sample - handleFrom, handleAxis) / axisLengthSq);
                            float handleDistance = (sample - (handleFrom + handleAxis * alongHandle)).magnitude - halfStroke;

                            coverage += Mathf.Clamp01(0.5f - Mathf.Min(ringDistance, handleDistance));
                        }
                    }
                    coverage /= IconSamplesPerAxis * IconSamplesPerAxis;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(coverage * 255f));
                }
            }
            return CreateIconSprite(texture, pixels, size);
        }

        /// <summary>Two fat tears hanging from the eyes, each with fill, dark rim and a highlight fleck.</summary>
        private static Sprite GenerateBabyTears(int size)
        {
            var texture = NewIconTexture(size);
            var teardrops = PlaceTeardrops(size);
            float rimHalfWidth = size * 0.008f;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float sumR = 0f, sumG = 0f, sumB = 0f, sumA = 0f;
                    for (int sy = 0; sy < IconSamplesPerAxis; sy++)
                    {
                        for (int sx = 0; sx < IconSamplesPerAxis; sx++)
                        {
                            // Texture rows run bottom-up; the design space is y-down.
                            var sample = new Vector2(x + (sx + 0.5f) * IconSampleStep,
                                size - (y + (sy + 0.5f) * IconSampleStep));
                            if (TearColorAt(sample, teardrops, rimHalfWidth) is Color color)
                            {
                                sumR += color.r; sumG += color.g; sumB += color.b; sumA += 1f;
                            }
                        }
                    }
                    pixels[y * size + x] = UnpremultipliedAverage(sumR, sumG, sumB, sumA);
                }
            }
            return CreateIconSprite(texture, pixels, size);
        }

        /// <summary>One teardrop per eye, in texture pixels (y-down).</summary>
        private static (Vector2 Head, Vector2 Tip, float Radius, Vector2 Fleck, float FleckRadius)[]
            PlaceTeardrops(int size)
        {
            // Hung from the eyes' inner corners in the icon's 0-100 space, y-down; the face is tilted,
            // so the right drop rides higher. Sized to survive 28px minification in the player bar.
            var drops = new (Vector2 Pos, float Scale, float RotDegrees)[]
            {
                (new Vector2(32f, 36f), 1.45f, -8f),
                (new Vector2(54f, 32f), 1.45f, 8f),
            };
            float unit = size / 100f;
            var teardrops = new (Vector2 Head, Vector2 Tip, float Radius, Vector2 Fleck, float FleckRadius)[drops.Length];
            for (int i = 0; i < drops.Length; i++)
            {
                float scale = drops[i].Scale * unit;
                float radians = drops[i].RotDegrees * Mathf.Deg2Rad;
                float cos = Mathf.Cos(radians);
                float sin = Mathf.Sin(radians);
                Vector2 Rotate(Vector2 v) => new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
                var origin = drops[i].Pos * unit;
                // The base drop: a radius-7 head with its tip 11 above the origin.
                teardrops[i] = (
                    origin + Rotate(new Vector2(0f, 3.5f) * scale),
                    origin + Rotate(new Vector2(0f, -11f) * scale),
                    7f * scale,
                    origin + Rotate(new Vector2(-2.2f, 3.2f) * scale),
                    1.8f * scale);
            }
            return teardrops;
        }

        /// <returns>Null where the sample misses every teardrop.</returns>
        private static Color? TearColorAt(Vector2 sample,
            (Vector2 Head, Vector2 Tip, float Radius, Vector2 Fleck, float FleckRadius)[] teardrops,
            float rimHalfWidth)
        {
            float nearest = float.MaxValue;
            bool isInFleck = false;
            foreach (var teardrop in teardrops)
            {
                float distance = TeardropDistance(sample, teardrop.Head, teardrop.Tip, teardrop.Radius);
                if (distance < nearest)
                {
                    nearest = distance;
                }
                if (distance < -rimHalfWidth &&
                    (sample - teardrop.Fleck).magnitude < teardrop.FleckRadius)
                {
                    isInFleck = true;
                }
            }
            if (nearest < -rimHalfWidth)
            {
                return isInFleck ? TearFleck : TearFill;
            }
            if (nearest < rimHalfWidth)
            {
                return TearRim;
            }
            return null;
        }

        /// <summary>Averaging RGB over the transparent samples too would darken every edge toward black.</summary>
        private static Color32 UnpremultipliedAverage(float sumR, float sumG, float sumB, float coveredSamples)
        {
            if (coveredSamples <= 0f)
            {
                return new Color32(0, 0, 0, 0);
            }
            return new Color32(
                (byte)(sumR / coveredSamples * 255f),
                (byte)(sumG / coveredSamples * 255f),
                (byte)(sumB / coveredSamples * 255f),
                (byte)(coveredSamples / (IconSamplesPerAxis * IconSamplesPerAxis) * 255f));
        }

        /// <summary>Signed distance to a round-cone: the convex hull of a circle (center
        /// <paramref name="head"/>) and the point <paramref name="tip"/>, tapering to zero.</summary>
        private static float TeardropDistance(Vector2 p, Vector2 head, Vector2 tip, float radius)
        {
            var axis = tip - head;
            float height = axis.magnitude;
            if (height < 0.0001f)
            {
                return (p - head).magnitude - radius;
            }
            var along = axis / height;
            var local = p - head;
            float y = Vector2.Dot(local, along);
            float lateral = Mathf.Abs(local.x * along.y - local.y * along.x);
            float taper = radius / height;
            float slope = Mathf.Sqrt(Mathf.Max(0f, 1f - taper * taper));
            float k = lateral * -taper + y * slope;
            if (k < 0f)
            {
                return local.magnitude - radius;
            }
            if (k > slope * height)
            {
                return (p - tip).magnitude;
            }
            return lateral * slope + y * taper - radius;
        }

        private static Texture2D NewIconTexture(int size) =>
            new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };

        private static Sprite CreateIconSprite(Texture2D texture, Color32[] pixels, int size)
        {
            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }
    }
}
