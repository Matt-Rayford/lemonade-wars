using System.Collections.Generic;
using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class EffectsPlayer
    {
        private abstract class Fx { }

        // ---- passive: released in batches, never hold the table

        private sealed class MoneyFx : Fx { public int PlayerId; public int Amount; }

        private sealed class SoundFx : Fx { public string Name; }

        private sealed class CallbackFx : Fx { public System.Action Run; }

        // ---- blocking: one at a time

        private sealed class ToastFx : Fx { public string Text; }

        private sealed class HoldFx : Fx
        {
            public System.Action Start;
            public System.Action OnSkipped;
            public float Seconds;
        }

        private sealed class RevealFx : Fx
        {
            public Texture2D Art;
            public string Title;
        }

        /// <summary>
        /// A reveal that then flies to Destination. It announces a state change (the Whiniest
        /// Baby), not a play a modal will narrate, so CancelPendingReveals keeps it.
        /// </summary>
        private sealed class ClaimFx : Fx
        {
            public Texture2D Art;
            public string Title;
            /// <summary>Resolved when the flight begins: the fan it joins may have re-laid-out since queueing.</summary>
            public System.Func<Vector3> Destination;
            /// <summary>Fires exactly once, on landing or skip: the persistent card may appear where the flight ended.</summary>
            public System.Action OnDelivered;
        }

        private sealed class FlyFx : Fx
        {
            /// <summary>Null for a coin flight.</summary>
            public Texture2D CardBack;
            public int FromPlayerId;
            public int ToPlayerId;
            /// <summary>Coin flights only: shown as floaters at both ends on arrival.</summary>
            public int Amount;
            public int Count = 1;
        }

        /// <summary>
        /// A shelf purchase made visible: the card lifts out of its slot, shows mid-table,
        /// then flies to the buyer. Like ClaimFx, CancelPendingReveals keeps it.
        /// </summary>
        private sealed class PurchaseFx : Fx
        {
            public Texture2D Art;
            public int BuyerId;
            /// <summary>0 for free takes, which show no money.</summary>
            public int Price;
            public System.Func<int, Texture2D> BillArt;
            /// <summary>False for the viewer's own buys: they just clicked the card, so only the money plays.</summary>
            public bool ShowCard = true;
            /// <summary>Resolved when the effect starts (layout settles in between). Null result = deliver with no motion.</summary>
            public System.Func<Vector3?> Origin;
            /// <summary>Fires exactly once on every exit path: a suppressed refill slot may show its new card.</summary>
            public System.Action OnDelivered;
            /// <summary>The lifted card is on screen: its shelf stand-in may go.</summary>
            public System.Action OnStarted;
        }

        /// <summary>
        /// The shelf changing hands with the deck: swept cards slide under the Black Market
        /// deck, then each dealt card slides out to its slot. A paid refresh slaps its dollar first.
        /// </summary>
        private sealed class MarketDealFx : Fx
        {
            public System.Func<Vector3?> DeckWorld;
            public List<ShelfCardFlight> Sweep = new List<ShelfCardFlight>();
            /// <summary>Every deal's OnLanded fires exactly once, on every exit path.</summary>
            public List<ShelfCardFlight> Deals = new List<ShelfCardFlight>();
            /// <summary>Who paid for a refresh; -1 = no money shown.</summary>
            public int PayerId = -1;
            public int Price;
            public System.Func<int, Texture2D> BillArt;
            /// <summary>The flights are on screen: stand-ins on the shelf may go.</summary>
            public System.Action OnStarted;
        }

        private sealed class EarnFx : Fx
        {
            public int PlayerId;
            public int Amount;
            public System.Func<int, Texture2D> BillArt;
            /// <summary>The player's displayed cash may catch up; fires on every exit path.</summary>
            public System.Action OnArrived;
        }
    }
}
