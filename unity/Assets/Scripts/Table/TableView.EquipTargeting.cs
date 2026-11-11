using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Equip targeting: the table dims except the board band, the taken card floats up
    // top, and the player aims at the live board (tucked upgrades visible, so a full
    // stand reads as full) with an arrow to the hovered cell.
    public sealed partial class TableView
    {
        /// <summary>While true the modal pipeline stands down, as for an open prompt (see MaybeShowModal).</summary>
        public bool EquipTargetingActive => _targetingPick != null;

        /// <summary>Extra size of the hot ring on the hovered aim target, both sides together.</summary>
        private const float AimGlowPx = 21f;
        private const float TargetingCardWidthPx = 190f;
        private const float TargetingCardHeightPx = 266f;

        private RectTransform _targetingRoot;
        private RectTransform _targetingCardHost;
        private RectTransform _targetingCopiesHost;
        private RectTransform _targetingArrowHost;
        private ISet<int?> _targetingValid;
        /// <summary>Equipped-card pick mode: pickable tucked-card ids (null = stand mode).</summary>
        private ISet<int> _targetingEquipValid;
        private System.Action<int?> _targetingPick;
        private System.Action _targetingCancel;
        private int? _targetingHover;
        private bool _targetingHoverAny;
        private Vector2 _targetingCardBottom;

        /// <summary>
        /// An invisible full-screen layer that takes the pointer and hit-tests the board
        /// cells itself, so hover and clicks do not fight the cells' own handlers.
        /// </summary>
        private void BuildEquipTargeting()
        {
            _targetingRoot = UiKit.CreatePanel(_canvasRoot, "EquipTargeting", new Color(0, 0, 0, 0));
            UiKit.Anchor(_targetingRoot, Vector2.zero, Vector2.one);
            var dimColor = new Color(0f, 0f, 0f, 0.66f);
            void Dim(Vector2 min, Vector2 max)
            {
                var panel = UiKit.CreatePanel(_targetingRoot, "Dim", dimColor);
                panel.GetComponent<Image>().raycastTarget = false;
                UiKit.Anchor(panel, min, max);
            }
            Dim(new Vector2(0f, BoardTop), new Vector2(1f, 1f));                     // market band + up
            Dim(new Vector2(0f, 0f), new Vector2(1f, BoardBottom));                  // action bar + hand
            Dim(new Vector2(0f, BoardBottom), new Vector2(BoardLeft, BoardTop));     // player column + stage
            Dim(new Vector2(BoardRight, BoardBottom), new Vector2(1f, BoardTop));    // whatever lies right of the board
            UiKit.AddClick(_targetingRoot.gameObject, PickOrCancelEquipTargeting);

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

        private void PickOrCancelEquipTargeting()
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
        }

        /// <summary>Waits for a click on one of your stands (or the turf).</summary>
        /// <param name="caption">Floats above the card, for picks whose art alone does not say what is asked.</param>
        public void BeginEquipTargeting(Texture2D texture, ISet<int?> validTargets,
            System.Action<int?> onPick, System.Action onCancel, string caption = null)
        {
            _targetingValid = validTargets;
            _targetingEquipValid = null;
            ArmTargetingLayer(texture, onPick, onCancel, caption);
        }

        /// <summary>
        /// The same layer aimed at a board's equipped cards, each ringed in place with its
        /// stand in view. The caller shows the right board first.
        /// </summary>
        public void BeginEquippedCardPick(Texture2D texture, ISet<int> validEquipped,
            System.Action<int> onPick, System.Action onCancel, string caption)
        {
            _targetingValid = null;
            _targetingEquipValid = validEquipped;
            ArmTargetingLayer(texture, id => onPick(id.Value), onCancel, caption);
        }

        private void ArmTargetingLayer(Texture2D texture,
            System.Action<int?> onPick, System.Action onCancel, string caption)
        {
            _targetingPick = onPick;
            _targetingCancel = onCancel;
            _targetingHover = null;
            _targetingHoverAny = false;
            UiKit.Clear(_targetingCardHost);
            UiKit.Clear(_targetingCopiesHost);
            UiKit.Clear(_targetingArrowHost);

            var marketBandCentre = new Vector2(0, _targetingRoot.rect.height * 0.325f);
            FloatTakenCard(texture, marketBandCentre);
            if (!string.IsNullOrEmpty(caption))
            {
                AddTargetingCaption(caption, marketBandCentre);
            }

            _targetingRoot.SetAsLastSibling(); // over the HUD strip, which is built after the table
            _targetingRoot.gameObject.SetActive(true);
        }

        private void FloatTakenCard(Texture2D texture, Vector2 centre)
        {
            var image = UiKit.CreateCardImage(_targetingCardHost, texture,
                TargetingCardWidthPx, TargetingCardHeightPx);
            image.raycastTarget = false;
            var frame = (RectTransform)image.transform.parent;
            frame.GetComponent<Image>().raycastTarget = false;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(TargetingCardWidthPx, TargetingCardHeightPx);
            frame.anchoredPosition = centre;
            var shadow = frame.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);
            shadow.effectDistance = new Vector2(0, -6f);
            frame.gameObject.SetActive(texture != null); // no white slab for art-less picks
            _targetingCardBottom = centre + new Vector2(0, -TargetingCardHeightPx * 0.5f);
        }

        private void AddTargetingCaption(string caption, Vector2 cardCentre)
        {
            var label = UiKit.CreateText(_targetingCardHost, caption, 22,
                TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.55f));
            label.raycastTarget = false;
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.sizeDelta = new Vector2(760f, 34f);
            labelRect.anchoredPosition = cardCentre + new Vector2(0, TargetingCardHeightPx * 0.5f + 12f);
        }

        /// <summary>Call every frame: drives the hover ring and the arrow.</summary>
        public void TickEquipTargeting(Vector2 screenPosition)
        {
            if (_targetingPick == null)
            {
                return;
            }
            if (_targetingEquipValid != null)
            {
                TickEquippedCardPick(screenPosition);
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

            UiKit.Clear(_targetingCopiesHost);
            UiKit.Clear(_targetingArrowHost);
            if (any)
            {
                foreach (var (id, cell, _) in _dropCells)
                {
                    if (Equals(id, hover))
                    {
                        var cardFrame = cell.Find("Card") as RectTransform ?? cell;
                        RingAndPointAt(cardFrame, cell.TransformPoint(
                            new Vector3(cell.rect.center.x, cell.rect.yMax - 16f, 0)));
                        break;
                    }
                }
            }
        }

        private void TickEquippedCardPick(Vector2 screenPosition)
        {
            int? hover = null;
            foreach (var (id, strip, _) in _equipCells)
            {
                if (_targetingEquipValid.Contains(id) && strip != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(strip, screenPosition))
                {
                    hover = id;
                    break;
                }
            }
            foreach (var (id, _, glow) in _equipCells)
            {
                if (glow != null)
                {
                    // Every tick: a re-render rebuilds the cells with their rings off.
                    glow.SetActive(hover == id);
                }
            }
            bool any = hover != null;
            if (any == _targetingHoverAny && hover == _targetingHover)
            {
                return;
            }
            _targetingHover = hover;
            _targetingHoverAny = any;
            UiKit.Clear(_targetingCopiesHost);
            UiKit.Clear(_targetingArrowHost);
            if (!any)
            {
                return;
            }
            foreach (var (id, strip, _) in _equipCells)
            {
                if (id == hover.Value)
                {
                    RingAndPointAt(strip, strip.TransformPoint(
                        new Vector3(strip.rect.center.x, strip.rect.yMax, 0)));
                    break;
                }
            }
        }

        /// <summary>Built from the target's live position: the board can scroll under the overlay.</summary>
        private void RingAndPointAt(RectTransform ringed, Vector3 arrowTipWorld)
        {
            Vector2 local = _targetingRoot.InverseTransformPoint(
                ringed.TransformPoint(ringed.rect.center));
            UiKit.CreateGlow(_targetingCopiesHost,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), local,
                ringed.rect.width + AimGlowPx, ringed.rect.height + AimGlowPx, DropGlowHot);
            UiKit.DrawArrow(_targetingArrowHost, _targetingCardBottom,
                (Vector2)_targetingRoot.InverseTransformPoint(arrowTipWorld),
                UiKit.ButtonColor);
        }

        /// <summary>Dismisses the layer without invoking pick or cancel.</summary>
        private void EndEquipTargeting()
        {
            if (_targetingEquipValid != null)
            {
                foreach (var (_, _, glow) in _equipCells)
                {
                    if (glow != null)
                    {
                        glow.SetActive(false);
                    }
                }
                _targetingEquipValid = null;
            }
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
