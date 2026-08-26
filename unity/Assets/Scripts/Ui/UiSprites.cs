using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Procedurally generated shared UI sprites: a 9-sliced rounded rectangle (card frames,
    /// masks) and a soft-falloff rounded glow. Generated once per session — no asset files.
    /// </summary>
    public static class UiSprites
    {
        private static Sprite _roundedRect;
        private static Sprite _roundedOutline;
        private static Sprite _glow;
        private static Sprite _glowRing;
        private static Sprite _circle;
        private static Sprite _magnifier;
        private static Sprite _babyTears;

        /// <summary>
        /// Anti-aliased rounded rectangle, 9-sliced so the corner radius stays constant.
        /// Generated at 2x density (pixelsPerUnit 200): same on-screen size, twice the
        /// texels per corner — smooth even where the UI magnifies corners (previews,
        /// discard browser).
        /// </summary>
        public static Sprite RoundedRect
        {
            get
            {
                if (_roundedRect == null)
                {
                    _roundedRect = Generate(128, 28f, 0f, border: 40f, pixelsPerUnit: 200f);
                }
                return _roundedRect;
            }
        }

        /// <summary>Rounded-rect OUTLINE (~2 unit rim), 9-sliced: selection borders.</summary>
        public static Sprite RoundedOutline
        {
            get
            {
                if (_roundedOutline == null)
                {
                    _roundedOutline = Generate(128, 28f, 0f, border: 40f,
                        pixelsPerUnit: 200f, outline: 4f);
                }
                return _roundedOutline;
            }
        }

        /// <summary>Anti-aliased filled circle (player badges). Use Image.Type.Simple.</summary>
        public static Sprite Circle
        {
            get
            {
                if (_circle == null)
                {
                    _circle = Generate(128, 62f, 0f, border: 0f, pixelsPerUnit: 200f);
                }
                return _circle;
            }
        }

        /// <summary>Soft glow: solid rounded core fading out over a wide falloff. 9-sliced.</summary>
        public static Sprite Glow
        {
            get
            {
                if (_glow == null)
                {
                    _glow = Generate(192, 20f, 60f, border: 88f, pixelsPerUnit: 200f);
                }
                return _glow;
            }
        }

        /// <summary>
        /// Outer glow ring: a thin rim at the shape edge fading outward only — the
        /// interior stays clear, so whatever it wraps remains readable. 9-sliced; the
        /// rim sits 31 canvas units inside the rect edge (falloff 60px + inset, at 2x).
        /// </summary>
        public static Sprite GlowRing
        {
            get
            {
                if (_glowRing == null)
                {
                    _glowRing = Generate(192, 20f, 60f, border: 88f,
                        pixelsPerUnit: 200f, outline: 6f);
                }
                return _glowRing;
            }
        }

        /// <summary>
        /// Magnifying glass: a ring with a handle running down-right, drawn white so any
        /// tint lands exactly. Square — give it a square rect. Use Image.Type.Simple.
        /// </summary>
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

        /// <summary>
        /// Two fat tears hanging from the eyes — overlay for the tantrum icon when its
        /// owner is the Whiniest Baby. Full-color (tint white). Authored in the icon's
        /// own space: give it the icon's exact rect.
        /// </summary>
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

        /// <summary>
        /// Teardrops (signed-distance round-cones: a circle hulled to a tip), each
        /// with fill, dark rim, and a highlight fleck. Drawn LARGE and shown ~36px, so
        /// like the magnifier it supersamples and keeps mipmaps for minification.
        /// </summary>
        private static Sprite GenerateBabyTears(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };

            var fill = new Color(0.608f, 0.863f, 1f);       // light tear blue
            var rim = new Color(0.082f, 0.369f, 0.561f);    // deep blue edge
            var fleck = new Color(0.918f, 0.973f, 1f);      // near-white glint

            // One drop per eye, hung from the inner corners (the icon's eyes sit high;
            // the face is tilted, so the right drop rides higher). Values are in 0-100
            // icon space, y-down; scale multiplies the base drop (tip 11 above a
            // radius-7 head). Sized to survive 28px minification in the player bar.
            var drops = new (Vector2 Pos, float Scale, float RotDegrees)[]
            {
                (new Vector2(32f, 36f), 1.45f, -8f),
                (new Vector2(54f, 32f), 1.45f, 8f),
            };
            float unit = size / 100f;
            int count = drops.Length;
            var heads = new Vector2[count];      // circle centers
            var tips = new Vector2[count];
            var radii = new float[count];
            var flecks = new Vector2[count];
            var fleckRadii = new float[count];
            for (int i = 0; i < count; i++)
            {
                float k = drops[i].Scale * unit;
                float rad = drops[i].RotDegrees * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);
                Vector2 Rotate(Vector2 v) => new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
                var origin = drops[i].Pos * unit;
                heads[i] = origin + Rotate(new Vector2(0f, 3.5f) * k);
                tips[i] = origin + Rotate(new Vector2(0f, -11f) * k);
                radii[i] = 7f * k;
                flecks[i] = origin + Rotate(new Vector2(-2.2f, 3.2f) * k);
                fleckRadii[i] = 1.8f * k;
            }

            float rimHalfWidth = size * 0.008f;
            const int samples = 4;
            const float step = 1f / samples;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float sumR = 0f, sumG = 0f, sumB = 0f, sumA = 0f;
                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            // Texture rows run bottom-up; the design space is y-down.
                            var p = new Vector2(x + (sx + 0.5f) * step,
                                size - (y + (sy + 0.5f) * step));
                            float d = float.MaxValue;
                            bool inFleck = false;
                            for (int i = 0; i < count; i++)
                            {
                                float di = TeardropDistance(p, heads[i], tips[i], radii[i]);
                                if (di < d)
                                {
                                    d = di;
                                }
                                if (di < -rimHalfWidth &&
                                    (p - flecks[i]).magnitude < fleckRadii[i])
                                {
                                    inFleck = true;
                                }
                            }
                            if (d < -rimHalfWidth)
                            {
                                var c = inFleck ? fleck : fill;
                                sumR += c.r; sumG += c.g; sumB += c.b; sumA += 1f;
                            }
                            else if (d < rimHalfWidth)
                            {
                                sumR += rim.r; sumG += rim.g; sumB += rim.b; sumA += 1f;
                            }
                        }
                    }
                    if (sumA <= 0f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }
                    // Un-premultiply: averaging RGB with transparent samples would
                    // darken every edge toward black.
                    pixels[y * size + x] = new Color32(
                        (byte)(sumR / sumA * 255f),
                        (byte)(sumG / sumA * 255f),
                        (byte)(sumB / sumA * 255f),
                        (byte)(sumA / (samples * samples) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>
        /// Signed distance to a teardrop: the convex hull of a circle (center
        /// <paramref name="head"/>, radius <paramref name="radius"/>) and the point
        /// <paramref name="tip"/> — a round-cone tapering to zero.
        /// </summary>
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

        /// <summary>
        /// Union of two signed-distance shapes: a circle rim and a capsule handle. Both
        /// are drawn as "distance to the shape's centre line, minus half the stroke",
        /// which keeps the stroke weight even around the curve and along the handle.
        /// </summary>
        /// <remarks>
        /// This one is drawn LARGE and displayed SMALL (~22px), so a single sample per
        /// pixel crawls and sparkles under minification. Two defences: supersample the
        /// distance field, and generate mipmaps so the GPU has a properly filtered
        /// smaller copy to pick instead of undersampling the big one.
        /// </remarks>
        private static Sprite GenerateMagnifier(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };

            float stroke = size * 0.045f;                       // half the line weight
            var lensCenter = new Vector2(size * 0.42f, size * 0.58f);
            float lensRadius = size * 0.27f;
            // Handle runs from the lens rim at 45° down-right, out toward the corner.
            float diagonal = lensRadius * 0.7071f;
            var handleFrom = lensCenter + new Vector2(diagonal, -diagonal);
            var handleTo = new Vector2(size * 0.85f, size * 0.15f);
            var handleAxis = handleTo - handleFrom;
            float axisLengthSq = Vector2.Dot(handleAxis, handleAxis);

            const int samples = 4;                              // 4x4 grid per pixel
            const float step = 1f / samples;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float coverage = 0f;
                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) * step, y + (sy + 0.5f) * step);

                            // Ring: distance to the lens circle's centre line.
                            float ring = Mathf.Abs((p - lensCenter).magnitude - lensRadius) - stroke;

                            // Handle: distance to the segment, i.e. a capsule.
                            float t = Mathf.Clamp01(Vector2.Dot(p - handleFrom, handleAxis) / axisLengthSq);
                            float handle = (p - (handleFrom + handleAxis * t)).magnitude - stroke;

                            coverage += Mathf.Clamp01(0.5f - Mathf.Min(ring, handle));
                        }
                    }
                    coverage /= samples * samples;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(coverage * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>
        /// Draw a rounded rect of the given corner radius; alpha falls from 1 at the shape
        /// edge to 0 over <paramref name="falloff"/> pixels (0 = crisp 1px anti-aliased edge).
        /// </summary>
        private static Sprite Generate(int size, float radius, float falloff, float border,
            float pixelsPerUnit = 100f, float outline = 0f)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float half = size / 2f;
            // The solid core is inset by the falloff so the fade has room inside the texture.
            var coreHalf = new Vector2(half - falloff - 2f, half - falloff - 2f);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    // Signed distance to the rounded rectangle.
                    var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (coreHalf - new Vector2(radius, radius));
                    float outside = new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude;
                    float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0);
                    float dist = outside + inside - radius;

                    float alpha = falloff <= 0f
                        ? Mathf.Clamp01(0.5f - dist)                       // crisp AA edge
                        : Mathf.Pow(1f - Mathf.Clamp01(dist / falloff), 1.5f); // soft glow fade
                    if (outline > 0f)
                    {
                        // Keep only a band along the shape edge: a rim, not a fill.
                        alpha *= Mathf.Clamp01(dist + outline + 0.5f);
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), pixelsPerUnit, 0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }
    }
}
