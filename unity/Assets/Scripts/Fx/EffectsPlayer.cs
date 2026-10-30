using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Event-driven presentation effects. Non-blocking money floaters release in batches;
    /// blocking theatre (attack reveals, steal flights, toasts) plays one at a time, and
    /// everything waits politely behind the dice and the turn banner. Modals in turn wait
    /// on <see cref="IsBusy"/>, so "you got attacked" plays BEFORE the response prompt.
    /// Click a reveal to skip it.
    /// </summary>
    public sealed class EffectsPlayer
    {
        private static readonly Color GainColor = new Color(0.45f, 0.95f, 0.50f);
        private static readonly Color LossColor = new Color(1f, 0.42f, 0.36f);
        private static readonly Color ToastColor = new Color(1f, 0.92f, 0.55f);

        private abstract class Fx { }
        private sealed class MoneyFx : Fx { public int PlayerId; public int Amount; }
        private sealed class SoundFx : Fx { public string Name; }
        private sealed class ToastFx : Fx { public string Text; }
        /// <summary>
        /// How long a revealed card holds centered. Playtest verdict: 1.7s read as
        /// "barely a second" for a card played on you — the title AND the art both
        /// need reading. Claims (Whiniest Baby) set their own, shorter hold.
        /// </summary>
        private const float RevealHoldSeconds = 2.0f;
        private const float ShelfCardWidth = 154f;
        private const float ShelfCardHeight = 216f;
        private const float SweepSeconds = 0.175f;
        /// <summary>Swept one at a time: the next card leaves as the last one slips under the deck.</summary>
        private const float SweepStaggerSeconds = SweepSeconds;
        /// <summary>A swept card fades through the last of its slide, as if passing under the deck.</summary>
        private const float SweepFadeOutFrom = 0.55f;
        private const float DealSeconds = 0.2f;
        /// <summary>A dealt copy outlives its landing by a few frames, until the real card has been drawn under it.</summary>
        private const float DealLingerSeconds = 0.06f;
        /// <summary>Dealt one at a time: the next card leaves the deck as the last one lands.</summary>
        private const float DealStaggerSeconds = DealSeconds;
        /// <summary>After the refresh dollar lands, this long before the shelf starts moving.</summary>
        private const float RefreshMoneyBeatSeconds = 0.25f;
        /// <summary>Height a purchased card shrinks to on arrival: a little under the 82px player bar.</summary>
        private const float PurchaseEndHeight = 60f;
        private const float BillWidth = 207f;
        private const float BillHeight = 148f;
        private const int PileAmountFontSize = 40;
        /// <summary>Black outline round the money label, in TMP font-relative units.</summary>
        private const float PileAmountOutlineWidth = 0.25f;
        /// <summary>The "-$X" sits this far right of the pile's top-right corner, and a little below it.</summary>
        private const float PileAmountGapPx = 10f;
        private const float PileAmountDropPx = 18f;
        /// <summary>The pile lands this far right of the card's showcase spot, so the card doesn't sit squarely on it.</summary>
        private const float BillPileRightShiftPx = 50f;
        /// <summary>Earnings: how long the pile sits before sliding into the bar, and the clear-table breath after.</summary>
        private const float EarningsHoldSeconds = 0.6f;
        private const float EarningsBreathSeconds = 0.5f;
        /// <summary>How long the viewer's own money sits on the table before fading on its own.</summary>
        private const float OwnMoneyHoldSeconds = 0.6f;
        /// <summary>Clear-table pause after the viewer's money has faded, before bots may act.</summary>
        private const float OwnMoneyBreathSeconds = 0.35f;
        private const float BillBaseTiltDegrees = 8f;
        /// <summary>
        /// How far a lower bill pokes out from under the one above, as a share of the
        /// bill's half-extent in the chosen direction: enough to read its denomination,
        /// never so far that it stops looking like one pile.
        /// </summary>
        private const float BillPeekMin = 0.45f;
        private const float BillPeekMax = 0.75f;
        /// <summary>Each lower bill is pushed out in its own direction; this is the scatter around an even spread.</summary>
        private const float BillDirectionJitterDegrees = 25f;
        /// <summary>A bill's turn relative to the pile's base tilt.</summary>
        private const float BillTurnDegrees = 40f;
        /// <summary>Neighbouring bills never sit this close in angle, or the pile reads as one flat stack.</summary>
        private const float BillMinTurnGapDegrees = 10f;

        private sealed class RevealFx : Fx
        {
            public Texture2D Art;
            public string Title;
        }
        /// <summary>A reveal that then FLIES somewhere: hold centered, shrink-fly to
        /// the anchor. NOT dropped by CancelPendingReveals — it announces a state
        /// change (the Whiniest Baby), not a play a modal is about to narrate.</summary>
        private sealed class ClaimFx : Fx
        {
            public Texture2D Art;
            public string Title;
            public System.Func<Vector3> Destination;
            /// <summary>Fires exactly once when the card lands (or the fx is skipped) —
            /// the moment the persistent card may appear where the flight ended.</summary>
            public System.Action OnDelivered;
        }
        private sealed class FlyFx : Fx
        {
            public Texture2D CardBack;   // null = coin flight
            public int FromPlayerId;
            public int ToPlayerId;
            public int Amount;           // coin flights: floaters at both ends on arrival
            public int Count = 1;
        }
        /// <summary>A shelf purchase: the bought card's art lifts out of its slot,
        /// shows itself mid-table, then flies to the buyer — so "someone took that
        /// card" is visible instead of the row just mutating. Like ClaimFx it announces
        /// a state change, so it is NOT dropped by CancelPendingReveals, and its
        /// OnDelivered must always fire.</summary>
        private sealed class PurchaseFx : Fx
        {
            public Texture2D Art;
            public int BuyerId;
            /// <summary>Dollars paid; 0 for free takes, which show no money.</summary>
            public int Price;
            /// <summary>Bill art by denomination, resolved when the effect starts.</summary>
            public System.Func<int, Texture2D> BillArt;
            /// <summary>
            /// False for the viewer's own buys: they just clicked the card, so only the
            /// money plays — and without holding the table up.
            /// </summary>
            public bool ShowCard = true;
            /// <summary>Resolved when the effect STARTS (layout settles between queue
            /// and start). Null result = nothing on screen to lift from: deliver
            /// instantly with no motion.</summary>
            public System.Func<Vector3?> Origin;
            /// <summary>Fires exactly once when the drift ends (or the fx is skipped /
            /// cleared) — the moment a suppressed refill slot may show its new card.</summary>
            public System.Action OnDelivered;
            /// <summary>Fires when the lifted card is on screen: its shelf stand-in may go.</summary>
            public System.Action OnStarted;
        }

        /// <summary>
        /// A player's sale-roll earnings: a pile of bills slides in from off-screen
        /// right to their showcase spot, holds, then slides into their bar. Blocking,
        /// one player at a time, with a breath between players; OnArrived is the
        /// moment their displayed cash may catch up, so it fires on every exit path.
        /// </summary>
        /// <summary>
        /// The shelf changing hands with the deck: cards being swept slide under the
        /// Black Market deck back and vanish, then each dealt card slides from the
        /// deck to its slot. A paid refresh first slaps the refresher's dollar down.
        /// Every deal's OnLanded fires exactly once, on every exit path.
        /// </summary>
        private sealed class MarketDealFx : Fx
        {
            public System.Func<Vector3?> DeckWorld;
            public List<ShelfCardFlight> Sweep = new List<ShelfCardFlight>();
            public List<ShelfCardFlight> Deals = new List<ShelfCardFlight>();
            /// <summary>Who paid for a refresh; -1 = no money shown.</summary>
            public int PayerId = -1;
            public int Price;
            public System.Func<int, Texture2D> BillArt;
            /// <summary>Fires when the flights are on screen: stand-ins on the shelf may go.</summary>
            public System.Action OnStarted;
        }

        private sealed class EarnFx : Fx
        {
            public int PlayerId;
            public int Amount;
            public System.Func<int, Texture2D> BillArt;
            public System.Action OnArrived;
        }

        private readonly RectTransform _layer;        // floaters + flights, no scrim
        private readonly RectTransform _revealRoot;   // dim scrim + card + title
        private readonly TMPro.TMP_Text _revealTitle;
        private readonly RectTransform _revealCardHost;
        private readonly System.Func<bool> _held;
        private readonly System.Func<int, Vector3?> _anchorFor;   // player id -> world
        private readonly System.Func<int, Vector3?> _cashAnchorFor;   // player id -> cash icon world
        private readonly System.Func<int, Vector3?> _showcaseWorld;    // buyer id -> where their card pauses
        private readonly Queue<Fx> _queue = new Queue<Fx>();

        private float _busyUntil;
        private System.Action _endActive;
        /// <summary>OnFinished is owed: SOMETHING was queued since the last idle
        /// announcement, so the renderer must be woken when we fully drain — even if
        /// the last item was a passive sound/floater. Missing this once left "modal
        /// due, everything idle, nothing on screen" frozen forever (the fx queue is
        /// one of the gates modals defer behind, and renders only re-run on wake).</summary>
        private bool _announceIdle;

        /// <summary>Re-render hook, like the dice: fires when the last effect finishes.</summary>
        public System.Action OnFinished;

        /// <summary>
        /// Blocking theatre still running or queued — modals and the turn banner wait
        /// on this. Passive effects (floaters, deferred sounds) do NOT count: they are
        /// the ones waiting for the theatre, not the other way round.
        /// </summary>
        public bool IsBusy
        {
            get
            {
                if (Time.time < _busyUntil || _endActive != null)
                {
                    return true;
                }
                foreach (var fx in _queue)
                {
                    if (!(fx is MoneyFx) && !(fx is SoundFx))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <param name="cashAnchorFor">A player's cash icon; money floaters rise from it. Null falls back to the bar.</param>
        /// <param name="showcaseWorld">Where a buyer's card pauses before sliding into their bar; null = over the bar itself.</param>
        public EffectsPlayer(RectTransform canvasRoot, System.Func<bool> held,
            System.Func<int, Vector3?> anchorFor, System.Func<int, Vector3?> cashAnchorFor = null,
            System.Func<int, Vector3?> showcaseWorld = null)
        {
            _held = held;
            _anchorFor = anchorFor;
            _cashAnchorFor = cashAnchorFor ?? anchorFor;
            _showcaseWorld = showcaseWorld ?? anchorFor;

            var go = new GameObject("Effects", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            _layer = (RectTransform)go.transform;
            UiKit.Anchor(_layer, Vector2.zero, Vector2.one);

            _revealRoot = UiKit.CreatePanel(canvasRoot, "AttackReveal", new Color(0, 0, 0, 0.55f));
            UiKit.Anchor(_revealRoot, Vector2.zero, Vector2.one);
            UiKit.AddClick(_revealRoot.gameObject, SkipActive);

            var cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(_revealRoot, false);
            _revealCardHost = (RectTransform)cardGo.transform;
            UiKit.Anchor(_revealCardHost, Vector2.zero, Vector2.one);

            // The title hugs the card: bottom-aligned in a band that starts just above
            // the card's top edge (card center +60, half-height 161), so the gap stays
            // the same whether the headline fits one line or wraps to two.
            _revealTitle = UiKit.CreateText(_revealRoot, "", 40,
                TextAnchor.LowerCenter, Color.white);
            var revealTitleRect = (RectTransform)_revealTitle.transform;
            revealTitleRect.anchorMin = new Vector2(0.05f, 0.5f);
            revealTitleRect.anchorMax = new Vector2(0.95f, 0.5f);
            revealTitleRect.pivot = new Vector2(0.5f, 0f);
            revealTitleRect.sizeDelta = new Vector2(0f, 110f);
            revealTitleRect.anchoredPosition = new Vector2(0f, 60f + 161f + 14f);
            _revealTitle.raycastTarget = false;
            UiKit.AddTextShadow(_revealTitle);

            _revealRoot.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ queue

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

        /// <summary>Reveal centered, then shrink-fly the card to a destination. The
        /// destination is a func because layout may not have settled at queue time.</summary>
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

        /// <summary>Showcase a purchased card out of its shelf slot, then fly it to the
        /// buyer's bar. The origin is a func because layout may not have settled at
        /// queue time; a null origin at start skips the motion but still delivers.</summary>
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

        /// <summary>Sweep cards under the Black Market deck and deal new ones to the shelf (see MarketDealFx).</summary>
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

        /// <summary>Slap a player's roll earnings on the table and slide them into their bar.</summary>
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

        /// <summary>
        /// The viewer is about to answer this play in a response modal — the modal tells
        /// the story itself, so drop any queued reveal instead of double-billing it.
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

        public void QueueToast(string text)
        {
            _announceIdle = true;
            _queue.Enqueue(new ToastFx { Text = text });
        }

        /// <summary>A sound that must wait out the theatre (e.g. the turn-start draw
        /// stays secret until ONWARD! dismisses the turn banner).</summary>
        public void QueueSound(string name)
        {
            _announceIdle = true;
            _queue.Enqueue(new SoundFx { Name = name });
        }

        /// <summary>Diagnostics: what the effects queue is doing right now.</summary>
        public string DebugState()
        {
            string queued = string.Join(",", _queue.Select(fx => fx.GetType().Name));
            return $"busy={IsBusy} blockingActive={_endActive != null} " +
                   $"busyFor={Mathf.Max(0, _busyUntil - Time.time):F1}s held={_held()} " +
                   $"queue=[{queued}]";
        }

        /// <summary>Drop everything (screen change, autopilot), including live sprites.</summary>
        public void Clear()
        {
            // The completion-callback law, queued half: effects that gate persistent UI
            // (a suppressed shelf slot, a hidden lord card) must deliver even when the
            // theatre is torn down before they start, or the suppression never lifts.
            // The ACTIVE one delivers via EndActive() below; these never started.
            foreach (var fx in _queue)
            {
                switch (fx)
                {
                    case ClaimFx claim:
                        claim.OnDelivered?.Invoke();
                        break;
                    case PurchaseFx purchase:
                        purchase.OnDelivered?.Invoke();
                        break;
                    case EarnFx earn:
                        earn.OnArrived?.Invoke();
                        break;
                    case MarketDealFx deal:
                        foreach (var card in deal.Deals)
                        {
                            card.OnLanded?.Invoke();
                        }
                        break;
                }
            }
            _queue.Clear();
            _busyUntil = 0f;
            EndActive();
            UiKit.Clear(_layer);
            // The completion-callback law: anything that ends the busy period must
            // guarantee OnFinished, or a modal deferred behind the effects (a Profit
            // Share window, a decision) never resurfaces. Next Tick announces idle.
            _announceIdle = true;
        }

        // ------------------------------------------------------------- tick

        public void Tick()
        {
            if (Time.time < _busyUntil)
            {
                return;
            }
            if (_endActive != null)
            {
                EndActive();
            }
            if (_queue.Count > 0 && !_held())
            {
                // Floaters and sounds are fire-and-forget: flush every leading one as
                // a batch (stacking floaters per player), then start at most one
                // blocking effect.
                var stacked = new Dictionary<int, int>();
                while (_queue.Count > 0 && (_queue.Peek() is MoneyFx || _queue.Peek() is SoundFx))
                {
                    var fx = _queue.Dequeue();
                    if (fx is SoundFx sound)
                    {
                        Sfx.Play(sound.Name);
                        continue;
                    }
                    var money = (MoneyFx)fx;
                    stacked.TryGetValue(money.PlayerId, out int index);
                    stacked[money.PlayerId] = index + 1;
                    SpawnMoney(money.PlayerId, money.Amount, index);
                }
                if (_queue.Count > 0)
                {
                    StartBlocking(_queue.Dequeue());
                    return;
                }
            }

            // Fully drained after doing ANY work since the last announcement: wake
            // the renderer exactly once, so deferred modals/banners open. This must
            // hold even when the tail of the queue was passive (a sound behind an
            // attack reveal) — the old early-return here is what froze tables.
            if (_announceIdle && _queue.Count == 0 && _endActive == null &&
                Time.time >= _busyUntil)
            {
                _announceIdle = false;
                OnFinished?.Invoke();
            }
        }

        /// <summary>Bumped whenever a blocking effect starts — the watchdog resets its
        /// stuck-timer on progress, so long LEGIT theatre chains never trip it.</summary>
        public int BlocksStarted { get; private set; }

        private void StartBlocking(Fx fx)
        {
            BlocksStarted++;
            switch (fx)
            {
                case RevealFx reveal:
                    StartReveal(reveal);
                    break;
                case ClaimFx claim:
                    StartClaim(claim);
                    break;
                case PurchaseFx purchase:
                    StartPurchase(purchase);
                    break;
                case EarnFx earn:
                    StartEarnings(earn);
                    break;
                case MarketDealFx deal:
                    StartMarketDeal(deal);
                    break;
                case ToastFx toast:
                    SpawnToast(toast.Text);
                    _busyUntil = Time.time + 1.0f;
                    _endActive = () => { };
                    break;
                case FlyFx fly:
                    StartFlight(fly);
                    break;
            }
        }

        /// <summary>Click on a reveal (or any blocking beat): jump to its end.</summary>
        private void SkipActive()
        {
            _busyUntil = 0f;
        }

        private void EndActive()
        {
            var end = _endActive;
            _endActive = null;
            end?.Invoke();
        }

        // ----------------------------------------------------------- reveal

        private void StartReveal(RevealFx reveal)
        {
            UiKit.Clear(_revealCardHost);
            _revealTitle.text = reveal.Title;

            const float width = 230f;
            const float height = 322f;
            var cardCenter = new Vector2(0, 60f);
            var image = UiKit.CreateCardImage(_revealCardHost, reveal.Art, width, height);
            image.raycastTarget = false;
            var frame = (RectTransform)image.transform.parent;
            frame.GetComponent<Image>().raycastTarget = false;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(width, height);
            frame.anchoredPosition = cardCenter;
            frame.gameObject.AddComponent<PopIn>();
            var shadow = frame.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);
            shadow.effectDistance = new Vector2(0, -6f);

            _revealRoot.gameObject.SetActive(true);
            _busyUntil = Time.time + RevealHoldSeconds;
            _endActive = () => _revealRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Reveal + delivery: the card holds centered long enough to read, then
        /// shrink-flies to its destination (the lord fan). The busy window covers the
        /// whole journey, so the deferred re-render — which adds the persistent card —
        /// runs right as the flight lands. Click-to-skip kills the flight too.
        /// </summary>
        private void StartClaim(ClaimFx claim)
        {
            StartReveal(new RevealFx { Art = claim.Art, Title = claim.Title });

            const float holdSeconds = 1.0f;
            const float flySeconds = 0.35f;
            const float width = 230f;
            const float height = 322f;

            var chip = (RectTransform)UiKit.CreateCardImage(_layer, claim.Art, width, height)
                .transform.parent;
            foreach (var graphic in chip.GetComponentsInChildren<UnityEngine.UI.Graphic>())
            {
                graphic.raycastTarget = false;
            }
            chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0.5f);
            chip.pivot = new Vector2(0.5f, 0.5f);
            chip.sizeDelta = new Vector2(width, height);

            bool delivered = false;
            void Deliver()
            {
                if (!delivered)
                {
                    delivered = true;
                    claim.OnDelivered?.Invoke();
                }
            }

            var motion = chip.gameObject.AddComponent<FlightMotion>();
            motion.From = new Vector2(0, 60f);    // the reveal card's resting spot
            motion.Delay = holdSeconds;
            motion.Duration = flySeconds;
            motion.ArcHeight = 40f;
            motion.EndScale = 190f / width;       // land at the fan's card size
            // The destination is resolved when the flight BEGINS, not at queue time —
            // the fan the card must join may have re-laid-out in the meantime.
            motion.OnStart = () =>
            {
                motion.To = _layer.InverseTransformPoint(claim.Destination());
                _revealRoot.gameObject.SetActive(false); // the chip takes over seamlessly
            };
            motion.OnArrive = Deliver;

            _busyUntil = Time.time + holdSeconds + flySeconds;
            _endActive = () =>
            {
                _revealRoot.gameObject.SetActive(false);
                if (chip != null)
                {
                    Object.Destroy(chip.gameObject); // skip-click: no orphaned flight
                }
                Deliver(); // skip still delivers — the card must never stay hidden
            };
        }

        // --------------------------------------------------------- purchases

        /// <summary>
        /// The bought card's art slides out of its shelf slot to a showcase spot level
        /// with the buyer's bar, holds a beat, then slides into that bar, shrinking and
        /// fading out. Blocking, so back-to-back bot purchases play one at a time; the
        /// busy window covers the whole trip, and delivery fires exactly once on EVERY
        /// exit path (leaving the showcase, skip, Clear) — a suppressed refill slot
        /// depends on it.
        /// </summary>
        private void StartPurchase(PurchaseFx purchase)
        {
            bool delivered = false;
            void Deliver()
            {
                if (!delivered)
                {
                    delivered = true;
                    purchase.OnDelivered?.Invoke();
                }
            }

            purchase.OnStarted?.Invoke();
            if (!purchase.ShowCard)
            {
                // Your own buy: the money slaps down and clears itself. It still holds
                // the table — bots act the moment the theatre is idle, and a bot's own
                // purchase landing on top of your bills read as clutter — plus a breath
                // after the fade so the next thing starts on a clear table.
                var ownFan = BuildPurchasePile(purchase, ShowcaseSpot(purchase.BuyerId));
                Deliver();
                if (ownFan == null)
                {
                    return;
                }
                ownFan.AutoFadeAfterSeconds = OwnMoneyHoldSeconds;
                _busyUntil = Time.time + BillFan.ArriveSeconds + OwnMoneyHoldSeconds +
                    BillFan.FadeSeconds + OwnMoneyBreathSeconds;
                _endActive = () =>
                {
                    if (ownFan != null)
                    {
                        Object.Destroy(ownFan.gameObject); // skip: no orphaned pile
                    }
                };
                return;
            }
            var world = purchase.Origin?.Invoke();
            if (world == null || purchase.Art == null)
            {
                Deliver(); // nothing on screen to lift from: no motion, no busy window
                return;
            }

            const float width = 154f;   // market card size: it IS the card leaving
            const float height = 216f;

            var image = UiKit.CreateCardImage(_layer, purchase.Art, width, height);
            var chip = (RectTransform)image.transform.parent;
            foreach (var graphic in chip.GetComponentsInChildren<UnityEngine.UI.Graphic>())
            {
                graphic.raycastTarget = false;
            }
            chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0.5f);
            chip.pivot = new Vector2(0.5f, 0.5f);
            chip.sizeDelta = new Vector2(width, height);
            chip.anchoredPosition = _layer.InverseTransformPoint(world.Value);
            chip.SetAsLastSibling(); // over the shelf card now shifted into this slot

            var showcase = ShowcaseSpot(purchase.BuyerId);
            // The money comes out first and the card lands on top of it: the fan is
            // built before the chip is raised, so the chip draws over the bills.
            var fan = BuildPurchasePile(purchase, showcase);
            chip.SetAsLastSibling();

            var motion = chip.gameObject.AddComponent<PurchaseJourney>();
            motion.Delay = fan != null ? BillFan.ArriveSeconds : 0f;
            motion.Showcase = showcase;
            motion.EndScale = PurchaseEndHeight / height;
            motion.Destination = () => PurchaseDestination(purchase.BuyerId, motion.Showcase);
            motion.OnLeaveShowcase = () =>
            {
                Deliver(); // the shelf is visibly empty by now
                if (fan != null)
                {
                    fan.BeginFade(); // the money is spent: it goes as the card goes
                }
            };
            motion.OnDone = () => { };

            _busyUntil = Time.time + motion.Delay + PurchaseJourney.TotalSeconds;
            _endActive = () =>
            {
                if (chip != null)
                {
                    Object.Destroy(chip.gameObject); // skip: no orphaned drift
                }
                if (fan != null)
                {
                    Object.Destroy(fan.gameObject);
                }
                Deliver(); // skip still delivers — the refill must never stay hidden
            };
        }

        private void StartMarketDeal(MarketDealFx deal)
        {
            var pending = new List<ShelfCardFlight>(deal.Deals);
            void Land(ShelfCardFlight card)
            {
                if (pending.Remove(card))
                {
                    card.OnLanded?.Invoke();
                }
            }
            void LandAll()
            {
                foreach (var card in pending.ToArray())
                {
                    Land(card);
                }
            }

            var deckWorld = deal.DeckWorld?.Invoke();
            if (deckWorld == null)
            {
                LandAll(); // no shelf on screen: nothing to animate
                return;
            }
            Vector2 deck = _layer.InverseTransformPoint(deckWorld.Value);
            var chips = new List<RectTransform>();
            float clock = 0f;

            BillFan fan = null;
            if (deal.PayerId >= 0 && deal.Price > 0)
            {
                fan = BuildPurchasePile(deal.PayerId, deal.Price, deal.BillArt, ShowcaseSpot(deal.PayerId));
                if (fan != null)
                {
                    fan.AutoFadeAfterSeconds = OwnMoneyHoldSeconds;
                    clock = BillFan.ArriveSeconds + RefreshMoneyBeatSeconds;
                }
            }

            int swept = 0;
            foreach (var card in deal.Sweep)
            {
                var art = card.Art?.Invoke();
                var slot = card.SlotWorld?.Invoke();
                if (art == null || slot == null)
                {
                    continue;
                }
                var chip = BuildShelfChip(art);
                chips.Add(chip);
                var motion = chip.gameObject.AddComponent<FlightMotion>();
                motion.From = _layer.InverseTransformPoint(slot.Value);
                motion.To = deck;
                motion.Delay = clock + swept * SweepStaggerSeconds;
                motion.Duration = SweepSeconds;
                motion.ArcHeight = 0f;
                motion.VisibleWhileWaiting = true;
                motion.FadeOutFrom = SweepFadeOutFrom;
                swept++;
            }
            if (swept > 0)
            {
                clock += SweepSeconds + SweepStaggerSeconds * (swept - 1);
            }
            deal.OnStarted?.Invoke(); // the sliding copies are up: the shelf may drop its stand-ins

            int dealt = 0;
            foreach (var card in deal.Deals)
            {
                var art = card.Art?.Invoke();
                var slot = card.SlotWorld?.Invoke();
                if (art == null || slot == null)
                {
                    Land(card); // nothing to fly: the slot just shows what it has
                    continue;
                }
                var chip = BuildShelfChip(art);
                chips.Add(chip);
                var motion = chip.gameObject.AddComponent<FlightMotion>();
                motion.From = deck;
                motion.To = _layer.InverseTransformPoint(slot.Value);
                motion.Delay = clock + dealt * DealStaggerSeconds;
                motion.Duration = DealSeconds;
                motion.ArcHeight = 0f;
                motion.LingerAfterArrivalSeconds = DealLingerSeconds;
                var landing = card;
                motion.OnArrive = () => Land(landing);
                dealt++;
            }
            if (dealt > 0)
            {
                clock += DealSeconds + DealStaggerSeconds * (dealt - 1);
            }

            _busyUntil = Time.time + clock + 0.1f;
            _endActive = () =>
            {
                foreach (var chip in chips)
                {
                    if (chip != null)
                    {
                        Object.Destroy(chip.gameObject); // skip: nothing left mid-air
                    }
                }
                if (fan != null)
                {
                    Object.Destroy(fan.gameObject);
                }
                LandAll(); // skip still lands — a hidden slot must never stay hidden
            };
        }

        /// <summary>A shelf-sized card chip (the market cell's own size), click-transparent.</summary>
        private RectTransform BuildShelfChip(Texture2D art)
        {
            var image = UiKit.CreateCardImage(_layer, art, ShelfCardWidth, ShelfCardHeight);
            var chip = (RectTransform)image.transform.parent;
            foreach (var graphic in chip.GetComponentsInChildren<UnityEngine.UI.Graphic>())
            {
                graphic.raycastTarget = false;
            }
            chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0.5f);
            chip.pivot = new Vector2(0.5f, 0.5f);
            chip.sizeDelta = new Vector2(ShelfCardWidth, ShelfCardHeight);
            return chip;
        }

        /// <summary>The bills that paid for a purchase, out of the buyer's bar, growing and fading in.</summary>
        private BillFan BuildPurchasePile(PurchaseFx purchase, Vector2 showcase) =>
            BuildPurchasePile(purchase.BuyerId, purchase.Price, purchase.BillArt, showcase);

        private BillFan BuildPurchasePile(int buyerId, int price, System.Func<int, Texture2D> billArt, Vector2 showcase)
        {
            var barWorld = _anchorFor(buyerId);
            if (barWorld == null)
            {
                return null;
            }
            var fan = BuildBillPile(price, billArt, $"-${price}", LossColor);
            if (fan == null)
            {
                return null;
            }
            fan.From = _layer.InverseTransformPoint(barWorld.Value);
            fan.To = showcase + new Vector2(BillPileRightShiftPx, 0);
            fan.StartScale = PurchaseEndHeight / BillHeight;
            ((RectTransform)fan.transform).anchoredPosition = fan.From;
            return fan;
        }

        /// <summary>
        /// Sale-roll earnings: the pile slides in from off-screen right, level with the
        /// player's showcase spot, holds a beat, then slides into their bar shrinking
        /// and fading — the purchase choreography with the money arriving instead of
        /// leaving. Blocking through a breath after it lands, so players take turns.
        /// </summary>
        private void StartEarnings(EarnFx earn)
        {
            bool arrived = false;
            void Arrive()
            {
                if (!arrived)
                {
                    arrived = true;
                    earn.OnArrived?.Invoke();
                }
            }

            var barWorld = _anchorFor(earn.PlayerId);
            var fan = barWorld == null
                ? null
                : BuildBillPile(earn.Amount, earn.BillArt, $"+${earn.Amount}", GainColor);
            if (fan == null)
            {
                Arrive(); // nothing to show: the cash just updates
                return;
            }
            var showcase = ShowcaseSpot(earn.PlayerId) + new Vector2(BillPileRightShiftPx, 0);
            fan.GrowAndFadeIn = false;
            fan.From = new Vector2(_layer.rect.xMax + BillWidth, showcase.y);
            fan.To = showcase;
            fan.AutoFadeAfterSeconds = EarningsHoldSeconds;
            fan.Destination = _layer.InverseTransformPoint(barWorld.Value);
            fan.EndScale = PurchaseEndHeight / BillHeight;
            fan.OnArrive = Arrive;
            ((RectTransform)fan.transform).anchoredPosition = fan.From;

            _busyUntil = Time.time + BillFan.ArriveSeconds + EarningsHoldSeconds +
                BillFan.DepartSeconds + EarningsBreathSeconds;
            _endActive = () =>
            {
                if (fan != null)
                {
                    Object.Destroy(fan.gameObject); // skip: no orphaned pile
                }
                Arrive(); // skip still lands the money — the cash must never stay behind
            };
        }

        /// <summary>
        /// A pile of bills for an amount, fanned so every one shows — the largest on top,
        /// each lower bill shifted and turned enough to peek out from under it — with
        /// the amount written beside it. Positioned by the caller. Null for nothing.
        /// </summary>
        private BillFan BuildBillPile(int amount, System.Func<int, Texture2D> billArt, string label, Color labelColor)
        {
            var bills = BillBreakdown.Bills(amount);
            if (bills.Count == 0 || billArt == null)
            {
                return null;
            }
            var root = new GameObject("BillFan", typeof(RectTransform));
            root.transform.SetParent(_layer, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(BillWidth, BillHeight);
            var placements = PilePlacements(bills.Count);
            // Smallest first, so the largest denomination ends up drawn on top.
            for (int i = bills.Count - 1; i >= 0; i--)
            {
                var art = billArt(bills[i]);
                if (art == null)
                {
                    continue;
                }
                var billGo = new GameObject($"Bill{bills[i]}", typeof(RectTransform), typeof(RawImage));
                billGo.transform.SetParent(rect, false);
                var bill = (RectTransform)billGo.transform;
                bill.anchorMin = bill.anchorMax = new Vector2(0.5f, 0.5f);
                bill.sizeDelta = new Vector2(BillWidth, BillHeight);
                bill.anchoredPosition = new Vector2(placements[i].x, placements[i].y);
                bill.localEulerAngles = new Vector3(0, 0, placements[i].z);
                var image = billGo.GetComponent<RawImage>();
                image.texture = art;
                image.raycastTarget = false;
            }
            AddPileAmountLabel(rect, label, labelColor, placements);
            return root.AddComponent<BillFan>();
        }

        /// <summary>
        /// Big "-$X" / "+$X" hanging off the top bill's top-right corner, a child of the
        /// pile so it moves and fades with the money: the pile shows the bills, this
        /// says the total at a glance. The top bill, not the pile's bounding box: a
        /// lower bill turned hard can push that box's corner into empty table.
        /// </summary>
        private static void AddPileAmountLabel(RectTransform pile, string text, Color color, Vector3[] placements)
        {
            var corner = TopRightCorner(placements[0]);
            var label = UiKit.CreateText(pile, text, PileAmountFontSize, TextAnchor.MiddleLeft, color);
            label.raycastTarget = false;
            label.fontStyle = TMPro.FontStyles.Bold;
            UiKit.AddTextOutline(label, PileAmountOutlineWidth, Color.black); // big type over busy art
            var rect = (RectTransform)label.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(200f, 72f); // generous: an outline must never be clipped by its own box
            rect.anchoredPosition = corner + new Vector2(PileAmountGapPx, -PileAmountDropPx);
        }

        /// <summary>Where a bill's top-right corner ends up after its turn.</summary>
        private static Vector2 TopRightCorner(Vector3 placement)
        {
            float radians = placement.z * Mathf.Deg2Rad;
            float x = BillWidth / 2f;
            float y = BillHeight / 2f;
            return new Vector2(
                placement.x + x * Mathf.Cos(radians) - y * Mathf.Sin(radians),
                placement.y + x * Mathf.Sin(radians) + y * Mathf.Cos(radians));
        }

        /// <summary>
        /// A fresh, valid pile for this many bills: offset (x, y) and turn (z), top bill
        /// first. The top bill sits at the centre with the pile's base tilt; each lower
        /// bill is pushed out in its own direction — spread evenly round the pile, then
        /// scattered — far enough that its edge and a corner number show, and turned by
        /// a random amount that never lines up with the bill above it.
        /// </summary>
        private static Vector3[] PilePlacements(int count)
        {
            var placements = new Vector3[count];
            float baseTilt = Random.Range(-BillBaseTiltDegrees, BillBaseTiltDegrees);
            placements[0] = new Vector3(0f, 0f, baseTilt);
            float firstDirection = Random.Range(0f, 360f);
            float spread = 360f / Mathf.Max(2, count - 1);
            for (int i = 1; i < count; i++)
            {
                float direction = firstDirection + (i - 1) * spread +
                    Random.Range(-BillDirectionJitterDegrees, BillDirectionJitterDegrees);
                var unit = new Vector2(Mathf.Cos(direction * Mathf.Deg2Rad), Mathf.Sin(direction * Mathf.Deg2Rad));
                // A rectangle's reach in a direction is the sum of its half-sides' projections.
                float reach = BillWidth / 2f * Mathf.Abs(unit.x) + BillHeight / 2f * Mathf.Abs(unit.y);
                var offset = unit * (reach * Random.Range(BillPeekMin, BillPeekMax));
                float turn = baseTilt + Random.Range(-BillTurnDegrees, BillTurnDegrees);
                float above = placements[i - 1].z;
                if (Mathf.Abs(Mathf.DeltaAngle(turn, above)) < BillMinTurnGapDegrees)
                {
                    turn = above + Mathf.Sign(turn - above + 0.01f) * BillMinTurnGapDegrees;
                }
                placements[i] = new Vector3(offset.x, offset.y, turn);
            }
            return placements;
        }

        /// <summary>Where a buyer's card pauses to be read, in layer space; the layer centre when the table has no spot for them.</summary>
        private Vector2 ShowcaseSpot(int buyerId)
        {
            var world = _showcaseWorld(buyerId) ?? _anchorFor(buyerId);
            return world == null ? Vector2.zero : (Vector2)_layer.InverseTransformPoint(world.Value);
        }

        /// <summary>The buyer's bar in layer space; a bar that is off-screen keeps the card at the showcase.</summary>
        private Vector2 PurchaseDestination(int buyerId, Vector2 fallback)
        {
            var world = _anchorFor(buyerId);
            return world == null ? fallback : (Vector2)_layer.InverseTransformPoint(world.Value);
        }

        // ---------------------------------------------------------- flights

        private void StartFlight(FlyFx fly)
        {
            var fromWorld = _anchorFor(fly.FromPlayerId);
            var toWorld = _anchorFor(fly.ToPlayerId);
            if (fromWorld == null || toWorld == null)
            {
                return;
            }
            Vector2 from = _layer.InverseTransformPoint(fromWorld.Value);
            Vector2 to = _layer.InverseTransformPoint(toWorld.Value);

            const float duration = 0.55f;
            const float stagger = 0.15f;
            for (int i = 0; i < fly.Count; i++)
            {
                var chip = fly.CardBack != null ? BuildCardChip(fly.CardBack) : BuildCoinChip();
                var motion = chip.gameObject.AddComponent<FlightMotion>();
                motion.From = from;
                motion.To = to;
                motion.Delay = i * stagger;
                motion.Duration = duration;
                if (fly.CardBack == null && i == fly.Count - 1)
                {
                    int thief = fly.ToPlayerId;
                    int victim = fly.FromPlayerId;
                    int amount = fly.Amount;
                    motion.OnArrive = () =>
                    {
                        SpawnMoney(thief, amount, 0);
                        SpawnMoney(victim, -amount, 0);
                    };
                }
            }
            _busyUntil = Time.time + duration + stagger * (fly.Count - 1) + 0.1f;
            _endActive = () => { };
        }

        private RectTransform BuildCoinChip()
        {
            var go = new GameObject("Coin", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_layer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(46, 46);
            var image = go.GetComponent<Image>();
            image.sprite = UiSprites.Circle;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = UiKit.ButtonColor;
            image.raycastTarget = false;
            var label = UiKit.CreateText(go.transform, "$", 26,
                TextAnchor.MiddleCenter, UiKit.ButtonTextColor);
            label.raycastTarget = false;
            UiKit.Anchor((RectTransform)label.transform, Vector2.zero, Vector2.one);
            return rect;
        }

        private RectTransform BuildCardChip(Texture2D back)
        {
            var image = UiKit.CreateCardImage(_layer, back, 66, 92);
            image.raycastTarget = false;
            var frame = (RectTransform)image.transform.parent;
            frame.GetComponent<Image>().raycastTarget = false;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(66, 92);
            return frame;
        }

        // ----------------------------------------------------------- spawns

        private void SpawnMoney(int playerId, int amount, int stackIndex)
        {
            // From the coin on the bar, not the bar's middle: the number reads as
            // the cash figure changing, and a loss no longer appears over the hand count.
            var world = _cashAnchorFor(playerId) ?? _anchorFor(playerId);
            if (world == null)
            {
                return;
            }
            var text = UiKit.CreateText(_layer,
                (amount > 0 ? "+$" : "-$") + Mathf.Abs(amount), 26,
                TextAnchor.MiddleCenter, amount > 0 ? GainColor : LossColor, body: true);
            text.raycastTarget = false;
            text.fontStyle = TMPro.FontStyles.Bold;
            UiKit.AddTextShadow(text);

            var rect = (RectTransform)text.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(220, 40);
            Vector2 local = _layer.InverseTransformPoint(world.Value);
            rect.anchoredPosition = local + new Vector2(0, stackIndex * 26f);

            var motion = text.gameObject.AddComponent<FloaterMotion>();
            motion.Delay = stackIndex * 0.12f;
        }

        private void SpawnToast(string content)
        {
            var text = UiKit.CreateText(_layer, content, 44, TextAnchor.MiddleCenter, ToastColor);
            text.raycastTarget = false;
            UiKit.AddTextShadow(text, 1.3f);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1400, 70);
            rect.anchoredPosition = new Vector2(0, 140f);
            text.gameObject.AddComponent<PopIn>();

            var motion = text.gameObject.AddComponent<FloaterMotion>();
            motion.RiseSpeed = 18f;
            motion.FadeStart = 0.9f;
            motion.Lifetime = 1.4f;
        }
    }
}
