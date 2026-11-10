using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class LemonadeWarsApp
    {
        private const float TableColorFadeSeconds = 0.25f;

        private Color _tableColorTarget = TablePalette.Home;
        private Color _tableColorFadeFrom = TablePalette.Home;
        private float _tableColorFadeStartedAt = float.NegativeInfinity;

        /// <summary>
        /// The camera's clear colour is the table, so fading it recolours the whole
        /// screen. Runs on every screen: leaving a game mid-view fades back home.
        /// </summary>
        private void TickTableColor()
        {
            var tableCamera = Camera.main;
            if (tableCamera == null)
            {
                return;
            }

            var wanted = TableColorForViewedBoard();
            if (wanted != _tableColorTarget)
            {
                _tableColorFadeFrom = tableCamera.backgroundColor;
                _tableColorTarget = wanted;
                _tableColorFadeStartedAt = Time.unscaledTime;
            }

            float fadeProgress =
                (Time.unscaledTime - _tableColorFadeStartedAt) / TableColorFadeSeconds;
            tableCamera.backgroundColor =
                Color.Lerp(_tableColorFadeFrom, _tableColorTarget, fadeProgress);
        }

        private Color TableColorForViewedBoard()
        {
            var view = View;
            if (_screen != Screen.Game || view == null)
            {
                return TablePalette.Home;
            }

            int viewedPlayer = _table.ViewedBoardPlayer;
            if (viewedPlayer < 0 || viewedPlayer == view.ViewerId)
            {
                return TablePalette.Home;
            }

            int playerCount = view.Players.Count;
            int seatsAfterViewer = (viewedPlayer - view.ViewerId + playerCount) % playerCount;
            return TablePalette.ForRival(seatsAfterViewer);
        }
    }
}
