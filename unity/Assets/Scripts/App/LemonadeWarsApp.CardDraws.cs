using System.Collections.Generic;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>Cards drawn into the hand whose flight has not landed: drawn invisible in place.</summary>
        private readonly HashSet<int> _handCardsInFlight = new HashSet<int>();
        /// <summary>The drawn Timeout is still on its way from the pile to the middle of the table.</summary>
        private bool _timeoutCardInFlight;
        private float _timeoutCardShownAt = -100f; // well before any landing, so nothing lingers at boot

        private void HandleOwnCardDrawn(CardDrawn drawn)
        {
            // Queued, not played: the turn-start draw waits behind ONWARD!.
            _fx.QueueSound(Sfx.CardDraw);
            int drawnId = drawn.InstanceId;
            var drawnArt = _art.Lemon(drawn.DefId);
            // The fan holds the card's slot open while it flies face-up from the pile.
            _handCardsInFlight.Add(drawnId);
            _renderedRevision = -1;
            void Fly() => _table.GlideCardToHand(drawnArt, _table.LemonPileWorld(), drawnId, () =>
            {
                _handCardsInFlight.Remove(drawnId);
                _renderedRevision = -1;
            });
            if (drawn.Reason == DrawReason.TurnStart)
            {
                RunAfterTurnBanner(Fly);
            }
            else
            {
                Fly();
            }
        }

        /// <summary>Face-down into their chip: you see that they drew, never what.</summary>
        private void HandleRivalCardDrawn(CardDrawn rivalDraw)
        {
            _table.GlideCardToPlayer(_art.Back("lemon"), _table.LemonPileWorld(), rivalDraw.PlayerId);
        }

        private void HandleTimeoutDrawn()
        {
            _fx.QueueSound(Sfx.CardDraw);
            _timeoutCardInFlight = true;
            var timeoutArt = _art.Lemon(TimeoutDefId);
            void Land()
            {
                _timeoutCardInFlight = false;
                _timeoutCardShownAt = UnityEngine.Time.time;
                _table.ShowTimeoutCard(timeoutArt);
            }
            // The table holds for the flight, so the decisions the Timeout raises open once it
            // has landed; at the top of YOUR turn it waits for ONWARD! like the draw it replaced.
            RunAfterTurnBanner(() => _fx.QueueHold(
                () => _table.GlideTimeoutToCenter(timeoutArt, _table.LemonPileWorld(), Land),
                TableView.TimeoutFlightSeconds + 0.15f, onSkipped: Land));
        }
    }
}
