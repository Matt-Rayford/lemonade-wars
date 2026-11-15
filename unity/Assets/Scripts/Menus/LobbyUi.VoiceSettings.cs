using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Settings for the card voice lines: a checkbox that turns them on, and under it, only
    // while they are on, a checkbox for the explicit takes, the voice your own cards speak in,
    // and how loud voices are on this client.
    public sealed partial class LobbyUi
    {
        private const float SettingsRowHeightPx = 44f;
        private const float CheckboxRowHeightPx = 36f;
        private const float CheckboxSizePx = 26f;
        /// <summary>The yellow of a ticked box stops this short of its rim.</summary>
        private const float CheckboxTickInsetPx = 5f;
        private const int SettingsLabelFontSize = 18;
        private static readonly Color SettingsLabelColor = new Color(0.8f, 0.8f, 0.8f);

        private Image _playerVoicesTick;
        private Image _explicitModeTick;
        private GameObject _myVoiceLabel;
        private RectTransform _myVoiceRow;
        private GameObject _explicitModeCheckbox;
        private TMPro.TMP_Text _voiceVolumeLabel;
        private GameObject _voiceVolumeSlider;

        private void BuildVoiceControls(Transform settingsColumn)
        {
            AddCheckbox(settingsColumn, "Enable Player Voices", out _playerVoicesTick, () =>
            {
                VoiceLines.HearVoices = !VoiceLines.HearVoices;
                ShowVoiceSettings();
            });
            _explicitModeCheckbox = AddCheckbox(settingsColumn, "Enable explicit mode", out _explicitModeTick, () =>
            {
                VoiceLines.ExplicitMode = !VoiceLines.ExplicitMode;
                ShowVoiceSettings();
            });

            _myVoiceLabel = AddSettingsLabel(settingsColumn, "Your Voice").gameObject;
            _myVoiceRow = CreateButtonRow((RectTransform)settingsColumn, "MyVoice", Vector2.zero, Vector2.one);
            var voiceRowSize = _myVoiceRow.gameObject.AddComponent<LayoutElement>();
            voiceRowSize.preferredHeight = SettingsRowHeightPx;
            voiceRowSize.flexibleHeight = 0;

            _voiceVolumeLabel = AddSettingsLabel(settingsColumn, "");
            var volumeSlider = UiKit.CreateSlider(settingsColumn,
                steps: 100 / VoiceLines.VolumeStep, initialStep: VoiceLines.Volume / VoiceLines.VolumeStep);
            _voiceVolumeSlider = volumeSlider.gameObject;
            volumeSlider.onValueChanged.AddListener(step =>
            {
                VoiceLines.Volume = (int)step * VoiceLines.VolumeStep;
                ShowVoiceVolume();
                // A line to judge the level by; one already playing just gets louder or quieter.
                if (!VoiceLines.IsSpeaking)
                {
                    VoiceLines.Audition(VoiceToSample());
                }
            });
        }

        private static TMPro.TMP_Text AddSettingsLabel(Transform settingsColumn, string words)
        {
            var label = UiKit.CreateText(settingsColumn, words, SettingsLabelFontSize,
                TextAnchor.MiddleLeft, SettingsLabelColor, body: true);
            label.gameObject.AddComponent<LayoutElement>().minHeight = 24;
            return label;
        }

        private void ShowVoiceVolume() =>
            _voiceVolumeLabel.text = VoiceLines.Volume == 0
                ? "Voice volume — muted"
                : $"Voice volume — {VoiceLines.Volume}%";

        /// <summary>The player's own voice, or with none chosen, the first there is: the level is for everyone's voices.</summary>
        private static string VoiceToSample() =>
            VoiceLines.MyVoice != VoiceLines.NoVoice || VoiceLines.Available.Count == 0
                ? VoiceLines.MyVoice
                : VoiceLines.Available[0];

        /// <summary>A box with its caption to the right; a click anywhere on the row flips it.</summary>
        private static GameObject AddCheckbox(Transform settingsColumn, string caption, out Image tick,
            System.Action onFlip)
        {
            var rowGo = new GameObject(caption, typeof(RectTransform), typeof(Image),
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(settingsColumn, false);
            rowGo.GetComponent<Image>().color = new Color(0, 0, 0, 0); // unseen: it makes the whole row clickable
            var rowSize = rowGo.GetComponent<LayoutElement>();
            rowSize.preferredHeight = CheckboxRowHeightPx;
            rowSize.flexibleHeight = 0;
            var layout = rowGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var boxGo = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            boxGo.transform.SetParent(rowGo.transform, false);
            var boxSize = boxGo.GetComponent<LayoutElement>();
            boxSize.preferredWidth = boxSize.preferredHeight = CheckboxSizePx;
            var box = boxGo.GetComponent<Image>();
            box.sprite = UiSprites.RoundedRect;
            box.type = Image.Type.Sliced;
            box.pixelsPerUnitMultiplier = 2.5f; // a small box wants a small corner
            box.color = ChoiceIdle;
            box.raycastTarget = false;

            var tickGo = new GameObject("Tick", typeof(RectTransform), typeof(Image));
            tickGo.transform.SetParent(boxGo.transform, false);
            UiKit.Anchor((RectTransform)tickGo.transform, Vector2.zero, Vector2.one,
                new Vector2(CheckboxTickInsetPx, CheckboxTickInsetPx),
                new Vector2(-CheckboxTickInsetPx, -CheckboxTickInsetPx));
            tick = tickGo.GetComponent<Image>();
            tick.sprite = UiSprites.RoundedRect;
            tick.type = Image.Type.Sliced;
            tick.pixelsPerUnitMultiplier = 4f;
            tick.color = UiKit.ButtonColor;
            tick.raycastTarget = false;

            var label = UiKit.CreateText(rowGo.transform, caption, SettingsLabelFontSize,
                TextAnchor.MiddleLeft, SettingsLabelColor, body: true);
            label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            label.raycastTarget = false;

            UiKit.AddClick(rowGo, () =>
            {
                Sfx.Play(Sfx.ButtonClick);
                onFlip();
            });
            return rowGo;
        }

        /// <summary>Redrawn after every change. The voice picker and the explicit box only exist for a player who has voices on.</summary>
        private void ShowVoiceSettings()
        {
            bool voicesOn = VoiceLines.HearVoices;
            _playerVoicesTick.enabled = voicesOn;
            _explicitModeTick.enabled = VoiceLines.ExplicitMode;
            _explicitModeCheckbox.SetActive(voicesOn);
            _myVoiceLabel.SetActive(voicesOn);
            _myVoiceRow.gameObject.SetActive(voicesOn);
            _voiceVolumeLabel.gameObject.SetActive(voicesOn);
            _voiceVolumeSlider.SetActive(voicesOn);
            ShowVoiceVolume();

            UiKit.Clear(_myVoiceRow);
            string myVoice = VoiceLines.MyVoice;
            AddChoice(_myVoiceRow, "NONE", myVoice == VoiceLines.NoVoice,
                myVoice == VoiceLines.NoVoice ? (System.Action)null : () => ChooseVoice(VoiceLines.NoVoice));
            foreach (string voice in VoiceLines.Available)
            {
                string choice = voice;
                // Live even when already chosen: every click is another line to judge the voice by.
                AddChoice(_myVoiceRow, VoiceLines.DisplayName(voice).ToUpperInvariant(), voice == myVoice,
                    () => ChooseVoice(choice));
            }
        }

        private void ChooseVoice(string voice)
        {
            bool isChange = voice != VoiceLines.MyVoice;
            VoiceLines.MyVoice = voice;
            VoiceLines.Audition(voice);
            if (isChange)
            {
                ShowVoiceSettings();
            }
        }
    }
}
