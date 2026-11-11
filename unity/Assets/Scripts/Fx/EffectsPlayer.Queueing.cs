using System.Collections.Generic;
using UnityEngine;

namespace LemonadeWars.Unity
{
    public sealed partial class EffectsPlayer
    {
        public void QueueMoney(int playerId, int amount)
        {
            if (amount != 0)
            {
                _announceIdle = true;
                _queue.Enqueue(new MoneyFx { PlayerId = playerId, Amount = amount });
            }
        }

        public void QueueMoneySteal(int fromPlayerId, int toPlayerId, int amount)
        {
            _announceIdle = true;
            _queue.Enqueue(new FlyFx
            {
                FromPlayerId = fromPlayerId,
                ToPlayerId = toPlayerId,
                Amount = amount,
            });
        }

        public void QueueCardFly(Texture2D cardBack, int fromPlayerId, int toPlayerId, int count)
        {
            _announceIdle = true;
            _queue.Enqueue(new FlyFx
            {
                CardBack = cardBack,
                FromPlayerId = fromPlayerId,
                ToPlayerId = toPlayerId,
                Count = Mathf.Clamp(count, 1, 3),
            });
        }

        public void QueueReveal(Texture2D art, string title)
        {
            _announceIdle = true;
            _queue.Enqueue(new RevealFx { Art = art, Title = title });
        }

        public void QueueClaim(Texture2D art, string title, System.Func<Vector3> destination,
            System.Action onDelivered = null)
        {
            _announceIdle = true;
            _queue.Enqueue(new ClaimFx
            {
                Art = art,
                Title = title,
                Destination = destination,
                OnDelivered = onDelivered,
            });
        }

        public void QueuePurchase(Texture2D art, int buyerId, int price, System.Func<int, Texture2D> billArt,
            System.Func<Vector3?> origin, System.Action onDelivered = null, bool showCard = true,
            System.Action onStarted = null)
        {
            _announceIdle = true;
            _queue.Enqueue(new PurchaseFx
            {
                Art = art,
                BuyerId = buyerId,
                Price = price,
                BillArt = billArt,
                ShowCard = showCard,
                Origin = origin,
                OnDelivered = onDelivered,
                OnStarted = onStarted,
            });
        }

        public void QueueMarketDeal(System.Func<Vector3?> deckWorld, List<ShelfCardFlight> sweep,
            List<ShelfCardFlight> deals, int payerId = -1, int price = 0,
            System.Func<int, Texture2D> billArt = null, System.Action onStarted = null)
        {
            _announceIdle = true;
            _queue.Enqueue(new MarketDealFx
            {
                DeckWorld = deckWorld,
                Sweep = sweep,
                Deals = deals,
                PayerId = payerId,
                Price = price,
                BillArt = billArt,
                OnStarted = onStarted,
            });
        }

        public void QueueEarnings(int playerId, int amount, System.Func<int, Texture2D> billArt,
            System.Action onArrived)
        {
            _announceIdle = true;
            _queue.Enqueue(new EarnFx
            {
                PlayerId = playerId,
                Amount = amount,
                BillArt = billArt,
                OnArrived = onArrived,
            });
        }

        public void QueueToast(string text)
        {
            _announceIdle = true;
            _queue.Enqueue(new ToastFx { Text = text });
        }

        /// <summary>A sound that must wait out the theatre: the turn-start draw stays secret until ONWARD!.</summary>
        public void QueueSound(string name)
        {
            _announceIdle = true;
            _queue.Enqueue(new SoundFx { Name = name });
        }

        /// <summary>Run something when the queue reaches it, behind the banner and any theatre ahead, without holding the table.</summary>
        public void QueueCallback(System.Action run)
        {
            _announceIdle = true;
            _queue.Enqueue(new CallbackFx { Run = run });
        }

        /// <summary>
        /// Hold the table for a beat the caller animates. <paramref name="onSkipped"/> fires
        /// instead of <paramref name="start"/> if the queue is torn down first: whatever
        /// start would have revealed must still appear.
        /// </summary>
        public void QueueHold(System.Action start, float seconds, System.Action onSkipped = null)
        {
            _announceIdle = true;
            _queue.Enqueue(new HoldFx { Start = start, Seconds = seconds, OnSkipped = onSkipped });
        }

        /// <summary>
        /// The viewer is about to answer this play in a response modal, which tells the
        /// story itself: drop any queued reveal instead of double-billing it.
        /// </summary>
        public void CancelPendingReveals()
        {
            if (_queue.Count == 0)
            {
                return;
            }
            var kept = new List<Fx>();
            foreach (var fx in _queue)
            {
                if (!(fx is RevealFx))
                {
                    kept.Add(fx);
                }
            }
            _queue.Clear();
            foreach (var fx in kept)
            {
                _announceIdle = true;
                _queue.Enqueue(fx);
            }
        }
    }
}
