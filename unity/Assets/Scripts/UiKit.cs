using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>Small helpers for building the debug HUD entirely from code.</summary>
    public static class UiKit
    {
        public static readonly Color PanelColor = new Color(0.10f, 0.12f, 0.16f, 0.92f);
        public static readonly Color ButtonColor = new Color(0.98f, 0.83f, 0.10f);
        public static readonly Color ButtonTextColor = new Color(0.12f, 0.10f, 0.05f);

        public static Font DefaultFont =>
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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

        public static Text CreateText(Transform parent, string content, int size,
            TextAnchor align = TextAnchor.UpperLeft, Color? color = null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = DefaultFont;
            text.fontSize = size;
            text.alignment = align;
            text.color = color ?? Color.white;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        public static Button CreateButton(Transform parent, string label, int fontSize,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = ButtonColor;
            go.GetComponent<LayoutElement>().minHeight = 34;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);

            var text = CreateText(go.transform, label, fontSize, TextAnchor.MiddleLeft, ButtonTextColor);
            Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 2), new Vector2(-6, -2));
            return button;
        }

        public static RawImage CreateCardImage(Transform parent, Texture2D texture, float width, float height)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.color = texture == null ? new Color(0.3f, 0.3f, 0.3f) : Color.white;
            var layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;
            return image;
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
            contentGo.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = (RectTransform)viewportGo.transform;
            scroll.content = content;
            return content;
        }

        /// <summary>Attach pointer-enter/exit hover callbacks to any UI object.</summary>
        public static void AddHover(GameObject go,
            UnityEngine.Events.UnityAction onEnter, UnityEngine.Events.UnityAction onExit)
        {
            var trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry
            {
                eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter,
            };
            enter.callback.AddListener(_ => onEnter());
            var exit = new UnityEngine.EventSystems.EventTrigger.Entry
            {
                eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit,
            };
            exit.callback.AddListener(_ => onExit());
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        /// <summary>Make any UI object clickable.</summary>
        public static void AddClick(GameObject go, UnityEngine.Events.UnityAction onClick)
        {
            var trigger = go.GetComponent<UnityEngine.EventSystems.EventTrigger>()
                ?? go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var click = new UnityEngine.EventSystems.EventTrigger.Entry
            {
                eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick,
            };
            click.callback.AddListener(_ => onClick());
            trigger.triggers.Add(click);
        }

        /// <summary>Small caption under/over a card.</summary>
        public static Text CreateBadge(Transform parent, string content, int size, Color background)
        {
            var go = new GameObject("Badge", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = background;
            go.GetComponent<LayoutElement>().minHeight = size + 8;
            var text = CreateText(go.transform, content, size, TextAnchor.MiddleCenter);
            Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(4, 1), new Vector2(-4, -1));
            return text;
        }
    }
}
