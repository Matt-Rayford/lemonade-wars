using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The online action timer as this client sees it: telling the server when the table has
    /// settled, and the clean-up when the viewer's own time runs out. The countdowns themselves
    /// are drawn on the player bars (TableView.ActionCountdowns).
    /// </summary>
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>The table must sit idle this long before it counts as settled: theatre hands over between frames.</summary>
        private const float SettledAfterIdleSeconds = 0.3f;
        private const float TimeUpNoticeSeconds = 5f;

        private float _tableIdleSince = -1f;

        /// <summary>
        /// The server holds the viewer's countdown until everything that led to their wait has
        /// been shown here. The Your Turn banner waits on the player, not on the table, so the
        /// clock runs under it.
        /// </summary>
        private void ReportTableSettledOnceIdle()
        {
            if (_remote == null || View == null)
            {
                return;
            }
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

        private void ShowClockOnTurnBanner()
        {
            if (_turnBanner.IsOpen)
            {
                _turnBanner.ShowSecondsLeft(_remote?.ActionSecondsLeft(_remote.Seat) is float secondsLeft
                    ? Mathf.CeilToInt(secondsLeft)
                    : (int?)null);
            }
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
