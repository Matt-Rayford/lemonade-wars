using LemonadeWars.Protocol;
using TMPro;
using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class LobbyUi
    {
        private const float LobbyStatusHeightPx = 36f;
        /// <summary>Higher than the other screens' titles: this one has the most to fit under it.</summary>
        private const float LobbyTitleBottom = 0.85f;
        private const float LobbyTitleTop = 0.95f;
        private const float LobbySeatsTop = 0.83f;

        private RectTransform _everyoneControls;
        private RectTransform _timerHeader;
        private RectTransform _dibsModeHeader;
        /// <summary>What a guest sees in place of the host's buttons: the settings as words, nothing to click.</summary>
        private RectTransform _guestTimerLine;
        private RectTransform _guestDibsLine;

        /// <summary>Every piece is built once here; <see cref="ArrangeLobbyControls"/> stacks the ones a host or a guest gets.</summary>
        private void BuildLobby(RectTransform canvasRoot)
        {
            _lobbyRoot = UiKit.CreatePanel(canvasRoot, "Lobby", UiKit.TableColor);
            UiKit.Anchor(_lobbyRoot, Vector2.zero, Vector2.one);
            _lobbyTitle = CreateScreenTitle(_lobbyRoot, "");
            UiKit.Anchor((RectTransform)_lobbyTitle.transform,
                new Vector2(0, LobbyTitleBottom), new Vector2(1, LobbyTitleTop));

            _hostControls = CreateButtonRow(_lobbyRoot, "HostControls", Vector2.zero, Vector2.one);
            UiKit.CreateButton(_hostControls, "Start game", 20, () => OnStart?.Invoke());

            _everyoneControls = CreateButtonRow(_lobbyRoot, "EveryoneControls", Vector2.zero, Vector2.one);
            UiKit.CreateButton(_everyoneControls, "Back", 20, () => OnLeave?.Invoke());
            var readyButton = UiKit.CreateButton(_everyoneControls,
                "READY UP", 20, () => OnReadyToggle?.Invoke(!_myReady));
            _readyLabel = readyButton.GetComponentInChildren<TMP_Text>();

            _timerChoiceRow = CreateChoiceRow(_lobbyRoot, "ACTION TIMER", out _timerHeader);
            _lobbyDibsModeRow = CreateChoiceRow(_lobbyRoot, "FIRST DIBS", out _dibsModeHeader);
            _guestTimerLine = CreateSettingLine(_lobbyRoot, "ActionTimerSetting");
            _guestDibsLine = CreateSettingLine(_lobbyRoot, "FirstDibsSetting");

            _lobbyStatus = UiKit.CreateText(_lobbyRoot, "", 18,
                TextAnchor.MiddleCenter, new Color(1f, 0.75f, 0.6f), body: true);
            _lobbySeatList = CreateSeatList(_lobbyRoot,
                new Vector2(StackLeft, StackBaseline), new Vector2(StackRight, LobbySeatsTop));
            ArrangeLobbyControls(isHost: false);
        }

        /// <summary>
        /// Bottom to top. A host: Start Game, Back / Ready Up, the action timer's buttons, First
        /// Dibs' buttons, the status line, the seats. A guest has no Start Game, and reads the two
        /// settings as lines of words: buttons they cannot press only look broken.
        /// </summary>
        private void ArrangeLobbyControls(bool isHost)
        {
            foreach (var hostOnly in new[] { _hostControls, _timerChoiceRow, _timerHeader, _lobbyDibsModeRow, _dibsModeHeader })
            {
                hostOnly.gameObject.SetActive(isHost);
            }
            _guestTimerLine.gameObject.SetActive(!isHost);
            _guestDibsLine.gameObject.SetActive(!isHost);

            float stackTopPx = 0f;
            if (isHost)
            {
                StackOnBaseline(_hostControls, MenuButtonHeightPx, ref stackTopPx);
            }
            StackOnBaseline(_everyoneControls, MenuButtonHeightPx, ref stackTopPx);
            if (isHost)
            {
                StackChoiceRow(_timerChoiceRow, _timerHeader, ref stackTopPx);
                StackChoiceRow(_lobbyDibsModeRow, _dibsModeHeader, ref stackTopPx);
            }
            else
            {
                StackOnBaseline(_guestTimerLine, SettingLineHeightPx, ref stackTopPx);
                StackOnBaseline(_guestDibsLine, SettingLineHeightPx, ref stackTopPx);
            }

            var statusRect = (RectTransform)_lobbyStatus.transform;
            StackOnBaseline(statusRect, LobbyStatusHeightPx, ref stackTopPx);
            // Wider than the buttons: a connection error can be a long sentence.
            statusRect.anchorMin = new Vector2(0.1f, StackBaseline);
            statusRect.anchorMax = new Vector2(0.9f, StackBaseline);
            _lobbySeatList.offsetMin = new Vector2(0f, stackTopPx);
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

            ArrangeLobbyControls(isHost);
            bool isManualDibs = room.FirstDibsMode == FirstDibsModes.Manual;
            if (isHost)
            {
                ShowActionTimerChoices(room.ActionTimerSeconds);
                ShowFirstDibsModes(_lobbyDibsModeRow, isManualDibs, isManual => OnSetFirstDibsMode?.Invoke(
                    isManual ? FirstDibsModes.Manual : FirstDibsModes.Random));
            }
            else
            {
                ShowActionTimerSetting(_guestTimerLine, room.ActionTimerSeconds);
                ShowSettingLine(_guestDibsLine, "FIRST DIBS", isManualDibs ? "MANUAL" : "RANDOM");
            }
            _readyLabel.text = myReady ? "UNREADY" : "READY UP";
            _lobbyStatus.text = status ?? "";
        }
    }
}
