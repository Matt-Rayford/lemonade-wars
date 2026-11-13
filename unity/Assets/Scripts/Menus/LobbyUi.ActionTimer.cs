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

        private void ShowActionTimerChoices(int selectedSeconds, bool isHost)
        {
            UiKit.Clear(_timerChoiceRow);
            foreach (int seconds in ActionTimerChoices)
            {
                int choice = seconds;
                bool isSelected = seconds == selectedSeconds;
                var onPick = isHost && !isSelected ? () => OnSetActionTimer?.Invoke(choice) : (System.Action)null;
                if (seconds == NoActionTimer && InfinityIcon != null)
                {
                    AddIconChoice(_timerChoiceRow, InfinityIcon, InfinityIconHeightPx, isSelected, onPick);
                }
                else
                {
                    AddChoice(_timerChoiceRow, seconds == NoActionTimer ? "NO LIMIT" : $"{seconds}s",
                        isSelected, onPick);
                }
            }
        }
    }
}
