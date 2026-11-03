using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Asset picking on your live board (Rummage Sale, a Timeout fine you must sell
    // for): your tucked Black Market cards and, when allowed, your stands become
    // pickable — the hovered one rings yellow, a click sends it to the staged pile
    // (the app moves it), and a picked asset dims in place until it comes back.
    // Picks are keyed: an equipped card by its instance id, a stand by StandKey.
    public sealed partial class TableView
    {
        /// <summary>Stand instance ids live in the same key space as card ids, offset past any card.</summary>
        public const int StandKeyOffset = 1 << 20;
        public static int StandKey(int standInstanceId) => standInstanceId + StandKeyOffset;
        public static bool IsStandKey(int key) => key >= StandKeyOffset;

        private static readonly Color RummageGlowColor = new Color(1f, 0.93f, 0.45f, 1f);
        private static readonly Color PickedCardTint = new Color(1f, 1f, 1f, 0.3f);

        public bool RummagePickActive => _rummageValid != null;

        /// <summary>A pickable asset on the board was clicked (a pick key).</summary>
        public System.Action<int> OnRummagePick;
        /// <summary>Esc or a click-off: the app decides whether its sale can be torn down.</summary>
        public System.Action OnRummageCancel;
        /// <summary>True while something else owns the pointer (the staged pile fanned
        /// out over the board): no board asset hovers or takes the click under it.</summary>
        public System.Func<bool> RummagePointerCovered;

        private ISet<int> _rummageValid;
        private ISet<int> _rummageValidStands = new HashSet<int>();
        private readonly HashSet<int> _rummagePicked = new HashSet<int>();

        public void BeginRummagePick(ISet<int> equippedIds, ISet<int> standIds = null)
        {
            _rummageValid = equippedIds;
            _rummageValidStands = standIds ?? new HashSet<int>();
            _rummagePicked.Clear();
        }

        public void SetRummagePicked(IEnumerable<int> pickedKeys)
        {
            _rummagePicked.Clear();
            _rummagePicked.UnionWith(pickedKeys);
        }

        public void EndRummagePick()
        {
            if (_rummageValid == null)
            {
                return;
            }
            _rummageValid = null;
            _rummageValidStands = new HashSet<int>();
            _rummagePicked.Clear();
            foreach (var (_, strip, glow) in _equipCells)
            {
                RestoreEquipCell(strip, glow);
            }
            foreach (var (id, cell, _) in _dropCells)
            {
                if (id is int standId)
                {
                    TintStandCard(cell, Color.white);
                    LightStandGlow(standId, false);
                }
            }
        }

        /// <summary>World centre of a pickable asset: a tucked card's strip, or a stand's card; null once it is off the board.</summary>
        public Vector3? RummagePickWorld(int key)
        {
            if (!IsStandKey(key))
            {
                return EquipStripWorld(key);
            }
            int standId = key - StandKeyOffset;
            foreach (var (id, cell, _) in _dropCells)
            {
                if (id == standId)
                {
                    return BoardCardWorld(cell);
                }
            }
            return null;
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
            bool covered = RummagePointerCovered?.Invoke() == true;
            int? hover = null;
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
            foreach (var (id, cell, _) in _dropCells)
            {
                if (!(id is int standId) || cell == null)
                {
                    continue;
                }
                int key = StandKey(standId);
                bool picked = _rummagePicked.Contains(key);
                bool pickable = _rummageValidStands.Contains(standId) && !picked;
                bool hovered = pickable && !covered && hover == null &&
                    RectTransformUtility.RectangleContainsScreenPoint(cell, screenPosition);
                if (hovered)
                {
                    hover = key;
                }
                LightStandGlow(standId, hovered);
                TintStandCard(cell, picked ? PickedCardTint : Color.white);
            }
            if (hover is int clicked && Input.GetMouseButtonDown(0))
            {
                OnRummagePick?.Invoke(clicked);
            }
        }

        private void LightStandGlow(int standId, bool on)
        {
            foreach (var (id, glow) in _dropGlows)
            {
                if (glow != null && Equals(id, standId))
                {
                    glow.SetActive(on);
                }
            }
        }

        private static void TintStandCard(RectTransform cell, Color tint)
        {
            var card = cell.Find("Card") as RectTransform;
            var face = card != null ? card.GetComponentInChildren<RawImage>() : null;
            if (face != null)
            {
                face.color = tint;
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
