using System.Collections.Generic;
using UnityEngine;

namespace LemonadeWars.Unity
{
    // Sticky drag: a double-clicked shelf card rides the cursor with no button held,
    // and the next click drops it on a legal target or sends it gliding back to its
    // shelf slot. The ghost belongs to the table, so it survives re-renders.
    public sealed partial class TableView
    {
        private const float GhostReturnSeconds = 0.25f;
        /// <summary>A returning ghost starts fading this far along its glide home.</summary>
        private const float GhostReturnFadeFrom = 0.6f;

        public bool StickyDragActive => _stickyGhost != null;

        /// <summary>A let-go card is back on its shelf slot (or had nowhere to go): the slot may show it again.</summary>
        public System.Action OnStickyReturned;

        /// <summary>Shelf cells go inert while a card is held or gliding: one purchase at a time.</summary>
        private bool ShelfHeld => _stickyGhost != null || _shelfGlidesInFlight > 0;

        private RectTransform _stickyGhost;
        private DragKind _stickyKind;
        private int _stickyMarketIndex;
        private string _stickySupplyTypeId;
        private Vector3? _stickyHomeWorld;
        /// <summary>Placement mode: where the held card goes on a stand click; null for a shelf purchase.</summary>
        private System.Action<int?> _stickyPlace;
        private System.Action _stickyPlaceCancel;
        private int? _stickyHover;
        private bool _stickyHoverAny;
        /// <summary>The double-click that armed the hold must not also drop it.</summary>
        private bool _stickySawRelease;

        /// <summary>
        /// A card with no shelf slot behind it (taken from the discard, the shelf or a
        /// rival's board) rides the cursor until a click on a lit stand places it; a click
        /// anywhere else lets it go.
        /// </summary>
        public void BeginStickyPlace(Texture2D art, ISet<int?> validTargets,
            System.Action<int?> onPlace, System.Action onCancel)
        {
            ArmSticky(DragKind.MarketCard, art, null);
            _stickyPlace = onPlace;
            _stickyPlaceCancel = onCancel;
            SetValidDropTargets(validTargets);
            _preview.SetDragging(true);
        }

        public void BeginMarketStickyDrag(int marketIndex, Texture2D art)
        {
            ArmSticky(DragKind.MarketCard, art, MarketSlotWorld(marketIndex));
            _stickyMarketIndex = marketIndex;
            OnMarketDragStart?.Invoke(marketIndex);
        }

        public void BeginSupplyStickyDrag(string standTypeId)
        {
            if (!_supplyArt.TryGetValue(standTypeId, out var art))
            {
                return;
            }
            ArmSticky(DragKind.SupplyStand, art, StandSupplyWorld(standTypeId));
            _stickySupplyTypeId = standTypeId;
            _supplyDragActive = true;
            _preview.SetDragging(true);
        }

        private void ArmSticky(DragKind kind, Texture2D art, Vector3? homeWorld)
        {
            if (_stickyGhost != null)
            {
                CancelStickyDrag();
            }
            _stickyKind = kind;
            _stickyHomeWorld = homeWorld;
            _stickyHover = null;
            _stickyHoverAny = false;
            _stickySawRelease = false;
            _stickyGhost = BuildShelfGhost(art);
            _stickyGhost.position = Input.mousePosition;
        }

        /// <summary>Call every frame: the ghost follows the cursor and the next click drops or cancels.</summary>
        public void TickStickyDrag(Vector2 screenPosition)
        {
            if (_stickyGhost == null)
            {
                return;
            }
            _stickyGhost.position = screenPosition;
            if (_stickyKind == DragKind.MarketCard)
            {
                TickStickyMarketHover(screenPosition);
            }
            if (!_stickySawRelease)
            {
                _stickySawRelease = !Input.GetMouseButton(0);
                return;
            }
            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }
            if (_stickyKind == DragKind.MarketCard)
            {
                DropStickyMarketCard();
            }
            else
            {
                DropStickySupplyStand(screenPosition);
            }
        }

        private void TickStickyMarketHover(Vector2 screenPosition)
        {
            int? hover = null;
            bool any = false;
            foreach (var (id, cell, _) in _dropCells)
            {
                if (_validDropTargets.Contains(id) &&
                    RectTransformUtility.RectangleContainsScreenPoint(cell, screenPosition))
                {
                    hover = id;
                    any = true;
                    break;
                }
            }
            _stickyHover = hover;
            _stickyHoverAny = any;
            // Every frame: a re-render rebuilds the glows switched off.
            foreach (var (id, glow) in _dropGlows)
            {
                if (glow != null)
                {
                    glow.SetActive(any && Equals(id, hover));
                }
            }
        }

        private void DropStickyMarketCard()
        {
            if (!_stickyHoverAny)
            {
                CancelStickyDrag();
                return;
            }
            int marketIndex = _stickyMarketIndex;
            int? target = _stickyHover;
            var place = _stickyPlace;
            Object.Destroy(EndStickyDrag().gameObject);
            if (place != null)
            {
                place(target);
            }
            else
            {
                OnMarketDrop?.Invoke(marketIndex, target);
            }
        }

        private void DropStickySupplyStand(Vector2 screenPosition)
        {
            if (!_viewingOwnBoard ||
                !RectTransformUtility.RectangleContainsScreenPoint(_boardPanel, screenPosition))
            {
                CancelStickyDrag();
                return;
            }
            string standTypeId = _stickySupplyTypeId;
            int insertIndex = _supplyInsertIndex >= 0 ? _supplyInsertIndex : _standCells.Count;
            Object.Destroy(EndStickyDrag().gameObject);
            OnSupplyDrop?.Invoke(standTypeId, insertIndex);
        }

        /// <summary>Let go without buying: the ghost glides back to its shelf slot.</summary>
        public void CancelStickyDrag()
        {
            if (_stickyGhost == null)
            {
                return;
            }
            var home = _stickyHomeWorld;
            var letGo = _stickyPlaceCancel;
            var ghost = EndStickyDrag();
            if (home == null)
            {
                Object.Destroy(ghost.gameObject);
                OnStickyReturned?.Invoke();
                letGo?.Invoke();
                return;
            }
            var motion = GlideGhost(ghost, ghost.anchoredPosition,
                _canvasRoot.InverseTransformPoint(home.Value), GhostReturnSeconds);
            motion.FadeOutFrom = GhostReturnFadeFrom;
            motion.OnArrive = () => OnStickyReturned?.Invoke();
        }

        /// <summary>Releases the hold's side effects and hands back the ghost for the caller to dispose.</summary>
        private RectTransform EndStickyDrag()
        {
            var ghost = _stickyGhost;
            _stickyGhost = null;
            _stickyPlace = null;
            _stickyPlaceCancel = null;
            if (_stickyKind == DragKind.MarketCard)
            {
                OnMarketDragEnd?.Invoke(); // the app drops the previews and the drop glows
                ClearDropHighlights();
            }
            else
            {
                _supplyDragActive = false;
                _preview.SetDragging(false);
                HideInsertPreview();
            }
            return ghost;
        }
    }
}
