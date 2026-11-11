using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Main menu + online lobby, code-built like everything else. The app supplies the
    /// callbacks; this class only owns widgets.
    /// </summary>
    public sealed partial class LobbyUi
    {
        public System.Action OnRulebook;
        public System.Action<IReadOnlyList<(string Name, string Level)>> OnStartSolo;
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
        public System.Action<string> OnSaveSettings;           // display name, on every edit

        /// <summary>Baked into the build from client-config.json; players never edit it.</summary>
        public string ServerUrl { get; }
        public bool MenuVisible => _menuRoot.gameObject.activeSelf;

        /// <summary>The name used at the table, from Settings; never empty.</summary>
        public string DisplayName =>
            string.IsNullOrWhiteSpace(_displayNameInput.text)
                ? "Player"
                : _displayNameInput.text.Trim();

        private static readonly string[] BotNames = { "Benny", "Cleo", "Dex", "Squeezy" };
        private const int MaxSeats = 5;
        private static readonly Color ReadyGreen = new Color(0.45f, 0.85f, 0.45f);
        private static readonly Color NotReadyRed = new Color(0.90f, 0.32f, 0.32f);

        private RectTransform _menuRoot;
        private RectTransform _lobbyRoot;
        private RectTransform _settingsRoot;
        private RectTransform _soloRoot;
        private RectTransform _joinRoot;
        private RectTransform _gamesRoot;
        private RectTransform _gamesList;
        private RectTransform _soloSeatList;
        private readonly List<(string Name, string Level)> _soloBots =
            new List<(string, string)>
            {
                ("Benny", "medium"), ("Cleo", "medium"), ("Dex", "medium"),
            };
        private TMP_InputField _displayNameInput;
        private TMP_InputField _codeInput;
        private TMP_Text _lobbyTitle;
        private RectTransform _lobbySeatList;
        private TMP_Text _lobbyStatus;
        private TMP_Text _menuStatus;
        private TMP_Text _serverStatus;
        private TMP_Text _readyLabel;
        private RectTransform _hostControls;
        private bool _myReady;

        public LobbyUi(RectTransform canvasRoot, string serverUrl, string defaultName)
        {
            ServerUrl = serverUrl;

            BuildMainMenu(canvasRoot);
            BuildSettings(canvasRoot, defaultName);
            BuildJoinRoom(canvasRoot);
            BuildSoloSetup(canvasRoot);
            BuildMyGames(canvasRoot);
            BuildLobby(canvasRoot);

            // The table's own backdrop, so the pre-game screens feel like the same world.
            foreach (var root in AllScreens)
            {
                UiKit.CreateWallpaper(root, Vector2.one);
            }

            ShowMenu("");
        }

        public void HideAll()
        {
            ActivateOnly(null);
        }

        private RectTransform[] AllScreens =>
            new[] { _menuRoot, _lobbyRoot, _settingsRoot, _soloRoot, _joinRoot, _gamesRoot };

        private void ActivateOnly(RectTransform target)
        {
            foreach (var root in AllScreens)
            {
                root.gameObject.SetActive(root == target);
            }
        }

        // ------------------------------------------------ shared chrome

        private static TMP_Text CreateScreenTitle(RectTransform root, string text)
        {
            var title = UiKit.CreateText(root, text, 48,
                TextAnchor.MiddleCenter, UiKit.ButtonColor);
            UiKit.Anchor((RectTransform)title.transform, new Vector2(0, 0.74f), new Vector2(1, 0.92f));
            return title;
        }

        private static Transform CreateButtonColumn(RectTransform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var column = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            column.transform.SetParent(parent, false);
            UiKit.Anchor((RectTransform)column.transform, anchorMin, anchorMax);
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            return column.transform;
        }

        /// <summary>Equal-width buttons side by side (Back/Join, Back/Start, ...).</summary>
        private static RectTransform CreateButtonRow(RectTransform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            UiKit.Anchor((RectTransform)go.transform, anchorMin, anchorMax);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 14;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            return (RectTransform)go.transform;
        }
    }
}
