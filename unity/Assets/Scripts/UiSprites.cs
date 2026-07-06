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
        private static Sprite _circle;

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
