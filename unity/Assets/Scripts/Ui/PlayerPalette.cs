using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// One colour per seat, fixed once the lobby seats the players: seat 1 is purple, then
    /// blue, red, orange, pink. Every client derives it from the seat number, so a player
    /// is the same colour on everyone's screen without the server sending anything.
    /// </summary>
    public static class PlayerPalette
    {
        /// <summary>The table before anyone is seated: menus and the lobby.</summary>
        public static readonly Color Menu = UiKit.TableColor;

        /// <summary>A player's bar wears the chip colour this strongly: dark enough for white text, and the chip still stands out on it.</summary>
        private const float BarStrength = 0.5f;

        // Table colours all sit at the purple's darkness (matched in OKLCH), so cards, white
        // text and the yellow wallpaper keep the same contrast on every player's table.
        private static readonly (Color Table, Color Chip) Purple =
            (UiKit.TableColor, new Color32(124, 77, 191, 255));
        private static readonly (Color Table, Color Chip) Blue =
            (new Color32(6, 36, 70, 255), new Color32(41, 121, 199, 255));
        private static readonly (Color Table, Color Chip) Red =
            (new Color32(70, 10, 16, 255), new Color32(196, 58, 64, 255));
        private static readonly (Color Table, Color Chip) Orange =
            (new Color32(74, 34, 4, 255), new Color32(214, 122, 31, 255));
        private static readonly (Color Table, Color Chip) Pink =
            (new Color32(70, 11, 52, 255), new Color32(209, 82, 156, 255));

        private static readonly (Color Table, Color Chip)[] BySeat = { Purple, Blue, Red, Orange, Pink };

        /// <summary>The background of the whole screen while this player's board is shown.</summary>
        public static Color TableColorOf(int seat) => ColorsOf(seat).Table;

        /// <summary>The player's avatar chip, wherever it appears.</summary>
        public static Color ChipColorOf(int seat) => ColorsOf(seat).Chip;

        /// <summary>The player's bar: their seat row in game setup and their bar at the table.</summary>
        public static Color BarColorOf(int seat)
        {
            var chip = ColorsOf(seat).Chip;
            return new Color(chip.r, chip.g, chip.b, BarStrength);
        }

        private static (Color Table, Color Chip) ColorsOf(int seat) =>
            BySeat[Mathf.Abs(seat) % BySeat.Length];
    }
}
