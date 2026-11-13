using System.IO;
using LemonadeWars.Engine.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The online action timer as this client sees it: telling the server when the table has
    /// settled, the viewer's own countdown in the message strip, and the clean-up when it runs out.
    /// </summary>
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>The table must sit idle this long before it counts as settled: theatre hands over between frames.</summary>
        private const float SettledAfterIdleSeconds = 0.3f;
        private const int UrgentCountdownSeconds = 10;
        private const float OwnCountdownWidthPx = 130f;
        private const float OwnCountdownIconPx = 34f;
        private const float TimeUpNoticeSeconds = 5f;
        private static readonly Color UrgentCountdownColor = new Color(1f, 0.38f, 0.32f);

        private RectTransform _ownCountdown;
        private TMP_Text _ownCountdownText;
        private int _ownCountdownShownSeconds = -1;
        private float _tableIdleSince = -1f;

        /// <summary>At the message strip's left end, clear of its centred words.</summary>
        private void BuildOwnCountdown(RectTransform root, RectTransform messageRect)
        {
            var countdownGo = new GameObject("OwnActionCountdown", typeof(RectTransform));
            countdownGo.transform.SetParent(root, false);
            _ownCountdown = (RectTransform)countdownGo.transform;
            float stripMiddle = (messageRect.anchorMin.y + messageRect.anchorMax.y) / 2f;
            _ownCountdown.anchorMin = _ownCountdown.anchorMax = new Vector2(messageRect.anchorMin.x, stripMiddle);
            _ownCountdown.pivot = new Vector2(0f, 0.5f);
            _ownCountdown.sizeDelta = new Vector2(OwnCountdownWidthPx, OwnCountdownIconPx);

            var hourglass = UiKit.LoadTextureSharp(
                Path.Combine(Application.streamingAssetsPath, "icons", "Hourglass.png"));
            if (hourglass != null)
            {
                var iconGo = new GameObject("Hourglass", typeof(RectTransform), typeof(RawImage));
                iconGo.transform.SetParent(_ownCountdown, false);
                var icon = (RectTransform)iconGo.transform;
                icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                icon.pivot = new Vector2(0f, 0.5f);
                icon.sizeDelta = new Vector2(OwnCountdownIconPx, OwnCountdownIconPx);
                var iconImage = iconGo.GetComponent<RawImage>();
                iconImage.texture = hourglass;
                iconImage.raycastTarget = false;
            }

            _ownCountdownText = UiKit.CreateText(_ownCountdown, "", Mathf.RoundToInt(_tableMessage.fontSize),
                TextAnchor.MiddleLeft, UiKit.ButtonColor);
            _ownCountdownText.raycastTarget = false;
            UiKit.AddTextShadow(_ownCountdownText);
            UiKit.Anchor((RectTransform)_ownCountdownText.transform, Vector2.zero, Vector2.one,
                new Vector2(OwnCountdownIconPx + 8f, 0f), Vector2.zero);
            _ownCountdown.gameObject.SetActive(false);
        }

        /// <summary>Runs every frame, on every screen: the countdown must also disappear on the way out of a game.</summary>
        private void TickActionTimer()
        {
            bool atOnlineTable = _remote != null && _screen == Screen.Game && View != null;
            if (atOnlineTable)
            {
                ReportTableSettledOnceIdle();
            }
            ShowOwnCountdown(atOnlineTable ? _remote.ActionSecondsLeft(_remote.Seat) : null);
        }

        /// <summary>
        /// The server holds the viewer's countdown until everything that led to their wait has
        /// been shown here. The Your Turn banner waits on the player, not on the table, so the
        /// clock runs under it.
        /// </summary>
        private void ReportTableSettledOnceIdle()
        {
            bool stillShowing = _renderedRevision != _session.Revision ||
                (!_turnBanner.IsOpen &&
                 (_dice.IsBusy || _fx.IsBusy || _playTheatre.IsBusy || TurnHandoverPending()));
            if (!_remote.OwesSettledReport || stillShowing)
            {
                _tableIdleSince = -1f;
                return;
            }
            if (_tableIdleSince < 0f)
            {
                _tableIdleSince = Time.time;
            }
            if (Time.time - _tableIdleSince >= SettledAfterIdleSeconds)
            {
                _tableIdleSince = -1f;
                _remote.ReportTableSettled();
            }
        }

        private void ShowOwnCountdown(float? secondsLeft)
        {
            _ownCountdown.gameObject.SetActive(secondsLeft != null);
            if (secondsLeft == null)
            {
                _ownCountdownShownSeconds = -1;
                return;
            }
            int wholeSeconds = Mathf.CeilToInt(secondsLeft.Value);
            if (wholeSeconds == _ownCountdownShownSeconds)
            {
                return;
            }
            _ownCountdownShownSeconds = wholeSeconds;
            _ownCountdownText.text = wholeSeconds.ToString();
            _ownCountdownText.color = wholeSeconds <= UrgentCountdownSeconds
                ? UrgentCountdownColor
                : UiKit.ButtonColor;
        }

        /// <summary>
        /// The table has just acted for the viewer: whatever they had half-open answers a
        /// question that is no longer being asked.
        /// </summary>
        private void HandleOwnActionTimerExpired()
        {
            // An unanswered Your Turn banner would otherwise sit over a turn that has already been taken.
            _turnBanner.Hide();
            _prompt.Hide();
            _picker.Hide();
            _reaction.Hide();
            _table.DismissModalOverlays();
            _autoModalOpen = false;
            _modalSignature = "";
            ReopenHeldModals();
            _alertText.text = "TIME'S UP — PLAY MOVED ON WITHOUT YOU";
            _alertUntil = Time.time + TimeUpNoticeSeconds;
            _alertGroup.blocksRaycasts = true;
        }
    }
}
