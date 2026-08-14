using UnityEngine;
using UnityEngine.EventSystems;

namespace LemonadeWars.Unity
{
    /// <summary>A board cell (turf or stand) that accepts dragged market cards and stands.</summary>
    public sealed class DropTarget : MonoBehaviour, IDropHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Stand instance id, or null for the turf.</summary>
        public int? StandInstanceId;
        public System.Action<int, int?> Dropped;
        /// <summary>Supply stands dropped on a cell still insert at the previewed position.</summary>
        public System.Action<string> SupplyDropped;
        /// <summary>Bragging Rights dropped here (the VP column cell).</summary>
        public System.Action BraggingDropped;
        /// <summary>Fires while a drag hovers this cell (true) or leaves it (false).</summary>
        public System.Action<int?, bool> HoverChanged;

        public void OnDrop(PointerEventData eventData)
        {
            var source = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<DragSource>()
                : null;
            if (source == null || !source.Dragging)
            {
                return;
            }
            if (source.Kind == DragKind.BraggingRights)
            {
                BraggingDropped?.Invoke();
            }
            else if (source.Kind == DragKind.SupplyStand)
            {
                SupplyDropped?.Invoke(source.SupplyTypeId);
            }
            else
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
