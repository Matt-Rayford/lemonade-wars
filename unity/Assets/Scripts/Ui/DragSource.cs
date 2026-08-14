using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Draggable table object (market card or supply stand): lifts with a glow on hover,
    /// spawns a ghost that follows the pointer while dragging. Drop resolution happens in
    /// <see cref="DropTarget"/> / <see cref="BoardDropZone"/>.
    /// </summary>
    public sealed class DragSource : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public DragKind Kind;
        public int MarketIndex;
        public string SupplyTypeId = "";
        public Texture2D Texture;
        public RectTransform CanvasRoot;
        public RectTransform LiftTarget;
        public GameObject GlowInner;
        public GameObject GlowOuter;
        public System.Func<bool> CanAct;
        public System.Action DragStarted;
        public System.Action DragEnded;

        public bool Dragging { get; private set; }

        private RectTransform _ghost;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (CanAct == null || !CanAct())
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
            if (CanAct == null || !CanAct())
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

            DragStarted?.Invoke();
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
}
