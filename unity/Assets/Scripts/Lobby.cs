using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Main menu + online lobby, code-built like everything else. The app supplies the
    /// callbacks; this class only owns widgets.
    /// </summary>
    public sealed class LobbyUi
    {
        public System.Action<IReadOnlyList<(string Name, string Level)>> OnStartSolo; // bots
        public System.Action<string, string> OnHost;          // serverUrl, name
        public System.Action<string, string, string> OnJoin;  // serverUrl, name, code
        public System.Action<string, string> OnMyGames;        // serverUrl, name
        public System.Action OnGamesBack;
        public System.Action OnGamesRefresh;
        public System.Action OnAddBot;
        public System.Action<int> OnRemoveBotSeat;             // seat index
        public System.Action<int, string> OnSetBotLevel;       // seat index, level
        public System.Action OnStart;
        public System.Action<bool> OnReadyToggle;
        public System.Action OnLeave;
        public System.Action<string> OnSaveSettings;           // display name

        public string ServerUrl => _serverInput.text;
        public bool MenuVisible => _menuRoot.gameObject.activeSelf;

        /// <summary>The name used at the table, from Settings; never empty.</summary>
        public string DisplayName =>
            string.IsNullOrWhiteSpace(_displayNameInput.text)
                ? "Player"
                : _displayNameInput.text.Trim();

        private static readonly string[] BotNames = { "Benny", "Cleo", "Dex", "Squeezy" };
        private const int MaxSeats = 5;

        private readonly RectTransform _menuRoot;
        private readonly RectTransform _lobbyRoot;
        private readonly RectTransform _settingsRoot;
        private readonly RectTransform _soloRoot;
        private readonly RectTransform _joinRoot;
        private readonly RectTransform _gamesRoot;
        private readonly RectTransform _gamesList;
        private readonly RectTransform _soloSeatList;
        private readonly List<(string Name, string Level)> _soloBots =
            new List<(string, string)>
            {
                ("Benny", "medium"), ("Cleo", "medium"), ("Dex", "medium"),
            };
        private readonly TMP_InputField _displayNameInput;
        private readonly TMP_InputField _serverInput;
        private readonly TMP_InputField _codeInput;
        private readonly TMP_Text _lobbyTitle;
        private readonly RectTransform _lobbySeatList;
        private readonly TMP_Text _lobbyStatus;
        private readonly TMP_Text _menuStatus;
        private readonly TMP_Text _serverStatus;
        private readonly TMP_Text _readyLabel;
        private readonly RectTransform _hostControls;
        private bool _myReady;

        public LobbyUi(RectTransform canvasRoot, string defaultServerUrl, string defaultName)
        {
            // ------------------------------------------------------- menu
            _menuRoot = UiKit.CreatePanel(canvasRoot, "Menu", new Color(0.08f, 0.10f, 0.14f, 0.97f));
            UiKit.Anchor(_menuRoot, Vector2.zero, Vector2.one);

            var title = UiKit.CreateText(_menuRoot, "LEMONADE WARS", 64,
                TextAnchor.MiddleCenter, new Color(0.98f, 0.83f, 0.10f));
            UiKit.Anchor((RectTransform)title.transform, new Vector2(0, 0.78f), new Vector2(1, 0.95f));

            var column = new GameObject("MenuColumn", typeof(RectTransform), typeof(VerticalLayoutGroup));
            column.transform.SetParent(_menuRoot, false);
            UiKit.Anchor((RectTransform)column.transform, new Vector2(0.34f, 0.16f), new Vector2(0.66f, 0.76f));
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            _serverStatus = UiKit.CreateText(column.transform, "", 14,
                TextAnchor.MiddleCenter, new Color(0.6f, 0.6f, 0.6f), body: true);

            // Main-menu buttons get more presence than the standard 34px rows.
            Button MenuButton(string label, UnityEngine.Events.UnityAction onClick)
            {
                var menuButton = UiKit.CreateButton(column.transform, label, 26, onClick);
                menuButton.GetComponent<LayoutElement>().minHeight = 64;
                return menuButton;
            }

            MenuButton("Play vs bots (offline)", ShowSoloSetup);
            MenuButton("Host online room", () => OnHost?.Invoke(_serverInput.text, DisplayName));
            MenuButton("Join room", ShowJoin);
            MenuButton("My games", () => OnMyGames?.Invoke(_serverInput.text, DisplayName));
            MenuButton("Settings", ShowSettings);

            _menuStatus = UiKit.CreateText(column.transform, "", 16,
                TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.5f), body: true);

            // ---------------------------------------------------- settings
            _settingsRoot = UiKit.CreatePanel(canvasRoot, "Settings", new Color(0.08f, 0.10f, 0.14f, 0.97f));
            UiKit.Anchor(_settingsRoot, Vector2.zero, Vector2.one);

            var settingsTitle = UiKit.CreateText(_settingsRoot, "SETTINGS", 48,
                TextAnchor.MiddleCenter, new Color(0.98f, 0.83f, 0.10f));
            UiKit.Anchor((RectTransform)settingsTitle.transform, new Vector2(0, 0.74f), new Vector2(1, 0.92f));

            var settingsColumn = new GameObject("SettingsColumn",
                typeof(RectTransform), typeof(VerticalLayoutGroup));
            settingsColumn.transform.SetParent(_settingsRoot, false);
            UiKit.Anchor((RectTransform)settingsColumn.transform,
                new Vector2(0.34f, 0.30f), new Vector2(0.66f, 0.70f));
            var settingsLayout = settingsColumn.GetComponent<VerticalLayoutGroup>();
            settingsLayout.spacing = 12;
            settingsLayout.childForceExpandHeight = false;
            settingsLayout.childControlHeight = true;
            settingsLayout.childControlWidth = true;

            var nameLabel = UiKit.CreateText(settingsColumn.transform,
                "Display Name — shown at the table on your turn", 16,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.8f), body: true);
            nameLabel.gameObject.AddComponent<LayoutElement>().minHeight = 24;
            _displayNameInput = UiKit.CreateInput(settingsColumn.transform, "Display Name", defaultName);

            var serverLabel = UiKit.CreateText(settingsColumn.transform,
                "Server URL", 16, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.8f), body: true);
            serverLabel.gameObject.AddComponent<LayoutElement>().minHeight = 24;
            _serverInput = UiKit.CreateInput(settingsColumn.transform, "Server", defaultServerUrl);

            UiKit.CreateButton(settingsColumn.transform, "Save & back", 20, () =>
            {
                OnSaveSettings?.Invoke(DisplayName);
                ShowMenu("");
            });
            _settingsRoot.gameObject.SetActive(false);

            // --------------------------------------------------- join room
            _joinRoot = UiKit.CreatePanel(canvasRoot, "JoinRoom", new Color(0.08f, 0.10f, 0.14f, 0.97f));
            UiKit.Anchor(_joinRoot, Vector2.zero, Vector2.one);

            var joinTitle = UiKit.CreateText(_joinRoot, "JOIN ROOM", 48,
                TextAnchor.MiddleCenter, new Color(0.98f, 0.83f, 0.10f));
            UiKit.Anchor((RectTransform)joinTitle.transform, new Vector2(0, 0.74f), new Vector2(1, 0.92f));

            var codeLabel = UiKit.CreateText(_joinRoot, "ROOM CODE", 24,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.8f));
            UiKit.Anchor((RectTransform)codeLabel.transform, new Vector2(0.3f, 0.60f), new Vector2(0.7f, 0.67f));

            _codeInput = UiKit.CreateInput(_joinRoot, "ABCDE");
            var codeRect = (RectTransform)_codeInput.transform;
            UiKit.Anchor(codeRect, new Vector2(0.37f, 0.46f), new Vector2(0.63f, 0.58f));
            _codeInput.characterLimit = 5;
            var codeText = _codeInput.textComponent;
            codeText.fontSize = 52;
            codeText.alignment = TextAlignmentOptions.Center;
            if (_codeInput.placeholder is TMP_Text codePlaceholder)
            {
                codePlaceholder.fontSize = 52;
                codePlaceholder.alignment = TextAlignmentOptions.Center;
            }

            var joinActions = new GameObject("JoinActions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            joinActions.transform.SetParent(_joinRoot, false);
            UiKit.Anchor((RectTransform)joinActions.transform,
                new Vector2(0.34f, 0.30f), new Vector2(0.66f, 0.38f));
            var joinActionsLayout = joinActions.GetComponent<HorizontalLayoutGroup>();
            joinActionsLayout.spacing = 14;
            joinActionsLayout.childForceExpandWidth = true;
            joinActionsLayout.childForceExpandHeight = true;
            joinActionsLayout.childControlWidth = true;
            joinActionsLayout.childControlHeight = true;
            UiKit.CreateButton((RectTransform)joinActions.transform, "< Back", 20,
                () => ShowMenu(""));
            UiKit.CreateButton((RectTransform)joinActions.transform, "Join", 20,
                () => OnJoin?.Invoke(_serverInput.text, DisplayName,
                    _codeInput.text.Trim().ToUpperInvariant()));
            _joinRoot.gameObject.SetActive(false);

            // -------------------------------------------------- solo setup
            _soloRoot = UiKit.CreatePanel(canvasRoot, "SoloSetup", new Color(0.08f, 0.10f, 0.14f, 0.97f));
            UiKit.Anchor(_soloRoot, Vector2.zero, Vector2.one);

            var soloTitle = UiKit.CreateText(_soloRoot, "PLAY VS BOTS", 48,
                TextAnchor.MiddleCenter, new Color(0.98f, 0.83f, 0.10f));
            UiKit.Anchor((RectTransform)soloTitle.transform, new Vector2(0, 0.74f), new Vector2(1, 0.92f));

            _soloSeatList = CreateSeatList(_soloRoot, new Vector2(0.32f, 0.18f), new Vector2(0.68f, 0.72f));

            var soloActions = new GameObject("SoloActions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            soloActions.transform.SetParent(_soloRoot, false);
            UiKit.Anchor((RectTransform)soloActions.transform,
                new Vector2(0.30f, 0.05f), new Vector2(0.70f, 0.13f));
            var soloActionsLayout = soloActions.GetComponent<HorizontalLayoutGroup>();
            soloActionsLayout.spacing = 14;
            soloActionsLayout.childForceExpandWidth = true;
            soloActionsLayout.childForceExpandHeight = true;
            soloActionsLayout.childControlWidth = true;
            soloActionsLayout.childControlHeight = true;
            UiKit.CreateButton((RectTransform)soloActions.transform, "< Back", 20,
                () => ShowMenu(""));
            UiKit.CreateButton((RectTransform)soloActions.transform, "Start game", 20,
                () => OnStartSolo?.Invoke(_soloBots.ToList()));
            _soloRoot.gameObject.SetActive(false);

            // --------------------------------------------------- my games
            _gamesRoot = UiKit.CreatePanel(canvasRoot, "MyGames", new Color(0.08f, 0.10f, 0.14f, 0.97f));
            UiKit.Anchor(_gamesRoot, Vector2.zero, Vector2.one);

            var gamesTitle = UiKit.CreateText(_gamesRoot, "MY GAMES", 48,
                TextAnchor.MiddleCenter, new Color(0.98f, 0.83f, 0.10f));
            UiKit.Anchor((RectTransform)gamesTitle.transform, new Vector2(0, 0.74f), new Vector2(1, 0.92f));

            _gamesList = CreateSeatList(_gamesRoot, new Vector2(0.24f, 0.18f), new Vector2(0.76f, 0.72f));

            var gamesActions = new GameObject("GamesActions",
                typeof(RectTransform), typeof(HorizontalLayoutGroup));
            gamesActions.transform.SetParent(_gamesRoot, false);
            UiKit.Anchor((RectTransform)gamesActions.transform,
                new Vector2(0.34f, 0.05f), new Vector2(0.66f, 0.13f));
            var gamesActionsLayout = gamesActions.GetComponent<HorizontalLayoutGroup>();
            gamesActionsLayout.spacing = 14;
            gamesActionsLayout.childForceExpandWidth = true;
            gamesActionsLayout.childForceExpandHeight = true;
            gamesActionsLayout.childControlWidth = true;
            gamesActionsLayout.childControlHeight = true;
            UiKit.CreateButton((RectTransform)gamesActions.transform, "< Back", 20,
                () => OnGamesBack?.Invoke());
            UiKit.CreateButton((RectTransform)gamesActions.transform, "Refresh", 20,
                () => OnGamesRefresh?.Invoke());
            _gamesRoot.gameObject.SetActive(false);

            // ------------------------------------------------------ lobby
            _lobbyRoot = UiKit.CreatePanel(canvasRoot, "Lobby", new Color(0.08f, 0.10f, 0.14f, 0.97f));
            UiKit.Anchor(_lobbyRoot, Vector2.zero, Vector2.one);

            _lobbyTitle = UiKit.CreateText(_lobbyRoot, "", 48,
                TextAnchor.MiddleCenter, new Color(0.98f, 0.83f, 0.10f));
            UiKit.Anchor((RectTransform)_lobbyTitle.transform, new Vector2(0, 0.74f), new Vector2(1, 0.92f));

            _lobbySeatList = CreateSeatList(_lobbyRoot, new Vector2(0.30f, 0.26f), new Vector2(0.70f, 0.72f));

            // Everyone: ready toggle + leave. Host additionally: start.
            var everyoneControls = new GameObject("EveryoneControls",
                typeof(RectTransform), typeof(HorizontalLayoutGroup));
            everyoneControls.transform.SetParent(_lobbyRoot, false);
            UiKit.Anchor((RectTransform)everyoneControls.transform,
                new Vector2(0.30f, 0.145f), new Vector2(0.70f, 0.225f));
            var everyoneLayout = everyoneControls.GetComponent<HorizontalLayoutGroup>();
            everyoneLayout.spacing = 14;
            everyoneLayout.childForceExpandWidth = true;
            everyoneLayout.childForceExpandHeight = true;
            everyoneLayout.childControlWidth = true;
            everyoneLayout.childControlHeight = true;
            UiKit.CreateButton((RectTransform)everyoneControls.transform, "< Back", 20,
                () => OnLeave?.Invoke());
            var readyButton = UiKit.CreateButton((RectTransform)everyoneControls.transform,
                "READY UP", 20, () => OnReadyToggle?.Invoke(!_myReady));
            _readyLabel = readyButton.GetComponentInChildren<TMP_Text>();

            var controls = new GameObject("LobbyControls", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            controls.transform.SetParent(_lobbyRoot, false);
            UiKit.Anchor((RectTransform)controls.transform, new Vector2(0.3f, 0.05f), new Vector2(0.7f, 0.13f));
            var controlsLayout = controls.GetComponent<HorizontalLayoutGroup>();
            controlsLayout.spacing = 14;
            controlsLayout.childForceExpandWidth = true;
            controlsLayout.childForceExpandHeight = true;
            controlsLayout.childControlWidth = true;
            controlsLayout.childControlHeight = true;
            _hostControls = (RectTransform)controls.transform;
            UiKit.CreateButton(_hostControls, "Start game", 20, () => OnStart?.Invoke());

            _lobbyStatus = UiKit.CreateText(_lobbyRoot, "", 18,
                TextAnchor.MiddleCenter, new Color(1f, 0.75f, 0.6f), body: true);
            UiKit.Anchor((RectTransform)_lobbyStatus.transform, new Vector2(0.1f, 0.24f), new Vector2(0.9f, 0.29f));

            ShowMenu("");
        }

        /// <summary>Activate exactly one pre-game screen.</summary>
        private void ActivateOnly(RectTransform target)
        {
            foreach (var root in new[] { _menuRoot, _lobbyRoot, _settingsRoot, _soloRoot, _joinRoot, _gamesRoot })
            {
                root.gameObject.SetActive(root == target);
            }
        }

        public void ShowMenu(string status)
        {
            ActivateOnly(_menuRoot);
            _menuStatus.text = status ?? "";
        }

        public void ShowSettings()
        {
            ActivateOnly(_settingsRoot);
        }

        /// <summary>The identity-backed games list; rows re-enter their rooms.</summary>
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

            string status;
            Color statusColor;
            bool shouty = false;
            if (game.Finished)
            {
                status = "finished";
                statusColor = new Color(0.6f, 0.6f, 0.6f);
            }
            else if (!game.Started)
            {
                status = "in lobby";
                statusColor = new Color(0.65f, 0.75f, 0.85f);
            }
            else if (game.YourTurn)
            {
                status = "YOUR TURN";
                statusColor = UiKit.ButtonColor;
                shouty = true;
            }
            else
            {
                status = $"{game.TurnPlayerName}'s turn";
                statusColor = new Color(0.75f, 0.75f, 0.75f);
            }
            var statusText = UiKit.CreateText(row.transform, status, shouty ? 19 : 16,
                TextAnchor.MiddleRight, statusColor, body: !shouty);
            statusText.raycastTarget = false;
            UiKit.Anchor((RectTransform)statusText.transform, Vector2.zero, Vector2.one,
                new Vector2(16, 2), new Vector2(-16, -2));

            string code = game.Code;
            UiKit.AddHover(row,
                () => background.color = hover,
                () => background.color = idle);
            UiKit.AddClick(row, () => onPick?.Invoke(code));
        }

        /// <summary>The big room-code entry: shown after "Join room" on the menu.</summary>
        public void ShowJoin()
        {
            ActivateOnly(_joinRoot);
            _codeInput.text = "";
            _codeInput.ActivateInputField();
        }

        /// <summary>Pre-game bot setup for solo play: no ready-up, just pick your table.</summary>
        public void ShowSoloSetup()
        {
            ActivateOnly(_soloRoot);
            RefreshSoloSeats();
        }

        private void RefreshSoloSeats()
        {
            UiKit.Clear(_soloSeatList);
            AddSeatRow(_soloSeatList, $"1. {DisplayName}   <- you", null);
            for (int i = 0; i < _soloBots.Count; i++)
            {
                int index = i;
                // The last remaining bot cannot be removed: 2 players minimum.
                AddSeatRow(_soloSeatList, $"{i + 2}. {_soloBots[i].Name} (bot)",
                    _soloBots.Count > 1
                        ? () =>
                        {
                            _soloBots.RemoveAt(index);
                            RefreshSoloSeats();
                        }
                        : (System.Action)null,
                    _soloBots[index].Level,
                    () =>
                    {
                        _soloBots[index] = (_soloBots[index].Name,
                            NextLevel(_soloBots[index].Level));
                        RefreshSoloSeats();
                    });
            }
            if (_soloBots.Count < BotNames.Length)
            {
                UiKit.CreateButton(_soloSeatList, "+ Add bot", 18, () =>
                {
                    _soloBots.Add((BotNames.First(n => _soloBots.All(b => b.Name != n)),
                        "medium"));
                    RefreshSoloSeats();
                });
            }
        }

        /// <summary>The difficulty chip cycles easy -> medium -> hard -> easy.</summary>
        public static string NextLevel(string level) =>
            level == "easy" ? "medium" : level == "medium" ? "hard" : "easy";

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
            foreach (var s in room.Seats)
            {
                string label = $"{s.Seat + 1}. {s.Name}" +
                    (s.Ready ? "   READY" : s.IsBot ? "" : "   ...") +
                    (s.IsBot || s.Connected ? "" : "  (disconnected)") +
                    (s.Seat == room.YourSeat ? "   <- you" : "");
                int seatIndex = s.Seat;
                string level = s.IsBot ? (string.IsNullOrEmpty(s.BotLevel) ? "medium" : s.BotLevel) : null;
                AddSeatRow(_lobbySeatList, label,
                    isHost && s.IsBot ? () => OnRemoveBotSeat?.Invoke(seatIndex) : (System.Action)null,
                    level,
                    isHost && s.IsBot
                        ? () => OnSetBotLevel?.Invoke(seatIndex, NextLevel(level))
                        : (System.Action)null);
            }
            if (isHost && room.Seats.Count > 0 && room.Seats.Count < MaxSeats)
            {
                UiKit.CreateButton(_lobbySeatList, "+ Add bot", 18, () => OnAddBot?.Invoke());
            }

            _readyLabel.text = myReady ? "READY! (click to undo)" : "READY UP";
            _lobbyStatus.text = status ?? "";
            _hostControls.gameObject.SetActive(isHost);
        }

        // ---------------------------------------------------- seat rows

        private static RectTransform CreateSeatList(RectTransform parent, Vector2 min, Vector2 max)
        {
            var go = new GameObject("SeatList", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            UiKit.Anchor((RectTransform)go.transform, min, max);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            return (RectTransform)go.transform;
        }

        /// <summary>
        /// One seat line. Bots get an X on the right when removal is allowed, and a
        /// difficulty chip to its left: clickable (cycles easy/medium/hard) when
        /// onCycleLevel is provided, informational otherwise.
        /// </summary>
        private static void AddSeatRow(RectTransform list, string label, System.Action onRemove,
            string botLevel = null, System.Action onCycleLevel = null)
        {
            var row = new GameObject("SeatRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(list, false);
            var background = row.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            background.color = new Color(0, 0, 0, 0.30f);
            row.GetComponent<LayoutElement>().minHeight = 48;

            var text = UiKit.CreateText(row.transform, label, 20, TextAnchor.MiddleLeft);
            UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one,
                new Vector2(16, 2), new Vector2(botLevel != null ? -152 : -56, -2));

            if (botLevel != null)
            {
                var chipGo = new GameObject("Level", typeof(RectTransform), typeof(Image));
                chipGo.transform.SetParent(row.transform, false);
                var chipRect = (RectTransform)chipGo.transform;
                chipRect.anchorMin = chipRect.anchorMax = new Vector2(1f, 0.5f);
                chipRect.pivot = new Vector2(1f, 0.5f);
                chipRect.sizeDelta = new Vector2(88f, 34f);
                chipRect.anchoredPosition = new Vector2(onRemove != null ? -49f : -7f, 0);
                var chipImage = chipGo.GetComponent<Image>();
                chipImage.sprite = UiSprites.RoundedRect;
                chipImage.type = Image.Type.Sliced;
                var chipIdle = new Color(0.24f, 0.30f, 0.40f, 0.95f);
                chipImage.color = chipIdle;
                var levelText = UiKit.CreateText(chipGo.transform,
                    botLevel.ToUpperInvariant(), 15, TextAnchor.MiddleCenter,
                    botLevel == "hard" ? new Color(1f, 0.62f, 0.45f)
                    : botLevel == "easy" ? new Color(0.62f, 0.90f, 0.62f)
                    : new Color(0.85f, 0.88f, 0.92f), body: true);
                levelText.raycastTarget = false;
                UiKit.Anchor((RectTransform)levelText.transform, Vector2.zero, Vector2.one);
                if (onCycleLevel != null)
                {
                    UiKit.AddHover(chipGo,
                        () => chipImage.color = new Color(0.34f, 0.42f, 0.56f, 1f),
                        () => chipImage.color = chipIdle);
                    UiKit.AddClick(chipGo, () => onCycleLevel());
                }
            }

            if (onRemove != null)
            {
                var removeGo = new GameObject("Remove", typeof(RectTransform), typeof(Image));
                removeGo.transform.SetParent(row.transform, false);
                var removeRect = (RectTransform)removeGo.transform;
                removeRect.anchorMin = removeRect.anchorMax = new Vector2(1f, 0.5f);
                removeRect.pivot = new Vector2(1f, 0.5f);
                removeRect.sizeDelta = new Vector2(36f, 36f);
                removeRect.anchoredPosition = new Vector2(-7f, 0);
                var removeImage = removeGo.GetComponent<Image>();
                removeImage.sprite = UiSprites.RoundedRect;
                removeImage.type = Image.Type.Sliced;
                removeImage.color = new Color(0.72f, 0.24f, 0.20f, 0.95f);
                var x = UiKit.CreateText(removeGo.transform, "X", 18,
                    TextAnchor.MiddleCenter, Color.white);
                x.raycastTarget = false;
                UiKit.Anchor((RectTransform)x.transform, Vector2.zero, Vector2.one);
                UiKit.AddHover(removeGo,
                    () => removeImage.color = new Color(0.88f, 0.32f, 0.26f, 1f),
                    () => removeImage.color = new Color(0.72f, 0.24f, 0.20f, 0.95f));
                UiKit.AddClick(removeGo, () => onRemove());
            }
        }

        public void HideAll()
        {
            ActivateOnly(null);
        }

        /// <summary>Live server reachability line under the server field.</summary>
        public void SetServerStatus(bool reachable, string detail)
        {
            _serverStatus.text = reachable ? "● server online" : $"○ {detail}";
            _serverStatus.color = reachable
                ? new Color(0.45f, 0.85f, 0.45f)
                : new Color(0.85f, 0.55f, 0.45f);
        }
    }
}
