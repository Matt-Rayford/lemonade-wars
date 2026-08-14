using UnityEngine;
using UnityEngine.EventSystems;

namespace LemonadeWars.Unity
{
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

        /// <summary>
        /// Screen switches deactivate whatever the cursor happens to be over, and Unity
        /// never sends the matching exit — so a menu button would come back still
        /// wearing its hover paint (two "selected" rows at once). Release it here.
        /// </summary>
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
                // Deactivation during teardown: the handler's targets are already
                // gone, and a destroyed object has no hover state left to clear.
            }
        }
    }
}
