using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Asset picking on your live board (Rummage Sale, a Timeout fine you must sell
    // for): tucked Black Market cards and, when allowed, stands ring yellow on hover,
    // and a click hands the asset to the app, which moves it to the staged pile. A
    // picked card leaves its stack, which closes up; a picked stand dims in place.
    public sealed partial class TableView
    {
        /// <summary>A pick is keyed by a card's instance id, or by a stand's id offset past any card id.</summary>
        public const int StandKeyOffset = 1 << 20;
        public static int StandKey(int standInstanceId) => standInstanceId + StandKeyOffset;
        public static bool IsStandKey(int key) => key >= StandKeyOffset;

        private static readonly Color RummageGlowColor = new Color(1f, 0.93f, 0.45f, 1f);
        private static readonly Color PickedStandTint = new Color(1f, 1f, 1f, 0.3f);

        public bool RummagePickActive => _rummageValid != null;

        /// <summary>A pickable asset on the board was clicked (a pick key).</summary>
        public System.Action<int> OnRummagePick;
        /// <summary>Esc or a click-off: the app decides whether its sale can be torn down.</summary>
        public System.Action OnRummageCancel;
        /// <summary>True while the fanned staged pile owns the pointer: without it a click on the pile also fired on the asset under it.</summary>
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
            foreach (var (_, _, glow) in _equipCells)
            {
                RestoreEquipCell(glow);
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

        /// <summary>World centre of a pickable asset (a tucked card's strip, or a stand's card); null once it is off the board.</summary>
        public Vector3? RummagePickWorld(int key)
        {
            if (!IsStandKey(key))
            {
                return EquipStripWorld(key) ?? EquipHomeWorld(key);
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

        /// <summary>Where a card picked for a sale flies back to: the top of the stack it left.</summary>
        private Vector3? EquipHomeWorld(int instanceId) =>
            _equipHomeCells.TryGetValue(instanceId, out var cell) && cell != null
                ? cell.TransformPoint(new Vector3(cell.rect.center.x, cell.rect.yMax, 0f))
                : (Vector3?)null;

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

        /// <summary>
        /// Call every frame: takes the click, and re-asserts the rings and dims, which
        /// every render rebuilds switched off.
        /// </summary>
        public void TickRummagePick(Vector2 screenPosition)
        {
            if (_rummageValid == null)
            {
                return;
            }
            bool covered = RummagePointerCovered?.Invoke() == true;
            int? hover = RefreshRummageCards(screenPosition, covered);
            hover = RefreshRummageStands(screenPosition, covered, hover);
            if (hover is int clicked && Input.GetMouseButtonDown(0))
            {
                OnRummagePick?.Invoke(clicked);
            }
        }

        /// <returns>The hovered pickable card's key, if any.</returns>
        private int? RefreshRummageCards(Vector2 screenPosition, bool covered)
        {
            int? hover = null;
            foreach (var (id, strip, glow) in _equipCells)
            {
                if (strip == null)
                {
                    continue;
                }
                // A picked card is off the board (its stack closed up), so every strip here is still on offer.
                bool hovered = _rummageValid.Contains(id) && !covered && hover == null &&
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
            }
            return hover;
        }

        /// <returns>The hovered pickable stand's key, or the card already hovered: only one asset hovers.</returns>
        private int? RefreshRummageStands(Vector2 screenPosition, bool covered, int? hover)
        {
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
                TintStandCard(cell, picked ? PickedStandTint : Color.white);
            }
            return hover;
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

        private static void RestoreEquipCell(GameObject glow)
        {
            if (glow != null)
            {
                glow.GetComponent<Image>().color = AttackGlowColor;
                glow.SetActive(false);
            }
        }
    }
}
