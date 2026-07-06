using System.Collections.Generic;
using System.Linq;
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
        public System.Action<IReadOnlyList<string>> OnStartSolo; // bot names
        public System.Action<string, string> OnHost;          // serverUrl, name
        public System.Action<string, string, string> OnJoin;  // serverUrl, name, code
        public System.Action OnResume;
        public System.Action OnAddBot;
        public System.Action<int> OnRemoveBotSeat;             // seat index
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
        private readonly RectTransform _soloSeatList;
        private readonly List<string> _soloBotNames = new List<string> { "Benny", "Cleo", "Dex" };
        private readonly InputField _displayNameInput;
        private readonly InputField _serverInput;
        private readonly InputField _codeInput;
        private readonly Text _lobbyTitle;
        private readonly RectTransform _lobbySeatList;
        private readonly Text _lobbyStatus;
        private readonly Text _menuStatus;
        private readonly Text _serverStatus;
        private readonly Text _readyLabel;
        private readonly Button _resumeButton;
        private readonly Text _resumeLabel;
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
                TextAnchor.MiddleCenter, new Color(0.6f, 0.6f, 0.6f));

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

            _resumeButton = MenuButton("", () => OnResume?.Invoke());
            _resumeLabel = _resumeButton.GetComponentInChildren<Text>();
            _resumeButton.gameObject.SetActive(false);

            MenuButton("Settings", ShowSettings);

            _menuStatus = UiKit.CreateText(column.transform, "", 16,
                TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.5f));

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
                TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.8f));
            nameLabel.gameObject.AddComponent<LayoutElement>().minHeight = 24;
            _displayNameInput = UiKit.CreateInput(settingsColumn.transform, "Display Name", defaultName);

            var serverLabel = UiKit.CreateText(settingsColumn.transform,
                "Server URL", 16, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.8f));
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
            codeText.alignment = TextAnchor.MiddleCenter;
            if (_codeInput.placeholder is Text codePlaceholder)
            {
                codePlaceholder.fontSize = 52;
                codePlaceholder.alignment = TextAnchor.MiddleCenter;
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
            UiKit.CreateButton((RectTransform)joinActions.transform, "Join", 20,
                () => OnJoin?.Invoke(_serverInput.text, DisplayName,
                    _codeInput.text.Trim().ToUpperInvariant()));
            UiKit.CreateButton((RectTransform)joinActions.transform, "< Back", 20,
                () => ShowMenu(""));
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
            UiKit.CreateButton((RectTransform)soloActions.transform, "Start game", 20,
                () => OnStartSolo?.Invoke(_soloBotNames.ToList()));
            UiKit.CreateButton((RectTransform)soloActions.transform, "< Back", 20,
                () => ShowMenu(""));
            _soloRoot.gameObject.SetActive(false);

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
            var readyButton = UiKit.CreateButton((RectTransform)everyoneControls.transform,
                "READY UP", 20, () => OnReadyToggle?.Invoke(!_myReady));
            _readyLabel = readyButton.GetComponentInChildren<Text>();
            UiKit.CreateButton((RectTransform)everyoneControls.transform, "< Back", 20,
                () => OnLeave?.Invoke());

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
                TextAnchor.MiddleCenter, new Color(1f, 0.75f, 0.6f));
            UiKit.Anchor((RectTransform)_lobbyStatus.transform, new Vector2(0.1f, 0.24f), new Vector2(0.9f, 0.29f));

            ShowMenu("");
        }

        public void ShowMenu(string status)
        {
            _menuRoot.gameObject.SetActive(true);
            _lobbyRoot.gameObject.SetActive(false);
            _settingsRoot.gameObject.SetActive(false);
            _soloRoot.gameObject.SetActive(false);
            _joinRoot.gameObject.SetActive(false);
            _menuStatus.text = status ?? "";
        }

        public void ShowSettings()
        {
            _menuRoot.gameObject.SetActive(false);
            _lobbyRoot.gameObject.SetActive(false);
            _settingsRoot.gameObject.SetActive(true);
            _soloRoot.gameObject.SetActive(false);
            _joinRoot.gameObject.SetActive(false);
        }

        /// <summary>The big room-code entry: shown after "Join room" on the menu.</summary>
        public void ShowJoin()
        {
            _menuRoot.gameObject.SetActive(false);
            _lobbyRoot.gameObject.SetActive(false);
            _settingsRoot.gameObject.SetActive(false);
            _soloRoot.gameObject.SetActive(false);
            _joinRoot.gameObject.SetActive(true);
            _codeInput.text = "";
            _codeInput.ActivateInputField();
        }

        /// <summary>Pre-game bot setup for solo play: no ready-up, just pick your table.</summary>
        public void ShowSoloSetup()
        {
            _menuRoot.gameObject.SetActive(false);
            _lobbyRoot.gameObject.SetActive(false);
            _settingsRoot.gameObject.SetActive(false);
            _soloRoot.gameObject.SetActive(true);
            _joinRoot.gameObject.SetActive(false);
            RefreshSoloSeats();
        }

        private void RefreshSoloSeats()
        {
            UiKit.Clear(_soloSeatList);
            AddSeatRow(_soloSeatList, $"1. {DisplayName}   <- you", null);
            for (int i = 0; i < _soloBotNames.Count; i++)
            {
                int index = i;
                // The last remaining bot cannot be removed: 2 players minimum.
                AddSeatRow(_soloSeatList, $"{i + 2}. {_soloBotNames[i]} (bot)",
                    _soloBotNames.Count > 1
                        ? () =>
                        {
                            _soloBotNames.RemoveAt(index);
                            RefreshSoloSeats();
                        }
                        : (System.Action)null);
            }
            if (_soloBotNames.Count < BotNames.Length)
            {
                UiKit.CreateButton(_soloSeatList, "+ Add bot", 18, () =>
                {
                    _soloBotNames.Add(BotNames.First(n => !_soloBotNames.Contains(n)));
                    RefreshSoloSeats();
                });
            }
        }

        public void ShowLobby(RemoteRoomState room, string status, bool myReady)
        {
            _menuRoot.gameObject.SetActive(false);
            _lobbyRoot.gameObject.SetActive(true);
            _settingsRoot.gameObject.SetActive(false);
            _soloRoot.gameObject.SetActive(false);
            _joinRoot.gameObject.SetActive(false);
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
                AddSeatRow(_lobbySeatList, label,
                    isHost && s.IsBot ? () => OnRemoveBotSeat?.Invoke(seatIndex) : (System.Action)null);
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

        /// <summary>One seat line; bots get an X on the right when removal is allowed.</summary>
        private static void AddSeatRow(RectTransform list, string label, System.Action onRemove)
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
                new Vector2(16, 2), new Vector2(-56, -2));

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
            _menuRoot.gameObject.SetActive(false);
            _lobbyRoot.gameObject.SetActive(false);
            _settingsRoot.gameObject.SetActive(false);
            _soloRoot.gameObject.SetActive(false);
            _joinRoot.gameObject.SetActive(false);
        }

        /// <summary>Show the Resume button when a previous online game is remembered.</summary>
        public void SetResumeInfo(string roomCode)
        {
            bool available = !string.IsNullOrEmpty(roomCode);
            _resumeButton.gameObject.SetActive(available);
            if (available)
            {
                _resumeLabel.text = $"Resume game ({roomCode})";
            }
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
