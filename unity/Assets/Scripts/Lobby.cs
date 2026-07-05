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
        public System.Action OnPlayLocal;
        public System.Action<string, string> OnHost;          // serverUrl, name
        public System.Action<string, string, string> OnJoin;  // serverUrl, name, code
        public System.Action OnResume;
        public System.Action OnAddBot;
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

        private readonly RectTransform _menuRoot;
        private readonly RectTransform _lobbyRoot;
        private readonly RectTransform _settingsRoot;
        private readonly InputField _displayNameInput;
        private readonly InputField _serverInput;
        private readonly InputField _codeInput;
        private readonly Text _lobbyTitle;
        private readonly Text _lobbySeats;
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

            _serverInput = UiKit.CreateInput(column.transform, "Server", defaultServerUrl);
            _serverStatus = UiKit.CreateText(column.transform, "", 14,
                TextAnchor.MiddleCenter, new Color(0.6f, 0.6f, 0.6f));

            UiKit.CreateButton(column.transform, "Play vs bots (offline)", 20,
                () => OnPlayLocal?.Invoke());
            UiKit.CreateButton(column.transform, "Host online room", 20,
                () => OnHost?.Invoke(_serverInput.text, DisplayName));

            _codeInput = UiKit.CreateInput(column.transform, "Room code (e.g. ABCDE)");
            UiKit.CreateButton(column.transform, "Join room", 20,
                () => OnJoin?.Invoke(_serverInput.text, DisplayName,
                    _codeInput.text.Trim().ToUpperInvariant()));

            _resumeButton = UiKit.CreateButton(column.transform, "", 20, () => OnResume?.Invoke());
            _resumeLabel = _resumeButton.GetComponentInChildren<Text>();
            _resumeButton.gameObject.SetActive(false);

            UiKit.CreateButton(column.transform, "Settings", 20, ShowSettings);

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
            UiKit.CreateButton(settingsColumn.transform, "Save & back", 20, () =>
            {
                OnSaveSettings?.Invoke(DisplayName);
                ShowMenu("");
            });
            _settingsRoot.gameObject.SetActive(false);

            // ------------------------------------------------------ lobby
            _lobbyRoot = UiKit.CreatePanel(canvasRoot, "Lobby", new Color(0.08f, 0.10f, 0.14f, 0.97f));
            UiKit.Anchor(_lobbyRoot, Vector2.zero, Vector2.one);

            _lobbyTitle = UiKit.CreateText(_lobbyRoot, "", 48,
                TextAnchor.MiddleCenter, new Color(0.98f, 0.83f, 0.10f));
            UiKit.Anchor((RectTransform)_lobbyTitle.transform, new Vector2(0, 0.74f), new Vector2(1, 0.92f));

            _lobbySeats = UiKit.CreateText(_lobbyRoot, "", 26, TextAnchor.UpperCenter);
            UiKit.Anchor((RectTransform)_lobbySeats.transform, new Vector2(0.2f, 0.30f), new Vector2(0.8f, 0.72f));

            // Everyone: ready toggle + leave. Host additionally: add bot + start.
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
            UiKit.CreateButton((RectTransform)everyoneControls.transform, "Leave", 20,
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
            UiKit.CreateButton(_hostControls, "Add bot", 20, () => OnAddBot?.Invoke());
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
            _menuStatus.text = status ?? "";
        }

        public void ShowSettings()
        {
            _menuRoot.gameObject.SetActive(false);
            _lobbyRoot.gameObject.SetActive(false);
            _settingsRoot.gameObject.SetActive(true);
        }

        public void ShowLobby(RemoteRoomState room, string status, bool myReady)
        {
            _menuRoot.gameObject.SetActive(false);
            _lobbyRoot.gameObject.SetActive(true);
            _settingsRoot.gameObject.SetActive(false);
            _myReady = myReady;
            _lobbyTitle.text = string.IsNullOrEmpty(room.Code) ? "CONNECTING..." : $"ROOM {room.Code}";
            _lobbySeats.text = room.Seats.Count == 0
                ? "Waiting for the server..."
                : string.Join("\n", room.Seats.Select(s =>
                    $"{s.Seat + 1}. {s.Name}" +
                    (s.Ready ? "   READY" : s.IsBot ? "" : "   ...") +
                    (s.IsBot || s.Connected ? "" : "  (disconnected)") +
                    (s.Seat == room.YourSeat ? "   <- you" : "")));
            _readyLabel.text = myReady ? "READY! (click to undo)" : "READY UP";
            _lobbyStatus.text = status ?? "";
            _hostControls.gameObject.SetActive(room.YourSeat == 0);
        }

        public void HideAll()
        {
            _menuRoot.gameObject.SetActive(false);
            _lobbyRoot.gameObject.SetActive(false);
            _settingsRoot.gameObject.SetActive(false);
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
