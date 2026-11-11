using System.Collections.Generic;
using LemonadeWars.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public sealed partial class LobbyUi
    {
        private void BuildMyGames(RectTransform canvasRoot)
        {
            _gamesRoot = UiKit.CreatePanel(canvasRoot, "MyGames", UiKit.TableColor);
            UiKit.Anchor(_gamesRoot, Vector2.zero, Vector2.one);
            CreateScreenTitle(_gamesRoot, "MY GAMES");

            _gamesList = CreateSeatList(_gamesRoot, new Vector2(0.24f, 0.18f), new Vector2(0.76f, 0.72f));

            var gamesActions = CreateButtonRow(_gamesRoot, "GamesActions",
                new Vector2(0.34f, 0.05f), new Vector2(0.66f, 0.13f));
            UiKit.CreateButton(gamesActions, "Back", 20, () => OnGamesBack?.Invoke());
            UiKit.CreateButton(gamesActions, "Refresh", 20, () => OnGamesRefresh?.Invoke());
            _gamesRoot.gameObject.SetActive(false);
        }

        /// <param name="onPick">Receives the room code of the clicked game, to re-enter it.</param>
        public void ShowGames(IReadOnlyList<GameSummary> games, System.Action<string> onPick)
        {
            ActivateOnly(_gamesRoot);
            UiKit.Clear(_gamesList);
            if (games == null || games.Count == 0)
            {
                AddSeatRow(_gamesList, "No games yet — host a room or join one!", null);
                return;
            }
            foreach (var game in games)
            {
                BuildGameRow(game, onPick);
            }
        }

        private void BuildGameRow(GameSummary game, System.Action<string> onPick)
        {
            var row = new GameObject("GameRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(_gamesList, false);
            var background = row.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            var idle = new Color(0, 0, 0, 0.30f);
            var hover = new Color(0.20f, 0.24f, 0.32f, 0.65f);
            background.color = idle;
            row.GetComponent<LayoutElement>().minHeight = 56;

            var label = UiKit.CreateText(row.transform,
                $"{game.Code}   {string.Join(", ", game.Players)}", 19, TextAnchor.MiddleLeft);
            label.raycastTarget = false;
            UiKit.Anchor((RectTransform)label.transform, Vector2.zero, Vector2.one,
                new Vector2(16, 2), new Vector2(-200, -2));

            var (status, statusColor, isShouty) = DescribeGameStatus(game);
            var statusText = UiKit.CreateText(row.transform, status, isShouty ? 19 : 16,
                TextAnchor.MiddleRight, statusColor, body: !isShouty);
            statusText.raycastTarget = false;
            UiKit.Anchor((RectTransform)statusText.transform, Vector2.zero, Vector2.one,
                new Vector2(16, 2), new Vector2(-16, -2));

            string code = game.Code;
            UiKit.AddHover(row,
                () => background.color = hover,
                () => background.color = idle);
            UiKit.AddClick(row, () => onPick?.Invoke(code));
        }

        private static (string Status, Color Color, bool IsShouty) DescribeGameStatus(GameSummary game)
        {
            if (game.Finished)
            {
                return ("finished", new Color(0.6f, 0.6f, 0.6f), false);
            }
            if (!game.Started)
            {
                return ("in lobby", new Color(0.65f, 0.75f, 0.85f), false);
            }
            if (game.YourTurn)
            {
                return ("YOUR TURN", UiKit.ButtonColor, true);
            }
            return ($"{game.TurnPlayerName}'s turn", new Color(0.75f, 0.75f, 0.75f), false);
        }
    }
}
