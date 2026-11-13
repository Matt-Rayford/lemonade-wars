using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The action timer on the player bars: over an awaited avatar, an hourglass whose sand runs
    // out with the player's time, and the seconds left written across it.
    public sealed partial class TableView
    {
        /// <summary>A player's running action timer; null while theirs is not counting down.</summary>
        public System.Func<int, (float SecondsLeft, float TotalSeconds)?> ActionCountdownOf;

        private const float CountdownGlassPx = 50f;
        private const int CountdownFontSize = 22;
        private const float CountdownDigitBorderPx = 2f;
        private const int UrgentCountdownSeconds = 10;
        /// <summary>Paler than the glass: at this size the art's own yellow sand melts into its yellow rim.</summary>
        private static readonly Color SandColor = new Color(1f, 0.96f, 0.78f);
        private static readonly Color UrgentSandColor = new Color(1f, 0.38f, 0.32f);

        // Where each bulb's sand starts and stops on the icon's canvas, as fractions up from its
        // bottom edge. Printed by tools/make_hourglass_sand.py, which draws the sand.
        private const float UpperSandFloor = 0.500f;
        private const float UpperSandBrim = 0.851f;
        private const float LowerSandFloor = 0.119f;
        private const float LowerSandBrim = 0.500f;

        private readonly List<(int PlayerId, GameObject Glass, Image UpperSand, Image LowerSand, TMP_Text[] Digits)>
            _countdownGlasses = new List<(int, GameObject, Image, Image, TMP_Text[])>();

        private Texture2D _emptyHourglassIcon;
        private Sprite _upperSand;
        private Sprite _lowerSand;

        /// <summary>Layers in draw order: the sand sits behind the glass, the seconds in front of everything.</summary>
        private void AddCountdownGlass(RectTransform avatar, int playerId)
        {
            ShadeAvatar(avatar);
            var glassGo = new GameObject("ActionCountdown", typeof(RectTransform));
            glassGo.transform.SetParent(avatar, false);
            var glass = (RectTransform)glassGo.transform;
            glass.anchorMin = glass.anchorMax = new Vector2(0.5f, 0.5f);
            glass.sizeDelta = new Vector2(CountdownGlassPx, CountdownGlassPx);

            var lowerSand = AddSand(glass, _lowerSand);
            var upperSand = AddSand(glass, _upperSand);
            AddGlassFrame(glass);
            var digits = UiKit.CreateOutlinedText(glass, "", CountdownFontSize,
                Color.white, CardInkColor, CountdownDigitBorderPx);
            UiKit.Anchor(digits, Vector2.zero, Vector2.one);

            _countdownGlasses.Add((playerId, glassGo, upperSand, lowerSand,
                digits.GetComponentsInChildren<TMP_Text>()));
            TickActionCountdowns();
        }

        /// <summary>Null when the sand art is missing: the glass then counts down by its number alone.</summary>
        private static Image AddSand(RectTransform glass, Sprite sandSprite)
        {
            if (sandSprite == null)
            {
                return null;
            }
            var sandGo = new GameObject("Sand", typeof(RectTransform), typeof(Image));
            sandGo.transform.SetParent(glass, false);
            UiKit.Anchor((RectTransform)sandGo.transform, Vector2.zero, Vector2.one);
            var sand = sandGo.GetComponent<Image>();
            sand.sprite = sandSprite;
            sand.type = Image.Type.Filled;
            sand.fillMethod = Image.FillMethod.Vertical;
            sand.fillOrigin = (int)Image.OriginVertical.Bottom;
            sand.raycastTarget = false;
            return sand;
        }

        private void AddGlassFrame(RectTransform glass)
        {
            var frameGo = new GameObject("Glass", typeof(RectTransform), typeof(RawImage));
            frameGo.transform.SetParent(glass, false);
            UiKit.Anchor((RectTransform)frameGo.transform, Vector2.zero, Vector2.one);
            var frame = frameGo.GetComponent<RawImage>();
            frame.texture = _emptyHourglassIcon != null ? _emptyHourglassIcon : _hourglassIcon;
            frame.raycastTarget = false;
        }

        /// <summary>Every frame: the bars are only rebuilt when the game changes, and the sand runs in between.</summary>
        public void TickActionCountdowns()
        {
            foreach (var (playerId, glass, upperSand, lowerSand, digits) in _countdownGlasses)
            {
                if (glass == null)
                {
                    continue; // its bar was torn down this frame
                }
                var countdown = ActionCountdownOf?.Invoke(playerId);
                glass.SetActive(countdown != null);
                if (countdown == null)
                {
                    continue;
                }
                var (secondsLeft, totalSeconds) = countdown.Value;
                float sandLeft = totalSeconds > 0f ? Mathf.Clamp01(secondsLeft / totalSeconds) : 0f;
                int wholeSeconds = Mathf.CeilToInt(secondsLeft);
                var sandColor = wholeSeconds <= UrgentCountdownSeconds ? UrgentSandColor : SandColor;
                if (upperSand != null)
                {
                    upperSand.fillAmount = Mathf.Lerp(UpperSandFloor, UpperSandBrim, sandLeft);
                    upperSand.color = sandColor;
                }
                if (lowerSand != null)
                {
                    lowerSand.fillAmount = Mathf.Lerp(LowerSandFloor, LowerSandBrim, 1f - sandLeft);
                    lowerSand.color = sandColor;
                }
                string shown = wholeSeconds.ToString();
                if (digits[0].text != shown)
                {
                    foreach (var layer in digits)
                    {
                        layer.text = shown;
                    }
                }
            }
        }
    }
}
