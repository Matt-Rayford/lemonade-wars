using UnityEngine;

namespace LemonadeWars.Unity
{
    // The lobby's action timer row: how long an awaited player has before the table acts for them.
    public sealed partial class LobbyUi
    {
        private const int NoActionTimer = 0;
        /// <summary>Left to right, as the row shows them.</summary>
        private static readonly int[] ActionTimerChoices = { NoActionTimer, 60, 30, 15 };
        private const float InfinityIconHeightPx = 24f;

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

        /// <summary>The host's row: every choice is a live button but the one already chosen.</summary>
        private void ShowActionTimerChoices(int selectedSeconds)
        {
            UiKit.Clear(_timerChoiceRow);
            foreach (int seconds in ActionTimerChoices)
            {
                int choice = seconds;
                bool isSelected = seconds == selectedSeconds;
                var onPick = isSelected ? (System.Action)null : () => OnSetActionTimer?.Invoke(choice);
                if (seconds == NoActionTimer && InfinityIcon != null)
                {
                    AddIconChoice(_timerChoiceRow, InfinityIcon, InfinityIconHeightPx, isSelected, onPick);
                }
                else
                {
                    AddChoice(_timerChoiceRow, TimerWords(seconds), isSelected, onPick);
                }
            }
        }

        /// <summary>A guest's line: "ACTION TIMER: 60S", or the infinity icon for no limit.</summary>
        private static void ShowActionTimerSetting(RectTransform line, int seconds)
        {
            bool showsInfinity = seconds == NoActionTimer && InfinityIcon != null;
            ShowSettingLine(line, "ACTION TIMER", TimerWords(seconds),
                showsInfinity ? InfinityIcon : null, InfinityIconHeightPx);
        }

        private static string TimerWords(int seconds) => seconds == NoActionTimer ? "NO LIMIT" : $"{seconds}s";
    }
}
