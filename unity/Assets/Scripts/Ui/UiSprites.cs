using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Shared UI sprites drawn in code once per session: no asset files.</summary>
    public static partial class UiSprites
    {
        private const int GlowRingSizePx = 192;
        private const float GlowRingFalloffPx = 60f;
        private const float GlowRingPixelsPerUnit = 200f;

        private static Sprite _roundedRect;
        private static Sprite _roundedOutline;
        private static Sprite _glow;
        private static Sprite _glowRing;
        private static Sprite _circle;

        /// <summary>9-sliced so the corner radius stays constant. Generated at 2x density
        /// (pixelsPerUnit 200) so magnified corners (previews, discard browser) stay smooth.</summary>
        public static Sprite RoundedRect
        {
            get
            {
                if (_roundedRect == null)
                {
                    _roundedRect = GenerateRoundedRect(128, 28f, 0f, border: 40f, pixelsPerUnit: 200f);
                }
                return _roundedRect;
            }
        }

        /// <summary>A ~2 unit rim, 9-sliced: selection borders.</summary>
        public static Sprite RoundedOutline
        {
            get
            {
                if (_roundedOutline == null)
                {
                    _roundedOutline = GenerateRoundedRect(128, 28f, 0f, border: 40f,
                        pixelsPerUnit: 200f, outline: 4f);
                }
                return _roundedOutline;
            }
        }

        /// <summary>Filled, for player badges. Use Image.Type.Simple.</summary>
        public static Sprite Circle
        {
            get
            {
                if (_circle == null)
                {
                    // Mipmapped: it is shown far below its 128px (16px status dots,
                    // 56px badges), and an unfiltered shrink turns the rim to mush.
                    _circle = GenerateRoundedRect(128, 62f, 0f, border: 0f, pixelsPerUnit: 200f,
                        mipmaps: true);
                }
                return _circle;
            }
        }

        /// <summary>A solid rounded core fading out over a wide falloff. 9-sliced.</summary>
        public static Sprite Glow
        {
            get
            {
                if (_glow == null)
                {
                    _glow = GenerateRoundedRect(192, 20f, 60f, border: 88f, pixelsPerUnit: 200f);
                }
                return _glow;
            }
        }

        /// <summary>A thin rim at the shape edge fading outward only: the interior stays
        /// clear, so whatever it wraps remains readable. 9-sliced.</summary>
        public static Sprite GlowRing
        {
            get
            {
                if (_glowRing == null)
                {
                    _glowRing = GenerateRoundedRect(GlowRingSizePx, 20f, GlowRingFalloffPx, border: 88f,
                        pixelsPerUnit: GlowRingPixelsPerUnit, outline: 6f);
                }
                return _glowRing;
            }
        }

        /// <summary>How far inside a GlowRing image's rect its bright rim sits, in UI units: size the
        /// image this much larger than a card on every side and the rim lands on the card's edge.</summary>
        public static float GlowRingRimInset =>
            (GlowRingFalloffPx + 2f) * 100f / GlowRingPixelsPerUnit;

        /// <param name="falloff">Pixels over which alpha falls from 1 at the shape edge to 0;
        /// 0 gives a crisp 1px anti-aliased edge.</param>
        private static Sprite GenerateRoundedRect(int size, float radius, float falloff, float border,
            float pixelsPerUnit = 100f, float outline = 0f, bool mipmaps = false)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipmaps)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear,
            };

            float half = size / 2f;
            // The solid core is inset by the falloff so the fade has room inside the texture.
            var coreHalf = new Vector2(half - falloff - 2f, half - falloff - 2f);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var fromCenter = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float dist = RoundedRectDistance(fromCenter, coreHalf, radius);

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

        /// <summary>Signed distance: negative inside the shape.</summary>
        private static float RoundedRectDistance(Vector2 fromCenter, Vector2 halfSize, float radius)
        {
            var q = new Vector2(Mathf.Abs(fromCenter.x), Mathf.Abs(fromCenter.y)) - (halfSize - new Vector2(radius, radius));
            float outside = new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0);
            return outside + inside - radius;
        }
    }
}
