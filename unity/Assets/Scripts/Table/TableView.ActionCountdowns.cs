using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // The action timer on the player bars: a pill of seconds at the foot of each awaited avatar.
    public sealed partial class TableView
    {
        /// <summary>Seconds left on a player's action timer; null while theirs is not counting down.</summary>
        public System.Func<int, float?> ActionSecondsLeft;
        private readonly List<(int PlayerId, Image Pill, TMP_Text Seconds)> _countdownPills =
            new List<(int, Image, TMP_Text)>();

        private static readonly Vector2 CountdownPillSizePx = new Vector2(38f, 20f);
        /// <summary>The pill's centre above the avatar's lowest point: it straddles the rim, inside the bar.</summary>
        private const float CountdownPillRisePx = 3f;
        private const int CountdownFontSize = 15;
        private const int UrgentCountdownSeconds = 10;
        private static readonly Color UrgentCountdownColor = new Color(0.80f, 0.20f, 0.18f);

        /// <summary>The seconds left on this player's action timer, on a pill at the foot of their avatar.</summary>
        private void AddCountdownPill(RectTransform avatar, int playerId)
        {
            var pillGo = new GameObject("ActionCountdown", typeof(RectTransform), typeof(Image));
            pillGo.transform.SetParent(avatar, false);
            var pillRect = (RectTransform)pillGo.transform;
            pillRect.anchorMin = pillRect.anchorMax = new Vector2(0.5f, 0f);
            pillRect.sizeDelta = CountdownPillSizePx;
            pillRect.anchoredPosition = new Vector2(0f, CountdownPillRisePx);
            var pill = pillGo.GetComponent<Image>();
            pill.sprite = UiSprites.RoundedRect;
            pill.type = Image.Type.Sliced;
            pill.pixelsPerUnitMultiplier = 2f; // a 20px-tall pill wants half the sprite's corner radius
            pill.raycastTarget = false;

            var seconds = UiKit.CreateText(pillGo.transform, "", CountdownFontSize,
                TextAnchor.MiddleCenter, Color.white);
            seconds.raycastTarget = false;
            UiKit.Anchor((RectTransform)seconds.transform, Vector2.zero, Vector2.one);
            _countdownPills.Add((playerId, pill, seconds));
            TickActionCountdowns();
        }

        /// <summary>Every frame: the bars are only rebuilt when the game changes, and the seconds tick in between.</summary>
        public void TickActionCountdowns()
        {
            foreach (var (playerId, pill, seconds) in _countdownPills)
            {
                if (pill == null)
                {
                    continue; // its bar was torn down this frame
                }
                float? secondsLeft = ActionSecondsLeft?.Invoke(playerId);
                pill.gameObject.SetActive(secondsLeft != null);
                if (secondsLeft == null)
                {
                    continue;
                }
                int wholeSeconds = Mathf.CeilToInt(secondsLeft.Value);
                string shown = wholeSeconds.ToString();
                if (seconds.text != shown)
                {
                    seconds.text = shown;
                    pill.color = wholeSeconds <= UrgentCountdownSeconds ? UrgentCountdownColor : CardInkColor;
                }
            }
        }
    }
}
