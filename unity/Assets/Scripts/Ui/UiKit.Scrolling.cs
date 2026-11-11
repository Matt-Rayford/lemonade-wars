using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public static partial class UiKit
    {
        /// <summary>Raw wheel deltas are tiny (~0.1 a tick), so the default of 1 crawls;
        /// 50px a tick is roughly one option row per notch.</summary>
        private const float WheelScrollSensitivity = 50f;

        /// <summary>Vertical; returns the content container to fill.</summary>
        public static RectTransform CreateScrollList(RectTransform host)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(host, false);
            Anchor((RectTransform)scrollGo.transform, Vector2.zero, Vector2.one,
                new Vector2(4, 4), new Vector2(-4, -4));
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.25f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = WheelScrollSensitivity;

            var viewport = CreateMaskedViewport(scrollGo.transform);

            var contentGo = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewport, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            // Fresh RectTransforms default to sizeDelta (100,100): with stretch anchors
            // that is +100px of WIDTH, center-pivoted, so rows poke 50px past BOTH edges.
            content.sizeDelta = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        /// <summary>Horizontal, for hands and long rows; returns the content container to fill.</summary>
        public static RectTransform CreateScrollRow(RectTransform host)
        {
            var scrollGo = new GameObject("ScrollRow", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(host, false);
            Anchor((RectTransform)scrollGo.transform, Vector2.zero, Vector2.one,
                new Vector2(4, 4), new Vector2(-4, -4));
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.15f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.vertical = false;
            scroll.scrollSensitivity = WheelScrollSensitivity;

            var viewport = CreateMaskedViewport(scrollGo.transform);

            var contentGo = new GameObject("Content", typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewport, false);
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

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        private static RectTransform CreateMaskedViewport(Transform scroll)
        {
            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scroll, false);
            var viewport = (RectTransform)viewportGo.transform;
            Anchor(viewport, Vector2.zero, Vector2.one);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;
            return viewport;
        }

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
    }
}
