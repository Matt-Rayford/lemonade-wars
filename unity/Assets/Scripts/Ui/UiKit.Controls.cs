using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public static partial class UiKit
    {
        private const float SliderHandleWidthPx = 26f;

        /// <param name="dark">Inverse chrome for a button ON a light surface, where the
        /// normal lemonade-yellow hover paint would vanish into the background.</param>
        /// <param name="clickSound">Clip name, or null for a button with its own audio.</param>
        /// <param name="highlighted">The call-to-action look: lemonade-yellow at rest
        /// (what the others only become on hover).</param>
        public static Button CreateButton(Transform parent, string label, int fontSize,
            UnityEngine.Events.UnityAction onClick, bool dark = false,
            string clickSound = Sfx.ButtonClick, bool highlighted = false)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 34;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            if (!string.IsNullOrEmpty(clickSound))
            {
                button.onClick.AddListener(() => Sfx.Play(clickSound));
            }
            button.onClick.AddListener(onClick);

            var idleBackground = highlighted ? ButtonColor
                : dark ? new Color(0.10f, 0.12f, 0.16f, 0.94f)
                : new Color(0.58f, 0.61f, 0.67f, 0.32f);
            var idleText = highlighted ? ButtonTextColor
                : dark ? new Color(0.96f, 0.94f, 0.86f) : new Color(0.93f, 0.93f, 0.90f);
            var hoverBackground = highlighted ? new Color(1f, 0.90f, 0.32f)
                : dark ? new Color(0.18f, 0.21f, 0.28f, 1f) : ButtonColor;
            var hoverText = dark ? ButtonColor : ButtonTextColor;
            var image = go.GetComponent<Image>();
            image.sprite = UiSprites.RoundedRect;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 14f / 10f; // ~10px corner radius
            image.color = idleBackground;

            var text = CreateText(go.transform, label, fontSize, TextAnchor.MiddleCenter, idleText);
            Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(10, 2), new Vector2(-10, -2));

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

        /// <summary>Whole-number steps: the value IS the step index, so dragging can only land
        /// on a legal stop (no rounding feedback loops). Subscribe to onValueChanged for it.</summary>
        public static Slider CreateSlider(Transform parent, int steps, int initialStep)
        {
            var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 40;
            var slider = go.GetComponent<Slider>();
            slider.transition = Selectable.Transition.None;

            CreateSliderTrack(go.transform);
            var fill = CreateSliderFill(go.transform);
            var handle = CreateSliderHandle(go.transform);

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

        private static void CreateSliderTrack(Transform slider)
        {
            var track = CreatePanel(slider, "Track", new Color(0.09f, 0.11f, 0.15f, 0.95f));
            track.GetComponent<Image>().sprite = UiSprites.RoundedRect;
            track.GetComponent<Image>().type = Image.Type.Sliced;
            Anchor(track, new Vector2(0, 0.34f), new Vector2(1, 0.66f),
                new Vector2(SliderHandleWidthPx / 2f, 0), new Vector2(-SliderHandleWidthPx / 2f, 0));
        }

        private static RectTransform CreateSliderFill(Transform slider)
        {
            // Inset by the handle radius so the fill lines up with the handle.
            var fillArea = CreatePanel(slider, "FillArea", new Color(0, 0, 0, 0));
            fillArea.GetComponent<Image>().raycastTarget = false;
            Anchor(fillArea, new Vector2(0, 0.34f), new Vector2(1, 0.66f),
                new Vector2(SliderHandleWidthPx / 2f, 0), new Vector2(-SliderHandleWidthPx / 2f, 0));
            var fill = CreatePanel(fillArea, "Fill", ButtonColor);
            fill.GetComponent<Image>().sprite = UiSprites.RoundedRect;
            fill.GetComponent<Image>().type = Image.Type.Sliced;
            fill.GetComponent<Image>().raycastTarget = false;
            fill.anchorMin = new Vector2(0, 0);
            fill.anchorMax = new Vector2(0, 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            return fill;
        }

        private static RectTransform CreateSliderHandle(Transform slider)
        {
            var handleArea = CreatePanel(slider, "HandleArea", new Color(0, 0, 0, 0));
            handleArea.GetComponent<Image>().raycastTarget = false;
            Anchor(handleArea, Vector2.zero, Vector2.one,
                new Vector2(SliderHandleWidthPx / 2f, 0), new Vector2(-SliderHandleWidthPx / 2f, 0));
            var handle = CreatePanel(handleArea, "Handle", new Color(0.98f, 0.90f, 0.55f));
            handle.GetComponent<Image>().sprite = UiSprites.Circle;
            handle.GetComponent<Image>().preserveAspect = true;
            handle.anchorMin = new Vector2(0, 0.5f);
            handle.anchorMax = new Vector2(0, 0.5f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(SliderHandleWidthPx, SliderHandleWidthPx);
            return handle;
        }

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
    }
}
