using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The Esc menu: pause the table (visually — the game itself keeps flowing for
    /// the other players), read the rules, toggle sound, or bail to the main menu.
    /// </summary>
    public sealed class PauseMenu
    {
        private readonly RectTransform _root;
        private readonly TMP_Text _soundLabel;
        private readonly Button _speedButton;
        private readonly TMP_Text _speedLabel;
        private int _volumeBeforeMute;

        public System.Action OnRulebook;
        public System.Action OnQuit;
        /// <summary>Bot pacing, shown only in games that have bots.</summary>
        public System.Action OnCycleSpeed;
        public System.Func<string> SpeedLabel;
        public System.Func<bool> HasBots;

        public bool IsOpen => _root.gameObject.activeSelf;

        public PauseMenu(RectTransform canvasRoot)
        {
            _root = UiKit.CreatePanel(canvasRoot, "PauseMenu", new Color(0.03f, 0.05f, 0.08f, 0.90f));
            UiKit.Anchor(_root, Vector2.zero, Vector2.one);
            UiKit.AddClick(_root.gameObject, Close); // click outside the column resumes

            var title = UiKit.CreateText(_root, "PAUSED", 52, TextAnchor.MiddleCenter,
                UiKit.ButtonColor);
            title.raycastTarget = false;
            UiKit.Anchor((RectTransform)title.transform, new Vector2(0, 0.68f), new Vector2(1, 0.82f));
            UiKit.AddTextShadow(title);

            var column = new GameObject("PauseColumn", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(Image));
            column.transform.SetParent(_root, false);
            column.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f); // swallow the resume click
            UiKit.Anchor((RectTransform)column.transform, new Vector2(0.38f, 0.28f), new Vector2(0.62f, 0.64f));
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            Button Add(string label, UnityEngine.Events.UnityAction onClick,
                string clickSound = Sfx.ButtonClick)
            {
                var button = UiKit.CreateButton(column.transform, label, 22, onClick,
                    clickSound: clickSound);
                button.gameObject.AddComponent<LayoutElement>().minHeight = 56;
                return button;
            }

            Add("Resume", Close);
            Add("Rulebook", () => OnRulebook?.Invoke());
            _soundLabel = Add("", ToggleSound).GetComponentInChildren<TMP_Text>();
            // Silent: cycling the pace announces itself in its own voice.
            _speedButton = Add("", () =>
            {
                OnCycleSpeed?.Invoke();
                RefreshSpeedLabel();
            }, clickSound: null);
            _speedLabel = _speedButton.GetComponentInChildren<TMP_Text>();
            Add("Quit to main menu", () =>
            {
                Close();
                OnQuit?.Invoke();
            });
            RefreshSoundLabel();

            _root.gameObject.SetActive(false);
        }

        public void Open()
        {
            RefreshSoundLabel(); // the Settings slider may have moved since last time
            RefreshSpeedLabel();
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
        }

        private void RefreshSpeedLabel()
        {
            bool bots = HasBots?.Invoke() == true;
            _speedButton.gameObject.SetActive(bots);
            if (bots)
            {
                _speedLabel.text = $"Bot speed: {(SpeedLabel?.Invoke() ?? "medium").ToUpperInvariant()}";
            }
        }

        public void Close()
        {
            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Mute toggle over the Settings volume: muting remembers the level so
        /// unmuting comes back where the player left the slider.
        /// </summary>
        private void ToggleSound()
        {
            if (Sfx.Volume > 0)
            {
                _volumeBeforeMute = Sfx.Volume;
                Sfx.Volume = 0;
            }
            else
            {
                Sfx.Volume = _volumeBeforeMute > 0 ? _volumeBeforeMute : 100;
            }
            RefreshSoundLabel();
        }

        private void RefreshSoundLabel()
        {
            _soundLabel.text = Sfx.Volume == 0 ? "Sound: OFF" : $"Sound: {Sfx.Volume}%";
        }
    }
}
