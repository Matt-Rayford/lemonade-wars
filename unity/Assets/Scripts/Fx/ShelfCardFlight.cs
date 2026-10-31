using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// One Black Market card's part in a shelf deal: where it sits (or lands) and
    /// what it looks like, both resolved when the effect starts, since the shelf is
    /// rebuilt between queueing and playing.
    /// </summary>
    public sealed class ShelfCardFlight
    {
        public System.Func<Texture2D> Art;
        public System.Func<Vector3?> SlotWorld;
        /// <summary>Dealt cards only: the slot may show its real card now. Fires exactly once on every exit path.</summary>
        public System.Action OnLanded;
    }
}
