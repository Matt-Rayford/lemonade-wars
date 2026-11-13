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

        /// <summary>Stacks an empty choice row and the header over it; returns the row.</summary>
        private RectTransform StackChoiceRow(RectTransform screen, string header, ref float stackTopPx)
        {
            var row = CreateButtonRow(screen, header + " choices", Vector2.zero, Vector2.one);
            StackOnBaseline(row, MenuButtonHeightPx, ref stackTopPx);
            var headerText = UiKit.CreateText(screen, header, ChoiceFontSize,
                TextAnchor.MiddleCenter, ChoiceHeaderColor);
            StackOnBaseline((RectTransform)headerText.transform, ChoiceHeaderHeightPx, ref stackTopPx);
            return row;
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
