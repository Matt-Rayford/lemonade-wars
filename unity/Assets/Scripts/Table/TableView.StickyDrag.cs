using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Sticky drag: a double-clicked shelf card rides the cursor with no button held —
    // the drag-and-drop language, with the drop decided by a second click. A click
    // off any legal target sends the card gliding back to its shelf slot. The ghost
    // belongs to the table, not the shelf cell, so it survives re-renders.
    public sealed partial class TableView
    {
        private const float GhostWidthPx = 150f;
        private const float GhostHeightPx = 210f;
        private const float GhostAlpha = 0.92f;
        private const float GhostReturnSeconds = 0.25f;
        private const float ShelfGlideSeconds = 0.3f;
        /// <summary>A returning ghost starts fading this far along its glide home.</summary>
        private const float GhostReturnFadeFrom = 0.6f;

        public bool StickyDragActive => _stickyGhost != null;

        /// <summary>Shelf cells go inert while a card is held or gliding: one purchase at a time.</summary>
        private bool ShelfHeld => _stickyGhost != null || _shelfGlidesInFlight > 0;

        private RectTransform _stickyGhost;
        private DragKind _stickyKind;
        private int _stickyMarketIndex;
        private string _stickySupplyTypeId;
        private Vector3? _stickyHomeWorld;
        private int? _stickyHover;
        private bool _stickyHoverAny;
        /// <summary>The double-click that armed the hold must not also drop it.</summary>
        private bool _stickySawRelease;
        private int _shelfGlidesInFlight;

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

        /// <summary>Called every frame by the app: the ghost follows the cursor, hover
        /// glows track it, and the next click drops or cancels.</summary>
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
            // Re-asserted every frame: a re-render rebuilds the glows switched off.
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
            Object.Destroy(EndStickyDrag().gameObject);
            OnMarketDrop?.Invoke(marketIndex, target);
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
            var ghost = EndStickyDrag();
            if (home == null)
            {
                Object.Destroy(ghost.gameObject);
                return;
            }
            var motion = ghost.gameObject.AddComponent<FlightMotion>();
            motion.From = ghost.anchoredPosition;
            motion.To = _canvasRoot.InverseTransformPoint(home.Value);
            motion.Duration = GhostReturnSeconds;
            motion.ArcHeight = 0f;
            motion.VisibleWhileWaiting = true;
            motion.FadeOutFrom = GhostReturnFadeFrom;
        }

        /// <summary>Leaving the table: no ghost may outlive it, held or mid-glide.</summary>
        public void DropShelfGhosts()
        {
            if (_stickyGhost != null)
            {
                Object.Destroy(EndStickyDrag().gameObject);
            }
            foreach (var ghost in _canvasRoot.GetComponentsInChildren<ShelfGhostTag>())
            {
                Object.Destroy(ghost.gameObject);
            }
            _shelfGlidesInFlight = 0;
        }

        /// <summary>Release the hold's side effects and hand back the ghost for the caller to dispose.</summary>
        private RectTransform EndStickyDrag()
        {
            var ghost = _stickyGhost;
            _stickyGhost = null;
            if (_stickyKind == DragKind.MarketCard)
            {
                OnMarketDragEnd?.Invoke();
            }
            else
            {
                _supplyDragActive = false;
                _preview.SetDragging(false);
                HideInsertPreview();
            }
            return ghost;
        }

        /// <summary>
        /// A card leaves its shelf slot and slides straight to where it is about to
        /// land — the no-decision purchase, which otherwise just pops into place.
        /// The buy itself fires on arrival; with either end unknown it fires at once.
        /// </summary>
        public void GlideShelfCard(Texture2D art, Vector3? fromWorld, Vector3? toWorld,
            System.Action onArrive)
        {
            if (fromWorld == null || toWorld == null)
            {
                onArrive?.Invoke();
                return;
            }
            _shelfGlidesInFlight++;
            var ghost = BuildShelfGhost(art);
            var motion = ghost.gameObject.AddComponent<FlightMotion>();
            motion.From = _canvasRoot.InverseTransformPoint(fromWorld.Value);
            motion.To = _canvasRoot.InverseTransformPoint(toWorld.Value);
            motion.Duration = ShelfGlideSeconds;
            motion.ArcHeight = 0f;
            motion.VisibleWhileWaiting = true;
            motion.OnArrive = () =>
            {
                _shelfGlidesInFlight = Mathf.Max(0, _shelfGlidesInFlight - 1);
                onArrive?.Invoke();
            };
        }

        private RectTransform BuildShelfGhost(Texture2D art)
        {
            var go = new GameObject("ShelfGhost", typeof(RectTransform), typeof(RawImage),
                typeof(ShelfGhostTag));
            go.transform.SetParent(_canvasRoot, false);
            go.transform.SetAsLastSibling();
            var ghost = (RectTransform)go.transform;
            ghost.sizeDelta = new Vector2(GhostWidthPx, GhostHeightPx);
            var image = go.GetComponent<RawImage>();
            image.texture = art;
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, GhostAlpha);
            return ghost;
        }
    }
}
