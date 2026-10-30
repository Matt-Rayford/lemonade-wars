using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Player pick: a blocking "choose a victim" overlay — the card floats mid-screen
    // with an arrow trailing the cursor to the player bars. Click-mode only, used by
    // follow-up decisions that aren't armed from a hand card.
    public sealed partial class TableView
    {
        private RectTransform _pickRoot;
        private RectTransform _pickArrowHost;
        private RectTransform _pickCard;
        private ISet<int> _pickValid;
        private System.Action<int> _pickOnPick;
        private System.Action _pickOnPass;
        private GameObject _pickPassButton;
        private int _pickHover = -1;
        private Vector2 _pickArrowFrom;
        private Vector2 _pickArrowTo;

        public bool PlayerPickActive => _pickRoot != null && _pickRoot.gameObject.activeSelf;

        private void BuildPlayerPick()
        {
            _pickRoot = UiKit.CreatePanel(Root, "PlayerPick", new Color(0, 0, 0, 0));
            UiKit.Anchor(_pickRoot, Vector2.zero, Vector2.one);
            UiKit.AddClick(_pickRoot.gameObject, FinishPlayerPick);

            var cardGo = new GameObject("PickCard", typeof(RectTransform), typeof(RawImage),
                typeof(Shadow));
            cardGo.transform.SetParent(_pickRoot, false);
            _pickCard = (RectTransform)cardGo.transform;
            _pickCard.anchorMin = _pickCard.anchorMax = new Vector2(0.5f, 0.55f);
            _pickCard.sizeDelta = new Vector2(300f, 420f);
            cardGo.GetComponent<RawImage>().raycastTarget = false;
            var shadow = cardGo.GetComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);
            shadow.effectDistance = new Vector2(0, -6f);

            var arrowGo = new GameObject("Arrow", typeof(RectTransform));
            arrowGo.transform.SetParent(_pickRoot, false);
            _pickArrowHost = (RectTransform)arrowGo.transform;
            UiKit.Anchor(_pickArrowHost, Vector2.zero, Vector2.one);

            // Optional abilities offer a way out, right under the card.
            var pass = UiKit.CreateButton(_pickRoot, "Pass", 18, () =>
            {
                var onPass = _pickOnPass;
                EndPlayerPick();
                onPass?.Invoke();
            }, dark: true);
            _pickPassButton = pass.gameObject;
            var passRect = (RectTransform)pass.transform;
            passRect.anchorMin = passRect.anchorMax = new Vector2(0.5f, 0.55f);
            passRect.pivot = new Vector2(0.5f, 1f);
            passRect.sizeDelta = new Vector2(180f, 44f);
            passRect.anchoredPosition = new Vector2(0f, -210f - 18f); // just below the card
            _pickRoot.gameObject.SetActive(false);
        }

        /// <param name="onPass">Declines the ability; null makes the pick mandatory (no button).</param>
        public void BeginPlayerPick(Texture2D cardArt, ISet<int> validTargets, System.Action<int> onPick,
            System.Action onPass = null)
        {
            _pickValid = validTargets;
            _pickOnPick = onPick;
            _pickOnPass = onPass;
            _pickPassButton.SetActive(onPass != null);
            _pickHover = -1;
            _pickArrowFrom = _pickArrowTo = new Vector2(float.NaN, float.NaN);
            _pickCard.GetComponent<RawImage>().texture = cardArt;
            _pickCard.gameObject.SetActive(cardArt != null);
            UiKit.Clear(_pickArrowHost);
            _pickRoot.SetAsLastSibling();
            _pickRoot.gameObject.SetActive(true);
            _preview.SetDragging(true); // no magnify pop-ups while aiming
        }

        public void EndPlayerPick()
        {
            if (!PlayerPickActive)
            {
                return;
            }
            foreach (var (_, _, glow) in _playerRows)
            {
                if (glow != null)
                {
                    glow.SetActive(false);
                }
            }
            _pickValid = null;
            _pickOnPick = null;
            _pickOnPass = null;
            _pickHover = -1;
            UiKit.Clear(_pickArrowHost);
            _pickRoot.gameObject.SetActive(false);
            _preview.SetDragging(false);
        }

        /// <summary>Called every frame by the app; drives the bar glow + the arrow.</summary>
        public void TickPlayerPick(Vector2 screenPosition)
        {
            if (!PlayerPickActive)
            {
                return;
            }
            int hover = -1;
            foreach (var (playerId, row, _) in _playerRows)
            {
                if (row != null && _pickValid.Contains(playerId) &&
                    RectTransformUtility.RectangleContainsScreenPoint(row, screenPosition))
                {
                    hover = playerId;
                    break;
                }
            }
            if (hover != _pickHover)
            {
                _pickHover = hover;
                foreach (var (playerId, _, glow) in _playerRows)
                {
                    if (glow != null)
                    {
                        glow.GetComponent<Image>().color = AttackGlowColor;
                        glow.SetActive(playerId == hover);
                    }
                }
            }

            // Arrow: from the middle of the floating card, trailing the cursor.
            Vector2 from = _pickRoot.InverseTransformPoint(_pickCard.TransformPoint(Vector3.zero));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _pickRoot, screenPosition, null, out var to);
            if ((from - _pickArrowFrom).sqrMagnitude < 4f &&
                (to - _pickArrowTo).sqrMagnitude < 4f)
            {
                return;
            }
            _pickArrowFrom = from;
            _pickArrowTo = to;
            UiKit.Clear(_pickArrowHost);
            UiKit.DrawArrow(_pickArrowHost, from, to, AttackArrowColor);
        }

        /// <summary>The decision is blocking: clicking empty space keeps aiming.</summary>
        private void FinishPlayerPick()
        {
            if (_pickHover < 0)
            {
                return;
            }
            int victim = _pickHover;
            var onPick = _pickOnPick;
            EndPlayerPick();
            onPick?.Invoke(victim);
        }
    }
}
