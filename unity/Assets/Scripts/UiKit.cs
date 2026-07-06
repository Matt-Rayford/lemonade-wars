using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>Small helpers for building the debug HUD entirely from code.</summary>
    public static class UiKit
    {
        public static readonly Color PanelColor = new Color(0.10f, 0.12f, 0.16f, 0.92f);
        public static readonly Color ButtonColor = new Color(0.98f, 0.83f, 0.10f);
        public static readonly Color ButtonTextColor = new Color(0.12f, 0.10f, 0.05f);

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

        public static Button CreateButton(Transform parent, string label, int fontSize,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 34;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);

            // Subtle translucent grey at rest (light text); lemonade-yellow with dark
            // text when the cursor invites it — same language as the prompt options.
            var idleBackground = new Color(0.58f, 0.61f, 0.67f, 0.32f);
            var idleText = new Color(0.93f, 0.93f, 0.90f);
            var image = go.GetComponent<Image>();
            image.color = idleBackground;

            var text = CreateText(go.transform, label, fontSize, TextAnchor.MiddleLeft, idleText);
            Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 2), new Vector2(-6, -2));

            AddHover(go,
                () =>
                {
                    image.color = ButtonColor;
                    text.color = ButtonTextColor;
                },
                () =>
                {
                    image.color = idleBackground;
                    text.color = idleText;
                });
            return button;
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
                Object.Destroy(container.GetChild(i).gameObject);
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
}
