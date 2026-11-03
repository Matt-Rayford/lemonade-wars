using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Rummage Sale: the board stays live and YOUR tucked Black Market cards become
    // pickable — the hovered one rings yellow, a click sends it to the staged play's
    // pile (the app moves it), and a picked card dims on its stand until it comes back.
    public sealed partial class TableView
    {
        private static readonly Color RummageGlowColor = new Color(1f, 0.93f, 0.45f, 1f);
        private static readonly Color PickedCardTint = new Color(1f, 1f, 1f, 0.3f);

        public bool RummagePickActive => _rummageValid != null;

        /// <summary>A pickable card on the board was clicked.</summary>
        public System.Action<int> OnRummagePick;
        /// <summary>Esc or a click-off: the app tears its sale down.</summary>
        public System.Action OnRummageCancel;
        /// <summary>True while something else owns the pointer (the staged pile fanned
        /// out over the board): no board card hovers or takes the click under it.</summary>
        public System.Func<bool> RummagePointerCovered;

        private ISet<int> _rummageValid;
        private readonly HashSet<int> _rummagePicked = new HashSet<int>();

        public void BeginRummagePick(ISet<int> validIds)
        {
            _rummageValid = validIds;
            _rummagePicked.Clear();
        }

        public void SetRummagePicked(IEnumerable<int> picked)
        {
            _rummagePicked.Clear();
            _rummagePicked.UnionWith(picked);
        }

        public void EndRummagePick()
        {
            if (_rummageValid == null)
            {
                return;
            }
            _rummageValid = null;
            _rummagePicked.Clear();
            foreach (var (_, strip, glow) in _equipCells)
            {
                RestoreEquipCell(strip, glow);
            }
        }

        /// <summary>World centre of a tucked card's visible strip; null once it is off the board.</summary>
        public Vector3? EquipStripWorld(int instanceId)
        {
            foreach (var (id, strip, _) in _equipCells)
            {
                if (id == instanceId && strip != null)
                {
                    return strip.TransformPoint(strip.rect.center);
                }
            }
            return null;
        }

        /// <summary>Called every frame by the app: re-asserts the rings and dims (cells are rebuilt on every render) and takes the click.</summary>
        public void TickRummagePick(Vector2 screenPosition)
        {
            if (_rummageValid == null)
            {
                return;
            }
            int? hover = null;
            bool covered = RummagePointerCovered?.Invoke() == true;
            foreach (var (id, strip, glow) in _equipCells)
            {
                if (strip == null)
                {
                    continue;
                }
                bool picked = _rummagePicked.Contains(id);
                bool pickable = _rummageValid.Contains(id) && !picked;
                bool hovered = pickable && !covered && hover == null &&
                    RectTransformUtility.RectangleContainsScreenPoint(strip, screenPosition);
                if (hovered)
                {
                    hover = id;
                }
                if (glow != null)
                {
                    glow.GetComponent<Image>().color = RummageGlowColor;
                    glow.SetActive(hovered);
                }
                if (strip.parent is RectTransform card && card.GetComponent<RawImage>() is RawImage face)
                {
                    face.color = picked ? PickedCardTint : Color.white;
                }
            }
            if (hover is int clicked && Input.GetMouseButtonDown(0))
            {
                OnRummagePick?.Invoke(clicked);
            }
        }

        private static void RestoreEquipCell(RectTransform strip, GameObject glow)
        {
            if (glow != null)
            {
                glow.GetComponent<Image>().color = AttackGlowColor;
                glow.SetActive(false);
            }
            if (strip != null && strip.parent is RectTransform card &&
                card.GetComponent<RawImage>() is RawImage face)
            {
                face.color = Color.white;
            }
        }
    }
}
