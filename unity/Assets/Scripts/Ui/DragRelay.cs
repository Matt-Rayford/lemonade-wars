using UnityEngine;
using UnityEngine.EventSystems;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Raw drag relay (hand cards): forwards pointer positions and nothing else — no
    /// ghost, no glow. The owner decides what a drag means.
    /// </summary>
    public sealed class DragRelay : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public System.Action<Vector2> Began;
        public System.Action<Vector2> Moved;
        public System.Action<Vector2> Ended;

        public void OnBeginDrag(PointerEventData eventData) => Began?.Invoke(eventData.position);
        public void OnDrag(PointerEventData eventData) => Moved?.Invoke(eventData.position);
        public void OnEndDrag(PointerEventData eventData) => Ended?.Invoke(eventData.position);
    }
}
