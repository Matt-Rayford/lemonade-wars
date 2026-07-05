using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Draggable market card: lifts with a glow on hover, spawns a ghost that follows the
    /// pointer while dragging. Drop resolution happens in <see cref="DropTarget"/>.
    /// </summary>
    public sealed class DragSource : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public int MarketIndex;
        public Texture2D Texture;
        public RectTransform CanvasRoot;
        public RectTransform LiftTarget;
        public GameObject GlowInner;
        public GameObject GlowOuter;
        public System.Func<int, bool> CanAct;
        public System.Action<int> DragStarted;
        public System.Action DragEnded;

        public bool Dragging { get; private set; }

        private RectTransform _ghost;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (CanAct == null || !CanAct(MarketIndex))
            {
                return;
            }
            SetHover(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!Dragging)
            {
                SetHover(false);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (CanAct == null || !CanAct(MarketIndex))
            {
                return;
            }
            Dragging = true;

            var ghostGo = new GameObject("DragGhost", typeof(RectTransform), typeof(RawImage));
            ghostGo.transform.SetParent(CanvasRoot, false);
            _ghost = (RectTransform)ghostGo.transform;
            _ghost.sizeDelta = new Vector2(150, 210);
            var image = ghostGo.GetComponent<RawImage>();
            image.texture = Texture;
            image.raycastTarget = false; // drops must reach the targets underneath
            image.color = new Color(1f, 1f, 1f, 0.92f);
            _ghost.position = eventData.position;

            DragStarted?.Invoke(MarketIndex);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_ghost != null)
            {
                _ghost.position = eventData.position;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            // OnDrop on the target (if any) fires before OnEndDrag.
            Dragging = false;
            if (_ghost != null)
            {
                Destroy(_ghost.gameObject);
                _ghost = null;
            }
            SetHover(false);
            DragEnded?.Invoke();
        }

        private void SetHover(bool on)
        {
            if (GlowInner != null)
            {
                GlowInner.SetActive(on);
            }
            if (GlowOuter != null)
            {
                GlowOuter.SetActive(on);
            }
            if (LiftTarget != null)
            {
                UiTween.SlideTo(LiftTarget, on ? new Vector2(0, 12) : Vector2.zero, 0.12f);
            }
        }
    }

    /// <summary>
    /// Minimal hover/click relay. Used instead of EventTrigger, which implements EVERY
    /// event interface (including IDropHandler) and therefore swallows drop events that
    /// should bubble up to a <see cref="DropTarget"/> on a parent.
    /// </summary>
    public sealed class PointerRelay : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public System.Action Entered;
        public System.Action Exited;
        public System.Action Clicked;

        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke();
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    }

    /// <summary>A board cell (turf or stand) that accepts a dragged market card.</summary>
    public sealed class DropTarget : MonoBehaviour, IDropHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Stand instance id, or null for the turf.</summary>
        public int? StandInstanceId;
        public System.Action<int, int?> Dropped;
        /// <summary>Fires while a drag hovers this cell (true) or leaves it (false).</summary>
        public System.Action<int?, bool> HoverChanged;

        public void OnDrop(PointerEventData eventData)
        {
            var source = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<DragSource>()
                : null;
            if (source != null && source.Dragging)
            {
                Dropped?.Invoke(source.MarketIndex, StandInstanceId);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (eventData.dragging)
            {
                HoverChanged?.Invoke(StandInstanceId, true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HoverChanged?.Invoke(StandInstanceId, false);
        }
    }
}
