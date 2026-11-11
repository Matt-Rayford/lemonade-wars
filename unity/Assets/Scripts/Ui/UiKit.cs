using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>Widget factory for the code-built UGUI: fonts, colors, panels, buttons,
    /// scroll lists, card images. Every screen builds its chrome through here.</summary>
    public static partial class UiKit
    {
        /// <summary>The whole table's background: every play zone is transparent over it.
        /// #1E0A3D, the Black Market card back's dominant color (Gamma color space, so 1:1 with sRGB).</summary>
        public static readonly Color TableColor = new Color(0.118f, 0.039f, 0.239f);

        public static readonly Color PanelColor = new Color(0.10f, 0.12f, 0.16f, 0.92f);
        public static readonly Color ButtonColor = new Color(0.98f, 0.83f, 0.10f);
        public static readonly Color ButtonTextColor = new Color(0.12f, 0.10f, 0.05f);

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

        public static void Clear(Transform container)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                // Destroy lands at the END of the frame: detach now, or a layout group
                // rebuilt this frame still places the fresh content below the corpses.
                var child = container.GetChild(i);
                child.SetParent(null, false);
                Object.Destroy(child.gameObject);
            }
        }

        public static void AddHover(GameObject go,
            UnityEngine.Events.UnityAction onEnter, UnityEngine.Events.UnityAction onExit)
        {
            var relay = go.GetComponent<PointerRelay>() ?? go.AddComponent<PointerRelay>();
            relay.Entered += () => onEnter();
            relay.Exited += () => onExit();
        }

        public static void AddClick(GameObject go, UnityEngine.Events.UnityAction onClick)
        {
            var relay = go.GetComponent<PointerRelay>() ?? go.AddComponent<PointerRelay>();
            relay.Clicked += () => onClick();
        }

        /// <summary>Hidden until activated; ignores parent layout groups.</summary>
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
    }
}
