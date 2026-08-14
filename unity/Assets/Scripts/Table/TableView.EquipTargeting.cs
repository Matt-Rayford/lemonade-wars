using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Equip targeting: the table dims EXCEPT the board band — you aim at your real
    // stands (tucked upgrades visible, so "that one is full" is legible), with the
    // taken card floating up top and a dashed arrow to the hovered cell.
    public sealed partial class TableView
    {
        private RectTransform _targetingRoot;
        private RectTransform _targetingCardHost;
        private RectTransform _targetingCopiesHost;
        private RectTransform _targetingArrowHost;
        private ISet<int?> _targetingValid;
        private System.Action<int?> _targetingPick;
        private System.Action _targetingCancel;
        private int? _targetingHover;
        private bool _targetingHoverAny;
        private Vector2 _targetingCardBottom;

        /// <summary>
        /// Invisible full-screen layer for aiming a just-taken card at a turf/stand.
        /// It intercepts the pointer itself and hit-tests the board cells manually, so
        /// hover glows and clicks work without fighting the cells' own handlers.
        /// </summary>
        private void BuildEquipTargeting()
        {
            _targetingRoot = UiKit.CreatePanel(Root, "EquipTargeting", new Color(0, 0, 0, 0));
            UiKit.Anchor(_targetingRoot, Vector2.zero, Vector2.one);
            // Dim everything EXCEPT the board band: the live stands (tucked upgrades
            // and all) are the targets, so the player aims with full information.
            var dimColor = new Color(0f, 0f, 0f, 0.66f);
            void Dim(Vector2 min, Vector2 max)
            {
                var panel = UiKit.CreatePanel(_targetingRoot, "Dim", dimColor);
                panel.GetComponent<Image>().raycastTarget = false;
                UiKit.Anchor(panel, min, max);
            }
            Dim(new Vector2(0f, 0.695f), new Vector2(1f, 1f));      // market band + up
            Dim(new Vector2(0f, 0f), new Vector2(1f, 0.21f));       // action bar + hand
            Dim(new Vector2(0f, 0.21f), new Vector2(0.21f, 0.695f)); // player column
            Dim(new Vector2(0.79f, 0.21f), new Vector2(1f, 0.695f)); // log column
            UiKit.AddClick(_targetingRoot.gameObject, () =>
            {
                var pick = _targetingPick;
                var cancel = _targetingCancel;
                bool onTarget = _targetingHoverAny;
                int? target = _targetingHover;
                EndEquipTargeting();
                if (onTarget)
                {
                    pick?.Invoke(target);
                }
                else
                {
                    cancel?.Invoke();
                }
            });

            var copiesGo = new GameObject("TargetCopies", typeof(RectTransform));
            copiesGo.transform.SetParent(_targetingRoot, false);
            _targetingCopiesHost = (RectTransform)copiesGo.transform;
            UiKit.Anchor(_targetingCopiesHost, Vector2.zero, Vector2.one);

            var arrowGo = new GameObject("Arrow", typeof(RectTransform));
            arrowGo.transform.SetParent(_targetingRoot, false);
            _targetingArrowHost = (RectTransform)arrowGo.transform;
            UiKit.Anchor(_targetingArrowHost, Vector2.zero, Vector2.one);

            var cardGo = new GameObject("FloatCard", typeof(RectTransform));
            cardGo.transform.SetParent(_targetingRoot, false);
            _targetingCardHost = (RectTransform)cardGo.transform;
            UiKit.Anchor(_targetingCardHost, Vector2.zero, Vector2.one);

            _targetingRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Dim everything but the board, float the taken card centered in the market
        /// band, and wait for a click on one of your real stands (or the turf).
        /// An optional caption floats above the card ("Word of Mouth — choose a
        /// stand to sell") for picks whose card art alone doesn't say what's asked.
        /// </summary>
        public void BeginEquipTargeting(Texture2D texture, ISet<int?> validTargets,
            System.Action<int?> onPick, System.Action onCancel, string caption = null)
        {
            const float width = 190f;
            const float height = 266f;

            _targetingValid = validTargets;
            _targetingPick = onPick;
            _targetingCancel = onCancel;
            _targetingHover = null;
            _targetingHoverAny = false;
            UiKit.Clear(_targetingCardHost);
            UiKit.Clear(_targetingCopiesHost);
            UiKit.Clear(_targetingArrowHost);

            // The taken card floats centered in the market band.
            var anchor = new Vector2(0, _targetingRoot.rect.height * 0.325f);
            var image = UiKit.CreateCardImage(_targetingCardHost, texture, width, height);
            image.raycastTarget = false;
            var frame = (RectTransform)image.transform.parent;
            frame.GetComponent<Image>().raycastTarget = false;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(width, height);
            frame.anchoredPosition = anchor;
            var shadow = frame.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);
            shadow.effectDistance = new Vector2(0, -6f);
            frame.gameObject.SetActive(texture != null); // no white slab for art-less picks
            _targetingCardBottom = anchor + new Vector2(0, -height * 0.5f);

            if (!string.IsNullOrEmpty(caption))
            {
                var label = UiKit.CreateText(_targetingCardHost, caption, 22,
                    TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.55f));
                label.raycastTarget = false;
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
                labelRect.pivot = new Vector2(0.5f, 0f);
                labelRect.sizeDelta = new Vector2(760f, 34f);
                labelRect.anchoredPosition = anchor + new Vector2(0, height * 0.5f + 12f);
            }

            _targetingRoot.SetAsLastSibling();
            _targetingRoot.gameObject.SetActive(true);
        }

        /// <summary>Called every frame by the app; drives hover glow + the dashed arrow.</summary>
        public void TickEquipTargeting(Vector2 screenPosition)
        {
            if (_targetingPick == null)
            {
                return;
            }
            int? hover = null;
            bool any = false;
            foreach (var (id, cell, _) in _dropCells)
            {
                if (_targetingValid.Contains(id) &&
                    RectTransformUtility.RectangleContainsScreenPoint(cell, screenPosition))
                {
                    hover = id;
                    any = true;
                    break;
                }
            }
            if (any == _targetingHoverAny && hover == _targetingHover)
            {
                return;
            }
            _targetingHover = hover;
            _targetingHoverAny = any;

            // Glow and arrow are rebuilt from the LIVE cell position: the real board
            // is the target surface now, and it can scroll under the overlay.
            UiKit.Clear(_targetingCopiesHost);
            UiKit.Clear(_targetingArrowHost);
            if (any)
            {
                foreach (var (id, cell, _) in _dropCells)
                {
                    if (Equals(id, hover))
                    {
                        var cardFrame = cell.Find("Card") as RectTransform ?? cell;
                        Vector2 local = _targetingRoot.InverseTransformPoint(
                            cardFrame.TransformPoint(cardFrame.rect.center));
                        UiKit.CreateGlow(_targetingCopiesHost,
                            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), local,
                            cardFrame.rect.width + 30, cardFrame.rect.height + 30, DropGlowHot);

                        var world = cell.TransformPoint(
                            new Vector3(cell.rect.center.x, cell.rect.yMax - 16f, 0));
                        UiKit.DrawDashedArrow(_targetingArrowHost, _targetingCardBottom,
                            (Vector2)_targetingRoot.InverseTransformPoint(world),
                            UiKit.ButtonColor);
                        break;
                    }
                }
            }
        }

        /// <summary>Dismiss the targeting layer without invoking pick or cancel.</summary>
        private void EndEquipTargeting()
        {
            _targetingPick = null;
            _targetingCancel = null;
            _targetingValid = null;
            _targetingHover = null;
            _targetingHoverAny = false;
            _targetingRoot.gameObject.SetActive(false);
            UiKit.Clear(_targetingCardHost);
            UiKit.Clear(_targetingCopiesHost);
            UiKit.Clear(_targetingArrowHost);
        }
    }
}
