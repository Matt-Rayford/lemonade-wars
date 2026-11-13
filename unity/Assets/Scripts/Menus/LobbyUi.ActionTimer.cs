using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The lobby's action timer row: how long an awaited player has before the table acts for them.
    public sealed partial class LobbyUi
    {
        private const int NoActionTimer = 0;
        /// <summary>Left to right, as the row shows them.</summary>
        private static readonly int[] ActionTimerChoices = { NoActionTimer, 60, 30, 15 };
        private const int TimerChoiceFontSize = 24;
        private const float InfinityIconHeightPx = 24f;
        private static readonly Color TimerHeaderColor = new Color(0.8f, 0.8f, 0.8f);
        /// <summary>The plain menu button's resting colours.</summary>
        private static readonly Color TimerChoiceIdle = new Color(0.58f, 0.61f, 0.67f, 0.32f);
        private static readonly Color TimerChoiceIdleInk = new Color(0.93f, 0.93f, 0.90f);

        private static Texture2D s_infinityIcon;
        private static bool s_infinityIconLoaded;

        private RectTransform _timerChoiceRow;

        private static Texture2D InfinityIcon
        {
            get
            {
                if (!s_infinityIconLoaded)
                {
                    s_infinityIconLoaded = true;
                    s_infinityIcon = UiKit.LoadInkIconAsMask("infinity.png");
                }
                return s_infinityIcon;
            }
        }

        private void BuildActionTimerPicker(RectTransform lobbyRoot)
        {
            var header = UiKit.CreateText(lobbyRoot, "ACTION TIMER", 24,
                TextAnchor.MiddleCenter, TimerHeaderColor);
            UiKit.Anchor((RectTransform)header.transform, new Vector2(0.30f, 0.31f), new Vector2(0.70f, 0.35f));
            _timerChoiceRow = CreateButtonRow(lobbyRoot, "ActionTimerChoices",
                new Vector2(0.30f, 0.235f), new Vector2(0.70f, 0.30f));
        }

        /// <summary>Everyone sees the setting; only the host's row can be clicked.</summary>
        private void ShowActionTimerChoices(int selectedSeconds, bool isHost)
        {
            UiKit.Clear(_timerChoiceRow);
            foreach (int seconds in ActionTimerChoices)
            {
                int choice = seconds;
                bool isSelected = seconds == selectedSeconds;
                AddTimerChoice(seconds, isSelected,
                    isHost && !isSelected ? () => OnSetActionTimer?.Invoke(choice) : (System.Action)null);
            }
        }

        private void AddTimerChoice(int seconds, bool isSelected, System.Action onPick)
        {
            var choiceGo = new GameObject("TimerChoice", typeof(RectTransform), typeof(Image));
            choiceGo.transform.SetParent(_timerChoiceRow, false);
            var background = choiceGo.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 14f / 10f; // the menu buttons' corner radius
            var restingFill = isSelected ? UiKit.ButtonColor : TimerChoiceIdle;
            var restingInk = isSelected ? UiKit.ButtonTextColor : TimerChoiceIdleInk;
            background.color = restingFill;

            Graphic ink = seconds == NoActionTimer && InfinityIcon != null
                ? AddInfinityIcon(choiceGo.transform)
                : AddTimerChoiceLabel(choiceGo.transform, seconds);
            ink.color = restingInk;
            ink.raycastTarget = false;

            if (onPick == null)
            {
                return;
            }
            UiKit.AddHover(choiceGo,
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
            UiKit.AddClick(choiceGo, () =>
            {
                Sfx.Play(Sfx.ButtonClick);
                onPick();
            });
        }

        private static Graphic AddInfinityIcon(Transform choice)
        {
            var iconGo = new GameObject("Infinity", typeof(RectTransform), typeof(RawImage));
            iconGo.transform.SetParent(choice, false);
            var icon = (RectTransform)iconGo.transform;
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
            icon.sizeDelta = new Vector2(
                InfinityIconHeightPx * InfinityIcon.width / InfinityIcon.height, InfinityIconHeightPx);
            var image = iconGo.GetComponent<RawImage>();
            image.texture = InfinityIcon;
            return image;
        }

        private static Graphic AddTimerChoiceLabel(Transform choice, int seconds)
        {
            var label = UiKit.CreateText(choice, seconds == NoActionTimer ? "NO LIMIT" : $"{seconds}s",
                TimerChoiceFontSize, TextAnchor.MiddleCenter);
            UiKit.Anchor((RectTransform)label.transform, Vector2.zero, Vector2.one);
            return label;
        }
    }
}
