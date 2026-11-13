using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public static partial class UiKit
    {
        /// <summary>Alpha is the dial: raise it to make the wallpaper pattern shout.</summary>
        public static readonly Color WallpaperColor = new Color(0.98f, 0.83f, 0.10f, 0.05f);
        /// <summary>On-screen width of one wallpaper tile, in reference-res pixels.</summary>
        public const float WallpaperTilePx = 1500f;

        /// <summary>Packed mip levels stop here, mirroring tools/make_mips.py.</summary>
        private const int MinPackedMipSizePx = 24;

        private static Texture2D _wallpaper;
        private static Shader _roundedImageShader;

        /// <summary>
        /// The repeating badge pattern, dropped behind everything else in <paramref name="parent"/>.
        /// The art ships white-on-alpha (tools/make_app_bg.py) so the tint lands exactly.
        /// </summary>
        /// <param name="anchorMax">Top edge, so the table can stop below its shelf.</param>
        public static void CreateWallpaper(Transform parent, Vector2 anchorMax)
        {
            if (_wallpaper == null)
            {
                string path = Path.Combine(Application.streamingAssetsPath, "images", "app-bg-tile.png");
                if (!File.Exists(path))
                {
                    return;
                }
                _wallpaper = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _wallpaper.LoadImage(File.ReadAllBytes(path));
                _wallpaper.wrapMode = TextureWrapMode.Repeat;
            }

            var go = new GameObject("Wallpaper", typeof(RectTransform), typeof(RawImage),
                typeof(TiledBackground));
            go.transform.SetParent(parent, false);
            Anchor((RectTransform)go.transform, Vector2.zero, anchorMax);

            var image = go.GetComponent<RawImage>();
            image.texture = _wallpaper;
            image.color = WallpaperColor;
            image.raycastTarget = false;

            var tiler = go.GetComponent<TiledBackground>();
            tiler.TileWidth = WallpaperTilePx;
            tiler.TileHeight = WallpaperTilePx * _wallpaper.height / _wallpaper.width;
            go.transform.SetAsFirstSibling();
        }

        /// <summary>
        /// Black-ink line art from the icons folder as a white mask: ink becomes opaque white, paper
        /// (white or clear) becomes clear. A tint then paints the ink any colour exactly, and light
        /// details inside the ink (a robot's eyes) read as holes. Null if the file is missing.
        /// </summary>
        public static Texture2D LoadInkIconAsMask(string fileName)
        {
            var icon = LoadTextureSharp(Path.Combine(Application.streamingAssetsPath, "icons", fileName));
            if (icon == null)
            {
                return null;
            }
            var pixels = icon.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                float inkiness = 1f - (pixel.r + pixel.g + pixel.b) / (3f * 255f);
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(pixel.a * inkiness));
            }
            icon.SetPixels32(pixels);
            icon.Apply(updateMipmaps: true);
            return icon;
        }

        /// <summary>Unity's box-filtered auto-mips smear minified card text: when tools/make_mips.py
        /// has packed Lanczos levels into a sibling `name.mips.ext`, those replace them.</summary>
        /// <returns>Null if the image is missing.</returns>
        public static Texture2D LoadTextureSharp(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return null;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            texture.LoadImage(File.ReadAllBytes(fullPath));
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;

            string packedPath = Path.ChangeExtension(fullPath, null) +
                ".mips" + Path.GetExtension(fullPath);
            if (File.Exists(packedPath))
            {
                ReplaceMipsWithPackedLevels(texture, packedPath);
            }
            return texture;
        }

        private static void ReplaceMipsWithPackedLevels(Texture2D texture, string packedPath)
        {
            var packed = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            packed.LoadImage(File.ReadAllBytes(packedPath));

            // The generator stacks levels top-to-bottom and Unity's origin is bottom-left,
            // so walk DOWN from the top. The stop conditions mirror the generator exactly.
            int y = packed.height;
            for (int level = 1; ; level++)
            {
                int levelWidth = texture.width >> level;
                int levelHeight = texture.height >> level;
                if (levelWidth < MinPackedMipSizePx || levelHeight < MinPackedMipSizePx ||
                    levelWidth > packed.width || y - levelHeight < 0)
                {
                    break;
                }
                y -= levelHeight;
                texture.SetPixels(packed.GetPixels(0, y, levelWidth, levelHeight), level);
            }
            // updateMipmaps: false keeps the sharp levels (deeper, tiny ones stay auto-generated).
            texture.Apply(false);
            Object.Destroy(packed);
        }

        /// <summary>
        /// Rounded corners are cut by the RoundedImage shader (stencil Masks stair-step).
        /// Returns the RawImage: attach handlers to its gameObject; its parent is the layout frame.
        /// </summary>
        public static RawImage CreateCardImage(Transform parent, Texture2D texture, float width, float height)
        {
            var frame = new GameObject("Card", typeof(RectTransform), typeof(Image),
                typeof(LayoutElement));
            frame.transform.SetParent(parent, false);
            var frameImage = frame.GetComponent<Image>();
            frameImage.sprite = UiSprites.RoundedRect;
            frameImage.type = Image.Type.Sliced;
            // The frame only shows as the placeholder for missing art.
            frameImage.color = new Color(0.28f, 0.28f, 0.32f);
            frameImage.pixelsPerUnitMultiplier = 14f / CardCornerRadius(width);
            frameImage.enabled = texture == null;
            var layout = frame.GetComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;
            // Never absorb leftover row space: that stretches the art.
            layout.flexibleWidth = 0;
            layout.flexibleHeight = 0;

            var go = new GameObject("Tex", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(frame.transform, false);
            Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);
            var image = go.GetComponent<RawImage>();
            image.texture = texture;
            if (texture == null)
            {
                image.color = new Color(1, 1, 1, 0);
            }
            else
            {
                image.material = RoundedImageMaterial(width, height);
            }
            return image;
        }

        /// <summary>Proportional at hand size and up; tapers faster below it, where a
        /// proportional radius bites into the small cards' thin printed borders.</summary>
        public static float CardCornerRadius(float width)
        {
            const float handWidth = 190f;
            const float handRadius = handWidth * (14f / 150f);
            return width >= handWidth
                ? width * (14f / 150f)
                : Mathf.Max(4f, handRadius * Mathf.Pow(width / handWidth, 1.8f));
        }

        public static Material RoundedImageMaterial(float width, float height)
        {
            if (_roundedImageShader == null)
            {
                _roundedImageShader = Resources.Load<Shader>("shaders/RoundedImage");
            }
            var material = new Material(_roundedImageShader);
            material.SetVector("_Size", new Vector4(width, height, 0, 0));
            material.SetFloat("_Radius", CardCornerRadius(width));
            return material;
        }
    }
}
