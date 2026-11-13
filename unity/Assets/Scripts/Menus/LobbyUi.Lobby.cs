using TMPro;
using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class LobbyUi
    {
        private void BuildLobby(RectTransform canvasRoot)
        {
            _lobbyRoot = UiKit.CreatePanel(canvasRoot, "Lobby", UiKit.TableColor);
            UiKit.Anchor(_lobbyRoot, Vector2.zero, Vector2.one);
            _lobbyTitle = CreateScreenTitle(_lobbyRoot, "");

            _lobbySeatList = CreateSeatList(_lobbyRoot, new Vector2(0.30f, 0.26f), new Vector2(0.70f, 0.72f));

            var everyoneControls = CreateButtonRow(_lobbyRoot, "EveryoneControls",
                new Vector2(0.30f, 0.145f), new Vector2(0.70f, 0.225f));
            UiKit.CreateButton(everyoneControls, "Back", 20, () => OnLeave?.Invoke());
            var readyButton = UiKit.CreateButton(everyoneControls,
                "READY UP", 20, () => OnReadyToggle?.Invoke(!_myReady));
            _readyLabel = readyButton.GetComponentInChildren<TMP_Text>();

            _hostControls = CreateButtonRow(_lobbyRoot, "LobbyControls",
                new Vector2(0.3f, 0.05f), new Vector2(0.7f, 0.13f));
            UiKit.CreateButton(_hostControls, "Start game", 20, () => OnStart?.Invoke());

            _lobbyStatus = UiKit.CreateText(_lobbyRoot, "", 18,
                TextAnchor.MiddleCenter, new Color(1f, 0.75f, 0.6f), body: true);
            UiKit.Anchor((RectTransform)_lobbyStatus.transform, new Vector2(0.1f, 0.24f), new Vector2(0.9f, 0.29f));
        }

        public void ShowLobby(RemoteRoomState room, string status, bool myReady)
        {
            ActivateOnly(_lobbyRoot);
            _myReady = myReady;
            _lobbyTitle.text = string.IsNullOrEmpty(room.Code) ? "CONNECTING..." : $"ROOM {room.Code}";

            bool isHost = room.YourSeat == 0;
            UiKit.Clear(_lobbySeatList);
            if (room.Seats.Count == 0)
            {
                AddSeatRow(_lobbySeatList, "Waiting for the server...", null);
            }
            foreach (var seat in room.Seats)
            {
                // Readiness is the row's dot and "you" its yellow rim, so the label spells out neither.
                string label = $"{seat.Seat + 1}. {WithoutLegacyBotSuffix(seat.Name)}" +
                    (seat.IsBot || seat.Connected ? "" : "  (disconnected)");
                int seatIndex = seat.Seat;
                string level = seat.IsBot ? (string.IsNullOrEmpty(seat.BotLevel) ? "medium" : seat.BotLevel) : null;
                AddSeatRow(_lobbySeatList, label,
                    isHost && seat.IsBot ? () => OnRemoveBotSeat?.Invoke(seatIndex) : (System.Action)null,
                    level,
                    isHost && seat.IsBot
                        ? () => OnSetBotLevel?.Invoke(seatIndex, NextLevel(level))
                        : (System.Action)null,
                    ready: seat.IsBot ? (bool?)null : seat.Ready, // bots are always ready: no dot
                    isYou: seat.Seat == room.YourSeat,
                    seat: seat.Seat);
            }
            if (isHost && room.Seats.Count > 0 && room.Seats.Count < MaxSeats)
            {
                UiKit.CreateButton(_lobbySeatList, "+ Add bot", 18, () => OnAddBot?.Invoke());
            }

            _readyLabel.text = myReady ? "UNREADY" : "READY UP";
            _lobbyStatus.text = status ?? "";
            _hostControls.gameObject.SetActive(isHost);
        }
    }
}
