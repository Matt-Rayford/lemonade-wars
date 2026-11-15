using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Game options shown as a row of buttons with the current choice in yellow: a header, then
    // the row. Everyone at the table sees the setting; only whoever may change it gets live buttons.
    public sealed partial class LobbyUi
    {
        private const int ChoiceFontSize = 24;
        private const float ChoiceHeaderHeightPx = 40f;
        private static readonly Color ChoiceHeaderColor = new Color(0.8f, 0.8f, 0.8f);
        /// <summary>The plain menu button's resting colours.</summary>
        private static readonly Color ChoiceIdle = new Color(0.58f, 0.61f, 0.67f, 0.32f);
        private static readonly Color ChoiceIdleInk = new Color(0.93f, 0.93f, 0.90f);

        private const float SettingLineHeightPx = 36f;
        private const float SettingLineGapPx = 10f;

        /// <summary>An empty choice row and its header, for the caller to stack.</summary>
        private static RectTransform CreateChoiceRow(RectTransform screen, string header, out RectTransform headerRect)
        {
            var row = CreateButtonRow(screen, header + " choices", Vector2.zero, Vector2.one);
            var headerText = UiKit.CreateText(screen, header, ChoiceFontSize,
                TextAnchor.MiddleCenter, ChoiceHeaderColor);
            headerRect = (RectTransform)headerText.transform;
            return row;
        }

        private static void StackChoiceRow(RectTransform row, RectTransform headerRect, ref float stackTopPx)
        {
            StackOnBaseline(row, MenuButtonHeightPx, ref stackTopPx);
            StackOnBaseline(headerRect, ChoiceHeaderHeightPx, ref stackTopPx);
        }

        // ---- the same settings as one line of words, for players who cannot change them

        /// <summary>An empty centred line, for the caller to stack and <see cref="ShowSettingLine"/> to fill.</summary>
        private static RectTransform CreateSettingLine(RectTransform screen, string name)
        {
            var lineGo = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            lineGo.transform.SetParent(screen, false);
            var layout = lineGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = SettingLineGapPx;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            return (RectTransform)lineGo.transform;
        }

        /// <summary>"FIRST DIBS: RANDOM", the value in the chosen button's yellow. A <paramref name="valueIcon"/> stands in for the value's words.</summary>
        private static void ShowSettingLine(RectTransform line, string name, string value,
            Texture2D valueIcon = null, float valueIconHeightPx = 0f)
        {
            UiKit.Clear(line);
            AddSettingWords(line, name + ":", ChoiceHeaderColor);
            if (valueIcon == null)
            {
                AddSettingWords(line, value, UiKit.ButtonColor);
                return;
            }
            var iconGo = new GameObject("Value", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
            iconGo.transform.SetParent(line, false);
            var element = iconGo.GetComponent<LayoutElement>();
            element.preferredWidth = valueIconHeightPx * valueIcon.width / valueIcon.height;
            element.preferredHeight = valueIconHeightPx;
            var image = iconGo.GetComponent<RawImage>();
            image.texture = valueIcon;
            image.color = UiKit.ButtonColor;
            image.raycastTarget = false;
        }

        private static void AddSettingWords(RectTransform line, string words, Color color)
        {
            var text = UiKit.CreateText(line, words, ChoiceFontSize, TextAnchor.MiddleCenter, color);
            text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            text.raycastTarget = false;
        }

        private static void AddChoice(RectTransform row, string label, bool isSelected, System.Action onPick)
        {
            var choice = AddChoiceBackground(row);
            var text = UiKit.CreateText(choice.transform, label, ChoiceFontSize, TextAnchor.MiddleCenter);
            UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one);
            WireChoice(choice, text, isSelected, onPick);
        }

        private static void AddIconChoice(RectTransform row, Texture2D inkMask, float iconHeightPx,
            bool isSelected, System.Action onPick)
        {
            var choice = AddChoiceBackground(row);
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
            iconGo.transform.SetParent(choice.transform, false);
            var icon = (RectTransform)iconGo.transform;
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
            icon.sizeDelta = new Vector2(iconHeightPx * inkMask.width / inkMask.height, iconHeightPx);
            var image = iconGo.GetComponent<RawImage>();
            image.texture = inkMask;
            WireChoice(choice, image, isSelected, onPick);
        }

        private static Image AddChoiceBackground(RectTransform row)
        {
            var choiceGo = new GameObject("Choice", typeof(RectTransform), typeof(Image));
            choiceGo.transform.SetParent(row, false);
            var background = choiceGo.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 14f / 10f; // the menu buttons' corner radius
            return background;
        }

        /// <summary>A null <paramref name="onPick"/> leaves the choice as a display: no hover, no click.</summary>
        private static void WireChoice(Image background, Graphic ink, bool isSelected, System.Action onPick)
        {
            var restingFill = isSelected ? UiKit.ButtonColor : ChoiceIdle;
            var restingInk = isSelected ? UiKit.ButtonTextColor : ChoiceIdleInk;
            background.color = restingFill;
            ink.color = restingInk;
            ink.raycastTarget = false;
            if (onPick == null)
            {
                return;
            }
            UiKit.AddHover(background.gameObject,
                () =>
                {
                    background.color = UiKit.ButtonColor;
                    ink.color = UiKit.ButtonTextColor;
                },
                () =>
                {
                    background.color = restingFill;
                    ink.color = restingInk;
                });
            UiKit.AddClick(background.gameObject, () =>
            {
                Sfx.Play(Sfx.ButtonClick);
                onPick();
            });
        }
    }
}
