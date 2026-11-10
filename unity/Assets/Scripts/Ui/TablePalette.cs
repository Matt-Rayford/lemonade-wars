using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// One table colour per seat at a five-player table, so whose board is on screen
    /// reads at a glance. Every colour sits at the home purple's darkness (matched in
    /// OKLCH), so cards, white text and the yellow wallpaper keep the same contrast.
    /// </summary>
    public static class TablePalette
    {
        public static readonly Color Home = UiKit.TableColor;

        private static readonly Color Blue = new Color32(6, 36, 70, 255);
        private static readonly Color Red = new Color32(70, 10, 16, 255);
        private static readonly Color Orange = new Color32(74, 34, 4, 255);
        private static readonly Color Pink = new Color32(70, 11, 52, 255);

        private static readonly Color[] RivalColors = { Blue, Red, Orange, Pink };

        /// <param name="seatsAfterViewer">1 for the next player in turn order, the top
        /// bar of the players column, counting down the column from there.</param>
        public static Color ForRival(int seatsAfterViewer)
        {
            return RivalColors[(seatsAfterViewer - 1) % RivalColors.Length];
        }
    }
}
