using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class LemonadeWarsApp
    {
        private const float TableColorFadeSeconds = 0.25f;

        private Color _tableColorTarget = PlayerPalette.Menu;
        private Color _tableColorFadeFrom = PlayerPalette.Menu;
        private float _tableColorFadeStartedAt = float.NegativeInfinity;

        /// <summary>
        /// The camera's clear colour is the table, so fading it recolours the whole
        /// screen. Runs on every screen: leaving a game fades back to the menu purple.
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
                return PlayerPalette.Menu;
            }

            int boardOwner = _table.ViewedBoardPlayer >= 0 ? _table.ViewedBoardPlayer : view.ViewerId;
            return PlayerPalette.TableColorOf(boardOwner);
        }
    }
}
