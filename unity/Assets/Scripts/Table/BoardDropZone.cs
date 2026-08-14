using UnityEngine;
using UnityEngine.EventSystems;

namespace LemonadeWars.Unity
{
    /// <summary>The board background: catches supply-stand drops between/around the cells.</summary>
    public sealed class BoardDropZone : MonoBehaviour, IDropHandler
    {
        public System.Action<string> SupplyDropped;

        public void OnDrop(PointerEventData eventData)
        {
            var source = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<DragSource>()
                : null;
            if (source != null && source.Dragging && source.Kind == DragKind.SupplyStand)
            {
                SupplyDropped?.Invoke(source.SupplyTypeId);
            }
        }
    }
}
