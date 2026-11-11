using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public sealed partial class LobbyUi
    {
        private void BuildMainMenu(RectTransform canvasRoot)
        {
            _menuRoot = UiKit.CreatePanel(canvasRoot, "Menu", UiKit.TableColor);
            UiKit.Anchor(_menuRoot, Vector2.zero, Vector2.one);

            var title = UiKit.CreateText(_menuRoot, "LEMONADE WARS", 64,
                TextAnchor.MiddleCenter, UiKit.ButtonColor);
            UiKit.Anchor((RectTransform)title.transform, new Vector2(0, 0.78f), new Vector2(1, 0.95f));

            var column = CreateButtonColumn(_menuRoot, "MenuColumn",
                new Vector2(0.34f, 0.16f), new Vector2(0.66f, 0.76f));

            _serverStatus = UiKit.CreateText(column, "", 14,
                TextAnchor.MiddleCenter, new Color(0.6f, 0.6f, 0.6f), body: true);

            // Main-menu buttons get more presence than the standard 34px rows.
            Button MenuButton(string label, UnityEngine.Events.UnityAction onClick)
            {
                var menuButton = UiKit.CreateButton(column, label, 26, onClick);
                menuButton.GetComponent<LayoutElement>().minHeight = 64;
                return menuButton;
            }

            MenuButton("Play vs bots (offline)", ShowSoloSetup);
            MenuButton("Host online room", () => OnHost?.Invoke(ServerUrl, DisplayName));
            MenuButton("Join room", ShowJoin);
            MenuButton("My games", () => OnMyGames?.Invoke(ServerUrl, DisplayName));
            MenuButton("Rulebook", () => OnRulebook?.Invoke());
            MenuButton("Settings", ShowSettings);
            MenuButton("Quit", QuitApplication);

            _menuStatus = UiKit.CreateText(column, "", 16,
                TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.5f), body: true);
        }

        public void ShowMenu(string status)
        {
            ActivateOnly(_menuRoot);
            _menuStatus.text = status ?? "";
        }

        public void SetServerStatus(bool reachable, string detail)
        {
            _serverStatus.text = reachable ? "● server online" : $"○ {detail}";
            _serverStatus.color = reachable ? ReadyGreen : new Color(0.85f, 0.55f, 0.45f);
        }

        private static void QuitApplication()
        {
#if UNITY_EDITOR
            // Application.Quit is a no-op inside the editor; stop Play mode instead.
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
