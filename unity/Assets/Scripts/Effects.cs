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
        private sealed class RevealFx : Fx
        {
            public Texture2D Art;
            public string Title;
        }
        private sealed class FlyFx : Fx
        {
            public Texture2D CardBack;   // null = coin flight
            public int FromPlayerId;
            public int ToPlayerId;
            public int Amount;           // coin flights: floaters at both ends on arrival
            public int Count = 1;
        }

        private readonly RectTransform _layer;        // floaters + flights, no scrim
        private readonly RectTransform _revealRoot;   // dim scrim + card + title
        private readonly TMPro.TMP_Text _revealTitle;
        private readonly RectTransform _revealCardHost;
        private readonly System.Func<bool> _held;
        private readonly System.Func<int, Vector3?> _anchorFor;   // player id -> world
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

        public EffectsPlayer(RectTransform canvasRoot, System.Func<bool> held,
            System.Func<int, Vector3?> anchorFor)
        {
            _held = held;
            _anchorFor = anchorFor;

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

            _revealTitle = UiKit.CreateText(_revealRoot, "", 40,
                TextAnchor.MiddleCenter, Color.white);
            UiKit.Anchor((RectTransform)_revealTitle.transform,
                new Vector2(0.05f, 0.85f), new Vector2(0.95f, 0.96f));
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
            _queue.Clear();
            _busyUntil = 0f;
            EndActive();
            UiKit.Clear(_layer);
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

        private void StartBlocking(Fx fx)
        {
            switch (fx)
            {
                case RevealFx reveal:
                    StartReveal(reveal);
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
            _busyUntil = Time.time + 1.7f;
            _endActive = () => _revealRoot.gameObject.SetActive(false);
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
            var world = _anchorFor(playerId);
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
            rect.anchoredPosition = local + new Vector2(0, 8f + stackIndex * 26f);

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

    /// <summary>Rise, linger, fade, self-destruct. Optional start delay for stacks.</summary>
    public sealed class FloaterMotion : MonoBehaviour
    {
        public float Delay;
        public float RiseSpeed = 55f;
        public float FadeStart = 0.75f;
        public float Lifetime = 1.25f;

        private RectTransform _rect;
        private CanvasGroup _group;
        private float _elapsed;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.alpha = 0f;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed - Delay;
            if (t < 0f)
            {
                return;
            }
            _group.alpha = t < 0.15f
                ? t / 0.15f
                : 1f - Mathf.Clamp01((t - FadeStart) / (Lifetime - FadeStart));
            _rect.anchoredPosition += new Vector2(0, RiseSpeed * Time.deltaTime);
            if (t >= Lifetime)
            {
                Destroy(gameObject);
            }
        }
    }

    /// <summary>Arcing hop between two table anchors (stolen coins, flying cards).</summary>
    public sealed class FlightMotion : MonoBehaviour
    {
        public Vector2 From;
        public Vector2 To;
        public float Delay;
        public float Duration = 0.55f;
        public float ArcHeight = 110f;
        public System.Action OnArrive;

        private RectTransform _rect;
        private CanvasGroup _group;
        private float _elapsed;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.alpha = 0f;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = (_elapsed - Delay) / Duration;
            if (t < 0f)
            {
                return;
            }
            if (t >= 1f)
            {
                OnArrive?.Invoke();
                Destroy(gameObject);
                return;
            }
            _group.alpha = Mathf.Clamp01(t * 6f);
            float eased = t * t * (3f - 2f * t);
            var position = Vector2.Lerp(From, To, eased);
            position.y += ArcHeight * 4f * t * (1f - t);
            _rect.anchoredPosition = position;
        }
    }

    /// <summary>Small scale punch on spawn (reveal cards, toasts).</summary>
    public sealed class PopIn : MonoBehaviour
    {
        private float _t;

        private void Update()
        {
            _t += Time.deltaTime;
            float progress = Mathf.Clamp01(_t / 0.22f);
            float overshoot = 1f + 0.14f * Mathf.Sin(progress * Mathf.PI);
            transform.localScale = Vector3.one * (0.8f + 0.2f * progress) * overshoot;
            if (progress >= 1f)
            {
                transform.localScale = Vector3.one;
                Destroy(this);
            }
        }
    }
}
