using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// "Your turn" interstitial shown when the turn passes to the local player: blurred
    /// backdrop, big title and a single ONWARD! button. Any pending modal (discard, fine,
    /// response) is deferred until it is dismissed.
    /// </summary>
    public sealed class TurnBanner
    {
        private static readonly Color ButtonIdle = UiKit.ButtonColor;
        private static readonly Color ButtonHover = new Color(1f, 0.92f, 0.35f);

        public System.Action OnDismiss;
        public bool IsOpen { get; private set; }
        /// <summary>Diagnostics: open-but-invisible means a reveal died mid-flight.</summary>
        public bool RootVisible => _root.gameObject.activeSelf;

        private readonly ModalBackdrop _backdrop;
        private readonly RectTransform _root;
        private readonly TMP_Text _subtitle;
        private readonly TMP_Text _timeLeft;
        private static readonly Color TimeLeftColor = new Color(0.96f, 0.96f, 0.92f);
        private static readonly Color TimeNearlyUpColor = new Color(1f, 0.38f, 0.32f);
        private const int NearlyUpSeconds = 10;

        public TurnBanner(RectTransform canvasRoot, MonoBehaviour host)
        {
            _root = UiKit.CreatePanel(canvasRoot, "TurnBanner", new Color(0, 0, 0, 0));
            UiKit.Anchor(_root, Vector2.zero, Vector2.one);
            _backdrop = new ModalBackdrop(_root, host);

            var title = UiKit.CreateText(_root, "YOUR TURN!", 96,
                TextAnchor.MiddleCenter, UiKit.ButtonColor);
            UiKit.Anchor((RectTransform)title.transform,
                new Vector2(0.05f, 0.52f), new Vector2(0.95f, 0.74f));
            UiKit.AddTextShadow(title, 1.4f);

            _subtitle = UiKit.CreateText(_root, "", 28,
                TextAnchor.MiddleCenter, new Color(0.96f, 0.96f, 0.92f));
            UiKit.Anchor((RectTransform)_subtitle.transform,
                new Vector2(0.1f, 0.44f), new Vector2(0.9f, 0.52f));
            UiKit.AddTextShadow(_subtitle);

            BuildOnwardButton();

            _timeLeft = UiKit.CreateText(_root, "", 24, TextAnchor.MiddleCenter, TimeLeftColor);
            UiKit.Anchor((RectTransform)_timeLeft.transform,
                new Vector2(0.1f, 0.215f), new Vector2(0.9f, 0.275f));
            UiKit.AddTextShadow(_timeLeft);

            _root.gameObject.SetActive(false);
        }

        private void BuildOnwardButton()
        {
            var buttonGo = new GameObject("Onward", typeof(RectTransform), typeof(Image), typeof(Shadow));
            buttonGo.transform.SetParent(_root, false);
            UiKit.Anchor((RectTransform)buttonGo.transform, new Vector2(0.41f, 0.29f), new Vector2(0.59f, 0.375f));
            var background = buttonGo.GetComponent<Image>();
            background.sprite = UiSprites.RoundedRect;
            background.type = Image.Type.Sliced;
            background.color = ButtonIdle;
            var buttonShadow = buttonGo.GetComponent<Shadow>();
            buttonShadow.effectColor = new Color(0, 0, 0, 0.5f);
            buttonShadow.effectDistance = new Vector2(0f, -5f);

            var label = UiKit.CreateText(buttonGo.transform, "ONWARD!", 34,
                TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
            UiKit.Anchor((RectTransform)label.transform, Vector2.zero, Vector2.one);

            UiKit.AddHover(buttonGo,
                () => background.color = ButtonHover,
                () => background.color = ButtonIdle);
            UiKit.AddClick(buttonGo, () =>
            {
                Hide();
                OnDismiss?.Invoke();
            });
        }

        public void Show(string subtitle)
        {
            IsOpen = true;
            _subtitle.text = subtitle ?? "";
            _timeLeft.text = "";
            // Appears next frame, once the backdrop blur has been captured.
            _backdrop.Reveal(_root.gameObject);
        }

        /// <summary>
        /// The banner covers the avatars and their hourglasses, and the action timer runs under
        /// it: this is the only place the clock can be seen. Null for a turn with no clock on it.
        /// </summary>
        public void ShowSecondsLeft(int? seconds)
        {
            string shown = seconds is int left ? $"{left} seconds left" : "";
            if (_timeLeft.text == shown)
            {
                return;
            }
            _timeLeft.text = shown;
            _timeLeft.color = seconds <= NearlyUpSeconds ? TimeNearlyUpColor : TimeLeftColor;
        }

        public void Hide()
        {
            IsOpen = false;
            _root.gameObject.SetActive(false);
            _backdrop.Hide();
        }
    }
}
