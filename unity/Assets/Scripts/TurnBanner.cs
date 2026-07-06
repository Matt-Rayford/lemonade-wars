using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// "Your turn" interstitial: blurred darkened backdrop, big lemonade-yellow title,
    /// and a single ONWARD! button. Shown when the turn passes to the local player —
    /// online or vs bots — so you get a clear cue before your decisions appear. Any
    /// pending modal (discard, fine, response) is deferred until dismissed.
    /// </summary>
    public sealed class TurnBanner
    {
        private static readonly Color ButtonIdle = UiKit.ButtonColor;
        private static readonly Color ButtonHover = new Color(1f, 0.92f, 0.35f);

        public System.Action OnDismiss;
        public bool IsOpen { get; private set; }

        private readonly ModalBackdrop _backdrop;
        private readonly RectTransform _root;
        private readonly TMP_Text _subtitle;

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

            _root.gameObject.SetActive(false);
        }

        public void Show(string subtitle)
        {
            IsOpen = true;
            _subtitle.text = subtitle ?? "";
            // Appears next frame, once the backdrop blur has been captured.
            _backdrop.Reveal(_root.gameObject);
        }

        public void Hide()
        {
            IsOpen = false;
            _root.gameObject.SetActive(false);
            _backdrop.Hide();
        }
    }
}
