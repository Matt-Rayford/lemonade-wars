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
        private static Sprite _glow;

        /// <summary>Anti-aliased rounded rectangle, 9-sliced so the corner radius stays constant.</summary>
        public static Sprite RoundedRect
        {
            get
            {
                if (_roundedRect == null)
                {
                    _roundedRect = Generate(64, 14f, 0f, border: 20f);
                }
                return _roundedRect;
            }
        }

        /// <summary>Soft glow: solid rounded core fading out over a wide falloff. 9-sliced.</summary>
        public static Sprite Glow
        {
            get
            {
                if (_glow == null)
                {
                    _glow = Generate(96, 10f, 30f, border: 44f);
                }
                return _glow;
            }
        }

        /// <summary>
        /// Draw a rounded rect of the given corner radius; alpha falls from 1 at the shape
        /// edge to 0 over <paramref name="falloff"/> pixels (0 = crisp 1px anti-aliased edge).
        /// </summary>
        private static Sprite Generate(int size, float radius, float falloff, float border)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
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
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }
    }
}
