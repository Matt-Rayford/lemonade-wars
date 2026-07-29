using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>Small helpers for building the debug HUD entirely from code.</summary>
    public static class UiKit
    {
        /// <summary>The one background color for the whole table. The play zones
        /// (shelf, board, hand, log) are transparent and let this show through, so
        /// changing this single value re-themes the entire screen.</summary>
        /// #1E0A3D — sampled from the Black Market card back (its dominant color).
        /// The project renders in Gamma color space, so these map 1:1 to sRGB.
        public static readonly Color TableColor = new Color(0.118f, 0.039f, 0.239f);

        public static readonly Color PanelColor = new Color(0.10f, 0.12f, 0.16f, 0.92f);
        public static readonly Color ButtonColor = new Color(0.98f, 0.83f, 0.10f);
        public static readonly Color ButtonTextColor = new Color(0.12f, 0.10f, 0.05f);

        /// <summary>Wallpaper tint: lemonade yellow, faint enough to stay wallpaper.
        /// Alpha is the dial — raise it to make the pattern shout.</summary>
        public static readonly Color WallpaperColor = new Color(0.98f, 0.83f, 0.10f, 0.05f);
        /// <summary>On-screen width of one wallpaper tile, in reference-res pixels.</summary>
        public const float WallpaperTile = 1500f;

        private static Texture2D _wallpaper;

        private static TMP_FontAsset _titleFont;
        private static TMP_FontAsset _bodyFont;

        /// <summary>
        /// Display font (Built Titling) as a dynamic SDF asset — headers, buttons,
        /// anything shouty. Missing glyphs (em dashes, arrows) fall back to the body font.
        /// </summary>
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

        /// <summary>
        /// Body font — Liberation Sans, the metrically identical open twin of the
        /// rulebook's Arial: stats, captions, status lines, inputs.
        /// </summary>
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

        /// <summary>
        /// TMP underlay standing in for the old UGUI Shadow component: a dark, softly
        /// offset copy behind the glyphs. Instantiates the text's own material.
        /// </summary>
        public static void AddTextShadow(TMP_Text text, float strength = 1f)
        {
            var material = text.fontMaterial;
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0, 0, 0, 0.85f));
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.3f * strength);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.3f * strength);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.25f);
        }

        public static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
            }
            return canvas;
        }

        /// <summary>
        /// The repeating badge pattern, tinted and dropped behind everything else in
        /// <paramref name="parent"/>. The art ships as white-on-alpha
        /// (tools/make_app_bg.py) precisely so the tint lands exactly: white x yellow
        /// == yellow, at whatever opacity we pick. Never takes a raycast.
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
            tiler.TileWidth = WallpaperTile;
            tiler.TileHeight = WallpaperTile * _wallpaper.height / _wallpaper.width;
            go.transform.SetAsFirstSibling();
        }

        /// <summary>
        /// Load an image with SHARP mipmaps. Unity's auto-mips are box-filtered and
        /// smear minified card text; tools/make_mips.py packs Lanczos downscales into
        /// a sibling `name.mips.ext` (levels stacked top-to-bottom), and this splits
        /// them back into the texture's mip levels. Falls back cleanly to the auto
        /// mips when no packed file exists. Returns null if the image is missing.
        /// </summary>
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
            if (!File.Exists(packedPath))
            {
                return texture;
            }
            var packed = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            packed.LoadImage(File.ReadAllBytes(packedPath));

            // Bands are stacked top-to-bottom by the generator; Unity's origin is
            // bottom-left, so walk DOWN from the top. Stop conditions mirror the
            // generator exactly (level dims = size >> L, floor 24px).
            const int minDim = 24;
            int y = packed.height;
            for (int level = 1; ; level++)
            {
                int levelWidth = texture.width >> level;
                int levelHeight = texture.height >> level;
                if (levelWidth < minDim || levelHeight < minDim ||
                    levelWidth > packed.width || y - levelHeight < 0)
                {
                    break;
                }
                y -= levelHeight;
                texture.SetPixels(packed.GetPixels(0, y, levelWidth, levelHeight), level);
            }
            // updateMipmaps: false — keep our sharp levels (deeper, tiny levels stay
            // auto-generated; nothing on screen ever samples them).
            texture.Apply(false);
            Object.Destroy(packed);
            return texture;
        }

        public static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        public static RectTransform Anchor(RectTransform rt, Vector2 min, Vector2 max,
            Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
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

        /// <param name="dark">
        /// Inverse chrome for buttons sitting ON a light surface (the yellow status
        /// bar): the normal hover paint is lemonade-yellow, which would make such a
        /// button vanish into its background.
        /// </param>
        /// <param name="clickSound">Clip name, or null for a button with its own audio.</param>
        public static Button CreateButton(Transform parent, string label, int fontSize,
            UnityEngine.Events.UnityAction onClick, bool dark = false,
            string clickSound = Sfx.ButtonClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 34;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            // The click sound belongs to the chrome, not to any one screen: every menu,
            // lobby, pause, and rulebook button is built here.
            if (!string.IsNullOrEmpty(clickSound))
            {
                button.onClick.AddListener(() => Sfx.Play(clickSound));
            }
            button.onClick.AddListener(onClick);

            // Subtle translucent grey at rest (light text); lemonade-yellow with dark
            // text when the cursor invites it — same language as the prompt options.
            var idleBackground = dark
                ? new Color(0.10f, 0.12f, 0.16f, 0.94f)
                : new Color(0.58f, 0.61f, 0.67f, 0.32f);
            var idleText = dark ? new Color(0.96f, 0.94f, 0.86f) : new Color(0.93f, 0.93f, 0.90f);
            var hoverBackground = dark ? new Color(0.18f, 0.21f, 0.28f, 1f) : ButtonColor;
            var hoverText = dark ? ButtonColor : ButtonTextColor;
            var image = go.GetComponent<Image>();
            // Same rounded corners as the prompt options and the shelf's refresh slab —
            // every button in the game shares one silhouette.
            image.sprite = UiSprites.RoundedRect;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 14f / 10f; // ~10px corner radius
            image.color = idleBackground;

            var text = CreateText(go.transform, label, fontSize, TextAnchor.MiddleLeft, idleText);
            Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 2), new Vector2(-6, -2));

            AddHover(go,
                () =>
                {
                    image.color = hoverBackground;
                    text.color = hoverText;
                },
                () =>
                {
                    image.color = idleBackground;
                    text.color = idleText;
                });
            return button;
        }

        /// <summary>
        /// Code-built slider with whole-number steps: the value IS the step index, so
        /// dragging can only ever land on a legal stop (no rounding feedback loops).
        /// Returns the Slider — subscribe to onValueChanged for the step index.
        /// </summary>
        public static Slider CreateSlider(Transform parent, int steps, int initialStep)
        {
            var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 40;
            var slider = go.GetComponent<Slider>();
            slider.transition = Selectable.Transition.None;

            const float handleWidth = 26f;
            var track = CreatePanel(go.transform, "Track", new Color(0.09f, 0.11f, 0.15f, 0.95f));
            track.GetComponent<Image>().sprite = UiSprites.RoundedRect;
            track.GetComponent<Image>().type = Image.Type.Sliced;
            Anchor(track, new Vector2(0, 0.34f), new Vector2(1, 0.66f),
                new Vector2(handleWidth / 2f, 0), new Vector2(-handleWidth / 2f, 0));

            // Fill area is inset by the handle radius so the fill lines up with it.
            var fillArea = CreatePanel(go.transform, "FillArea", new Color(0, 0, 0, 0));
            fillArea.GetComponent<Image>().raycastTarget = false;
            Anchor(fillArea, new Vector2(0, 0.34f), new Vector2(1, 0.66f),
                new Vector2(handleWidth / 2f, 0), new Vector2(-handleWidth / 2f, 0));
            var fill = CreatePanel(fillArea, "Fill", ButtonColor);
            fill.GetComponent<Image>().sprite = UiSprites.RoundedRect;
            fill.GetComponent<Image>().type = Image.Type.Sliced;
            fill.GetComponent<Image>().raycastTarget = false;
            fill.anchorMin = new Vector2(0, 0);
            fill.anchorMax = new Vector2(0, 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;

            var handleArea = CreatePanel(go.transform, "HandleArea", new Color(0, 0, 0, 0));
            handleArea.GetComponent<Image>().raycastTarget = false;
            Anchor(handleArea, Vector2.zero, Vector2.one,
                new Vector2(handleWidth / 2f, 0), new Vector2(-handleWidth / 2f, 0));
            var handle = CreatePanel(handleArea, "Handle", new Color(0.98f, 0.90f, 0.55f));
            handle.GetComponent<Image>().sprite = UiSprites.Circle;
            handle.GetComponent<Image>().preserveAspect = true;
            handle.anchorMin = new Vector2(0, 0.5f);
            handle.anchorMax = new Vector2(0, 0.5f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(handleWidth, handleWidth);

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = true;
            slider.minValue = 0;
            slider.maxValue = steps;
            slider.value = Mathf.Clamp(initialStep, 0, steps);
            return slider;
        }

        /// <summary>
        /// Card image with rounded corners, cut by the RoundedImage shader (smooth at
        /// any scale — stencil Masks are binary and stair-step). Returns the RawImage —
        /// attach hover/click handlers to its gameObject; its parent is the layout frame.
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
            // Never absorb leftover row space — that stretches the art.
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

        private static Shader _roundedImageShader;

        /// <summary>
        /// Corner radius for a card of the given width. Proportional at hand size and
        /// up; tapers faster below it — small cards' printed borders are thin, and a
        /// proportional radius bites into them too aggressively.
        /// </summary>
        public static float CardCornerRadius(float width)
        {
            const float handWidth = 190f;
            const float handRadius = handWidth * (14f / 150f);
            return width >= handWidth
                ? width * (14f / 150f)
                : Mathf.Max(4f, handRadius * Mathf.Pow(width / handWidth, 1.8f));
        }

        /// <summary>Material that clips its texture to a rounded rect.</summary>
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

        /// <summary>Vertical scroll list; returns the content container to fill.</summary>
        public static RectTransform CreateScrollList(RectTransform host)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(host, false);
            Anchor((RectTransform)scrollGo.transform, Vector2.zero, Vector2.one,
                new Vector2(4, 4), new Vector2(-4, -4));
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.25f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            Anchor((RectTransform)viewportGo.transform, Vector2.zero, Vector2.one);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            // Fresh RectTransforms default to sizeDelta (100,100): with stretch anchors
            // that is +100px of WIDTH, center-pivoted — rows poke 50px past BOTH edges.
            content.sizeDelta = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = (RectTransform)viewportGo.transform;
            scroll.content = content;
            return content;
        }

        /// <summary>Horizontal row for card images.</summary>
        public static RectTransform CreateCardRow(RectTransform host, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(host, false);
            Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one,
                new Vector2(6, 6), new Vector2(-6, -6));
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            return (RectTransform)go.transform;
        }

        public static void Clear(Transform container)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                // Destroy only lands at the END of the frame: detach now, or a layout
                // group rebuilt in this same frame still counts the corpses as children
                // and places the fresh content below them.
                var child = container.GetChild(i);
                child.SetParent(null, false);
                Object.Destroy(child.gameObject);
            }
        }

        /// <summary>Horizontal scroll strip (for hands and long rows); returns the content container.</summary>
        public static RectTransform CreateScrollRow(RectTransform host)
        {
            var scrollGo = new GameObject("ScrollRow", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(host, false);
            Anchor((RectTransform)scrollGo.transform, Vector2.zero, Vector2.one,
                new Vector2(4, 4), new Vector2(-4, -4));
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.15f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.vertical = false;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            Anchor((RectTransform)viewportGo.transform, Vector2.zero, Vector2.one);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0, 0);
            content.anchorMax = new Vector2(0, 1);
            content.pivot = new Vector2(0, 0.5f);
            var layout = contentGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            contentGo.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = (RectTransform)viewportGo.transform;
            scroll.content = content;
            return content;
        }

        /// <summary>Attach pointer-enter/exit hover callbacks to any UI object.</summary>
        public static void AddHover(GameObject go,
            UnityEngine.Events.UnityAction onEnter, UnityEngine.Events.UnityAction onExit)
        {
            var relay = go.GetComponent<PointerRelay>() ?? go.AddComponent<PointerRelay>();
            relay.Entered += () => onEnter();
            relay.Exited += () => onExit();
        }

        /// <summary>Make any UI object clickable.</summary>
        public static void AddClick(GameObject go, UnityEngine.Events.UnityAction onClick)
        {
            var relay = go.GetComponent<PointerRelay>() ?? go.AddComponent<PointerRelay>();
            relay.Clicked += () => onClick();
        }

        /// <summary>Dashed arrow with a winged head, built from rotated dash Images.</summary>
        public static void DrawDashedArrow(RectTransform host, Vector2 from, Vector2 to, Color color)
        {
            var direction = to - from;
            float length = direction.magnitude;
            if (length < 30f)
            {
                return;
            }
            var unit = direction / length;
            float angle = Mathf.Atan2(unit.y, unit.x) * Mathf.Rad2Deg;
            for (float d = 18f; d < length - 24f; d += 30f)
            {
                CreateDash(host, from + unit * d, angle, 16f, color);
            }
            // Arrowhead: two wings sweeping back from the tip.
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float wingAngle = angle + sign * 145f;
                var wingDir = new Vector2(
                    Mathf.Cos(wingAngle * Mathf.Deg2Rad), Mathf.Sin(wingAngle * Mathf.Deg2Rad));
                CreateDash(host, to + wingDir * 11f, wingAngle, 22f, color);
            }
        }

        private static void CreateDash(RectTransform host, Vector2 center, float angleDegrees,
            float length, Color color)
        {
            var go = new GameObject("Dash", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(length, 6f);
            rect.anchoredPosition = center;
            rect.localEulerAngles = new Vector3(0, 0, angleDegrees);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        /// <summary>Soft glow layer (hidden by default); ignores parent layout groups.</summary>
        public static GameObject CreateGlow(RectTransform parent, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, float width, float height, Color color)
        {
            var go = new GameObject("Glow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(width, height);
            var image = go.GetComponent<Image>();
            image.sprite = UiSprites.Glow;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            go.SetActive(false);
            return go;
        }

        /// <summary>Single-line text input with placeholder (TMP, body font).</summary>
        public static TMP_InputField CreateInput(Transform parent, string placeholder,
            string initial = "")
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image),
                typeof(TMP_InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var background = go.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            background.color = new Color(0.09f, 0.11f, 0.15f, 0.95f);
            go.GetComponent<LayoutElement>().minHeight = 44;

            // TMP inputs render inside an explicit masked viewport.
            var areaGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            areaGo.transform.SetParent(go.transform, false);
            var area = (RectTransform)areaGo.transform;
            Anchor(area, Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -4));

            var textGo = CreateText(area, "", 18, TextAnchor.MiddleLeft, body: true);
            Anchor((RectTransform)textGo.transform, Vector2.zero, Vector2.one);
            var placeholderGo = CreateText(area, placeholder, 18, TextAnchor.MiddleLeft,
                new Color(0.6f, 0.6f, 0.6f), body: true);
            Anchor((RectTransform)placeholderGo.transform, Vector2.zero, Vector2.one);

            var input = go.GetComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = textGo;
            input.placeholder = placeholderGo;
            input.text = initial;
            return input;
        }

        /// <summary>Small caption under/over a card.</summary>
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

    /// <summary>
    /// Repeats a RawImage's texture at a fixed on-screen size, whatever the rect ends
    /// up being. UGUI has no tiling mode for RawImage, so the repeat count lives in
    /// uvRect and has to be recomputed whenever the rect resizes (window resize, aspect
    /// change). Anchored bottom-left so the pattern stays put as the rect grows upward.
    /// </summary>
    public sealed class TiledBackground : MonoBehaviour
    {
        public float TileWidth = 512f;
        public float TileHeight = 512f;

        private RawImage _image;
        private RectTransform _rect;
        private Vector2 _lastSize = new Vector2(-1, -1);

        private void Awake()
        {
            _image = GetComponent<RawImage>();
            _rect = (RectTransform)transform;
        }

        private void Update()
        {
            var size = _rect.rect.size;
            if (size == _lastSize || TileWidth <= 0f || TileHeight <= 0f)
            {
                return;
            }
            _lastSize = size;
            _image.uvRect = new Rect(0f, 0f, size.x / TileWidth, size.y / TileHeight);
        }
    }
}
