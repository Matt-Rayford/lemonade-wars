using UnityEngine;
using UnityEngine.EventSystems;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Hover/click relay used instead of EventTrigger, which implements EVERY event interface
    /// (IDropHandler included) and so swallows drops meant for a <see cref="DropTarget"/> on a parent.
    /// </summary>
    public sealed class PointerRelay : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public System.Action Entered;
        public System.Action Exited;
        public System.Action Clicked;

        private bool _inside;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _inside = true;
            Entered?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _inside = false;
            Exited?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();

        /// <summary>Unity sends no exit when a screen switch deactivates what the cursor is over,
        /// so a button would come back still wearing its hover paint.</summary>
        private void OnDisable()
        {
            if (!_inside)
            {
                return;
            }
            _inside = false;
            try
            {
                Exited?.Invoke();
            }
            catch (MissingReferenceException)
            {
                // Teardown: the handler's targets are already destroyed, with no hover state left to clear.
            }
        }
    }
}
