using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    public sealed partial class LobbyUi
    {
        private void BuildSettings(RectTransform canvasRoot, string defaultName)
        {
            _settingsRoot = UiKit.CreatePanel(canvasRoot, "Settings", UiKit.TableColor);
            UiKit.Anchor(_settingsRoot, Vector2.zero, Vector2.one);
            CreateScreenTitle(_settingsRoot, "SETTINGS");

            var settingsColumn = CreateButtonColumn(_settingsRoot, "SettingsColumn",
                new Vector2(0.34f, 0.30f), new Vector2(0.66f, 0.70f));

            BuildUsernameRow(settingsColumn, defaultName);
            BuildVolumeControl(settingsColumn);

            UiKit.CreateButton(settingsColumn, "Back", 20, () => ShowMenu(""));
            _settingsRoot.gameObject.SetActive(false);
        }

        public void ShowSettings()
        {
            ActivateOnly(_settingsRoot);
        }

        private void BuildUsernameRow(Transform settingsColumn, string defaultName)
        {
            var nameRow = new GameObject("UsernameRow",
                typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            nameRow.transform.SetParent(settingsColumn, false);
            var nameRowSize = nameRow.GetComponent<LayoutElement>();
            nameRowSize.preferredHeight = 44;
            nameRowSize.flexibleHeight = 0; // force-expanding children would otherwise claim the column's slack
            var nameRowLayout = nameRow.GetComponent<HorizontalLayoutGroup>();
            nameRowLayout.spacing = 12;
            nameRowLayout.childAlignment = TextAnchor.MiddleLeft;
            nameRowLayout.childForceExpandWidth = false;
            nameRowLayout.childForceExpandHeight = false;
            nameRowLayout.childControlWidth = true;
            nameRowLayout.childControlHeight = true;
            var nameLabel = UiKit.CreateText(nameRow.transform, "Username:", 18,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.8f), body: true);
            nameLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 110;
            _displayNameInput = UiKit.CreateInput(nameRow.transform, "Display Name", defaultName);
            _displayNameInput.GetComponent<LayoutElement>().flexibleWidth = 1;
            _displayNameInput.onValueChanged.AddListener(_ => OnSaveSettings?.Invoke(DisplayName));
        }

        private static void BuildVolumeControl(Transform settingsColumn)
        {
            var volumeLabel = UiKit.CreateText(settingsColumn, "", 16,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.8f), body: true);
            volumeLabel.gameObject.AddComponent<LayoutElement>().minHeight = 24;
            void ShowVolume() =>
                volumeLabel.text = Sfx.Volume == 0
                    ? "Sound Effects — muted"
                    : $"Sound Effects — {Sfx.Volume}%";
            ShowVolume();
            var volumeSlider = UiKit.CreateSlider(settingsColumn,
                steps: 100 / Sfx.VolumeStep, initialStep: Sfx.Volume / Sfx.VolumeStep);
            volumeSlider.onValueChanged.AddListener(step =>
            {
                Sfx.Volume = (int)step * Sfx.VolumeStep;
                ShowVolume();
                Sfx.Play(Sfx.ButtonClick); // audition the new level on every stop
            });
        }
    }
}
