using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Event-driven presentation effects. Passive effects (money floaters, deferred sounds
    /// and callbacks) release in batches; blocking theatre (reveals, purchases, flights,
    /// toasts) plays one at a time, and everything waits behind the dice and the turn
    /// banner. Modals wait on <see cref="IsBusy"/>, so "you got attacked" plays BEFORE the
    /// response prompt. Click a blocking effect to skip it.
    /// </summary>
    public sealed partial class EffectsPlayer
    {
        private static readonly Color GainColor = new Color(0.45f, 0.95f, 0.50f);
        private static readonly Color LossColor = new Color(1f, 0.42f, 0.36f);

        private const float RevealTitleGapPx = 14f;

        /// <summary>Floaters and flights, no scrim: the play stage orders itself directly above it.</summary>
        public RectTransform Layer => _layer;
        private readonly RectTransform _layer;
        /// <summary>Dim scrim, card and title.</summary>
        private readonly RectTransform _revealRoot;
        private readonly TMPro.TMP_Text _revealTitle;
        private readonly RectTransform _revealCardHost;
        private readonly System.Func<bool> _held;
        private readonly System.Func<int, Vector3?> _barWorldOf;
        private readonly System.Func<int, Vector3?> _cashIconWorldOf;
        private readonly System.Func<int, Vector3?> _showcaseWorldOf;
        private readonly Queue<Fx> _queue = new Queue<Fx>();

        private float _busyUntil;
        private System.Action _endActive;
        /// <summary>
        /// Something was queued since the last idle announcement, so OnFinished is owed once
        /// we drain, even if the tail was passive: modals defer behind this queue.
        /// </summary>
        private bool _announceIdle;

        /// <summary>Re-render hook, like the dice: fires when the last effect finishes.</summary>
        public System.Action OnFinished;

        /// <summary>The watchdog resets its stuck-timer on this, so long legitimate theatre chains never trip it.</summary>
        public int BlocksStarted { get; private set; }

        /// <summary>
        /// Blocking theatre still running or queued. Passive effects do NOT count: they are
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
                    if (!IsPassive(fx))
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
            _barWorldOf = anchorFor;
            _cashIconWorldOf = cashAnchorFor ?? anchorFor;
            _showcaseWorldOf = showcaseWorld ?? anchorFor;

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

            _revealTitle = CreateRevealTitle(_revealRoot);

            _revealRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Bottom-aligned in a band that starts just above the card's top edge, so the gap
        /// stays the same whether the headline fits one line or wraps to two.
        /// </summary>
        private static TMPro.TMP_Text CreateRevealTitle(RectTransform revealRoot)
        {
            var title = UiKit.CreateText(revealRoot, "", 40, TextAnchor.LowerCenter, Color.white);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0.05f, 0.5f);
            titleRect.anchorMax = new Vector2(0.95f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0f);
            titleRect.sizeDelta = new Vector2(0f, 110f);
            titleRect.anchoredPosition = new Vector2(0f,
                RevealCardCenter.y + RevealCardHeight / 2f + RevealTitleGapPx);
            title.raycastTarget = false;
            UiKit.AddTextShadow(title);
            return title;
        }

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
            // Completion-callback law: an effect that gates persistent UI must deliver even
            // when dropped before it starts. The active one delivers via EndActive() below.
            foreach (var fx in _queue)
            {
                DeliverUnstarted(fx);
            }
            _queue.Clear();
            _busyUntil = 0f;
            EndActive();
            UiKit.Clear(_layer);
            // Ending the busy period owes OnFinished, or a modal deferred behind the
            // effects never resurfaces. The next Tick announces idle.
            _announceIdle = true;
        }

        private static void DeliverUnstarted(Fx fx)
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
                case CallbackFx callback:
                    callback.Run?.Invoke(); // a draw flight still releases the card it hides
                    break;
                case HoldFx hold:
                    hold.OnSkipped?.Invoke();
                    break;
                case MarketDealFx deal:
                    foreach (var card in deal.Deals)
                    {
                        card.OnLanded?.Invoke();
                    }
                    break;
            }
        }

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
                FlushLeadingPassiveEffects();
                if (_queue.Count > 0)
                {
                    StartBlocking(_queue.Dequeue());
                    return;
                }
            }

            // Wake the renderer exactly once per drain so deferred modals and banners
            // open, even when the tail of the queue was passive.
            if (_announceIdle && _queue.Count == 0 && _endActive == null &&
                Time.time >= _busyUntil)
            {
                _announceIdle = false;
                OnFinished?.Invoke();
            }
        }

        private static bool IsPassive(Fx fx) => fx is MoneyFx || fx is SoundFx || fx is CallbackFx;

        /// <summary>Money floaters released together stack per player instead of overlapping.</summary>
        private void FlushLeadingPassiveEffects()
        {
            var floatersPerPlayer = new Dictionary<int, int>();
            while (_queue.Count > 0 && IsPassive(_queue.Peek()))
            {
                var fx = _queue.Dequeue();
                if (fx is SoundFx sound)
                {
                    Sfx.Play(sound.Name);
                    continue;
                }
                if (fx is CallbackFx callback)
                {
                    callback.Run?.Invoke();
                    continue;
                }
                var money = (MoneyFx)fx;
                floatersPerPlayer.TryGetValue(money.PlayerId, out int stackIndex);
                floatersPerPlayer[money.PlayerId] = stackIndex + 1;
                SpawnMoney(money.PlayerId, money.Amount, stackIndex);
            }
        }

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
                    _busyUntil = Time.time + ToastHoldSeconds;
                    _endActive = () => { };
                    break;
                case HoldFx hold:
                    hold.Start?.Invoke();
                    _busyUntil = Time.time + hold.Seconds;
                    _endActive = () => { };
                    break;
                case FlyFx fly:
                    StartStealFlight(fly);
                    break;
            }
        }

        /// <summary>A click on the reveal scrim jumps the active effect to its end.</summary>
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

        /// <summary>
        /// Wraps a completion callback that both the natural end and a skip must fire, so
        /// it fires exactly once whichever comes first.
        /// </summary>
        private static System.Action FiringOnce(System.Action callback)
        {
            bool hasFired = false;
            return () =>
            {
                if (!hasFired)
                {
                    hasFired = true;
                    callback?.Invoke();
                }
            };
        }

        /// <summary>A click-transparent card centred on the effects layer, for the caller to place and animate.</summary>
        private RectTransform BuildFlightChip(Texture2D art, float width, float height)
        {
            var image = UiKit.CreateCardImage(_layer, art, width, height);
            var chip = (RectTransform)image.transform.parent;
            foreach (var graphic in chip.GetComponentsInChildren<UnityEngine.UI.Graphic>())
            {
                graphic.raycastTarget = false;
            }
            chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0.5f);
            chip.pivot = new Vector2(0.5f, 0.5f);
            chip.sizeDelta = new Vector2(width, height);
            return chip;
        }

        /// <summary>The effects layer's centre when the table has no spot for this player.</summary>
        private Vector2 ShowcaseSpot(int playerId)
        {
            var world = _showcaseWorldOf(playerId) ?? _barWorldOf(playerId);
            return world == null ? Vector2.zero : (Vector2)_layer.InverseTransformPoint(world.Value);
        }
    }
}
