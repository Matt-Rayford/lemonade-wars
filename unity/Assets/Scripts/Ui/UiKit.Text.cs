using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public static partial class UiKit
    {
        private static TMP_FontAsset _titleFont;
        private static TMP_FontAsset _bodyFont;

        /// <summary>Built Titling, for anything shouty. Missing glyphs (em dashes, arrows)
        /// fall back to the body font.</summary>
        public static TMP_FontAsset TitleFont
        {
            get
            {
                if (_titleFont == null)
                {
                    _titleFont = CreateFontAsset("fonts/built-titling-bd");
                    if (_titleFont != null && BodyFont != null && _titleFont != BodyFont)
                    {
                        _titleFont.fallbackFontAssetTable =
                            new List<TMP_FontAsset> { BodyFont };
                    }
                }
                return _titleFont;
            }
        }

        /// <summary>Liberation Sans, the metrically identical open twin of the rulebook's Arial.</summary>
        public static TMP_FontAsset BodyFont
        {
            get
            {
                if (_bodyFont == null)
                {
                    _bodyFont = CreateFontAsset("fonts/liberation-sans");
                }
                return _bodyFont;
            }
        }

        /// <summary>Dynamic SDF asset from a bundled ttf: crisp at any size, every glyph.</summary>
        private static TMP_FontAsset CreateFontAsset(string resourcePath)
        {
            var source = Resources.Load<Font>(resourcePath);
            if (source == null)
            {
                return TMP_Settings.defaultFontAsset; // essentials' Liberation Sans SDF
            }
            return TMP_FontAsset.CreateFontAsset(source, 90, 9,
                GlyphRenderMode.SDFAA, 1024, 1024);
        }

        private static TextAlignmentOptions Align(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                default: return TextAlignmentOptions.BottomRight;
            }
        }

        /// <summary>A dark, softly offset TMP underlay. Instantiates the text's own material.</summary>
        public static void AddTextShadow(TMP_Text text, float strength = 1f)
        {
            var material = text.fontMaterial;
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0, 0, 0, 0.85f));
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.3f * strength);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.3f * strength);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.25f);
        }

        /// <param name="width">In TMP's font-relative units; the atlas padding caps its
        /// reach, so keep it under about 0.35.</param>
        public static void AddTextOutline(TMP_Text text, float width, Color color)
        {
            var material = text.fontMaterial;
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetColor(ShaderUtilities.ID_OutlineColor, color);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
        }

        /// <summary>Title-font text by default; pass body for informational copy.</summary>
        public static TextMeshProUGUI CreateText(Transform parent, string content, int size,
            TextAnchor align = TextAnchor.UpperLeft, Color? color = null, bool body = false)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = body ? BodyFont : TitleFont;
            text.fontSize = size;
            text.alignment = Align(align);
            text.color = color ?? Color.white;
            text.text = content;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Truncate;
            return text;
        }

        /// <summary>Full-weight lettering over a ring of copies in the border color, like the cards'
        /// price tags: the font shader's own outline thins the face and is capped by the atlas padding.</summary>
        /// <returns>The holder to anchor.</returns>
        public static RectTransform CreateOutlinedText(Transform parent, string content, int size,
            Color fill, Color border, float borderPx)
        {
            const int ringCopies = 16;
            var holderGo = new GameObject("OutlinedText", typeof(RectTransform));
            holderGo.transform.SetParent(parent, false);
            var holder = (RectTransform)holderGo.transform;

            TextMeshProUGUI Layer(Color color, Vector2 offsetPx)
            {
                var layer = CreateText(holder, content, size, TextAnchor.MiddleCenter, color);
                layer.raycastTarget = false;
                layer.textWrappingMode = TextWrappingModes.NoWrap;
                layer.overflowMode = TextOverflowModes.Overflow;
                Anchor((RectTransform)layer.transform, Vector2.zero, Vector2.one, offsetPx, offsetPx);
                return layer;
            }

            // The outer ring sets the border's width; the inner one fills the gaps a
            // single ring leaves between its copies on wide borders.
            foreach (float radius in new[] { borderPx, borderPx * 0.5f })
            {
                for (int i = 0; i < ringCopies; i++)
                {
                    float angle = i * Mathf.PI * 2f / ringCopies;
                    Layer(border, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
            }
            Layer(fill, Vector2.zero);
            return holder;
        }

        public static TextMeshProUGUI CreateBadge(Transform parent, string content, int size,
            Color background)
        {
            var go = new GameObject("Badge", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = background;
            go.GetComponent<LayoutElement>().minHeight = size + 8;
            var text = CreateText(go.transform, content, size, TextAnchor.MiddleCenter, body: true);
            Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(4, 1), new Vector2(-4, -1));
            return text;
        }
    }
}
