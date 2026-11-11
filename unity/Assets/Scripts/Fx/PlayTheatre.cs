using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Another player's play, staged on the table: the card slides from their bar to the
    /// showcase spot and hangs there while any response window plays out. An attack
    /// points a curved arrow at its victim, whose bar glows red; a take stages its loot
    /// beside the card; reactions slap down on top (the Responses partial). Resolved, the
    /// card slides into the victim's bar (its own player's, for a take); cancelled, it
    /// fades where it floats. Only the arrival beat, a slap and the departure are
    /// BLOCKING theatre: the wait for reactions must not be, or the viewer's own
    /// reaction modal could never open.
    /// </summary>
    public sealed partial class PlayTheatre
    {
        private enum Phase { Idle, Arriving, Holding, Departing, Fading }

        private const float ArriveSeconds = 0.3f;
        private const float ReflectCrossSeconds = 0.6f;
        private const float MinHoldSeconds = 1.0f;
        private const float DepartSeconds = 0.5f;
        private const float FadeSeconds = 0.4f;
        private const float CardWidth = 154f;
        private const float CardHeight = 216f;
        /// <summary>Height on arrival at the victim's bar: a little under the 82px bar.</summary>
        private const float EndHeight = 60f;
        private const float LootGapPx = 18f;

        private readonly RectTransform _layer;
        private readonly RectTransform _arrowHost;
        private readonly System.Func<int, Vector3?> _barWorld;
        private readonly System.Func<int, (Vector3 Center, Vector3 Rim)?> _avatarWorld;
        private readonly System.Func<int, Vector3?> _showcaseWorld;
        private readonly System.Action<int, bool> _victimGlow;
        private readonly CardPreview _preview;
        private readonly Texture2D _grabIcon;
        private readonly System.Func<int, string> _nameOf;

        private RectTransform _chip;
        private CanvasGroup _group;
        private RectTransform _badge;
        private RectTransform _loot;
        private CanvasGroup _lootGroup;
        private Vector2 _lootShowcase;
        private Vector2 _lootTo;
        private Phase _phase = Phase.Idle;
        private float _phaseStart;
        private float _holdUntil;
        private Vector2 _from;
        private Vector2 _showcase;
        private Vector2 _to;
        private int _attackerId = -1;
        private int _targetId = -1;
        private float _arriveSeconds = ArriveSeconds;
        private string _defId = "";
        private bool _resolveWhenReady;
        private bool _cancelWhenReady;
        private System.Func<Vector3?> _resolveDestination;
        private float _resolveEndScale = 1f;
        private System.Action _resolveArrived;
        private float _departEndScale;
        private bool _departFades;
        private bool _wasBusy;

        /// <summary>Fires on every busy → idle edge; the app re-renders so held modals open.</summary>
        public System.Action OnIdle;

        /// <param name="victimGlow">Turns the red attack glow on a player's bar on or off.</param>
        /// <param name="grabIcon">The hand that hovers over stolen loot; null = loot shows bare.</param>
        public PlayTheatre(RectTransform canvasRoot, System.Func<int, Vector3?> barWorld,
            System.Func<int, (Vector3 Center, Vector3 Rim)?> avatarWorld, System.Func<int, Vector3?> showcaseWorld,
            System.Action<int, bool> victimGlow, CardPreview preview, Texture2D grabIcon,
            System.Func<int, string> nameOf)
        {
            _nameOf = nameOf;
            _barWorld = barWorld;
            _avatarWorld = avatarWorld;
            _showcaseWorld = showcaseWorld;
            _victimGlow = victimGlow;
            _preview = preview;
            _grabIcon = grabIcon;
            var go = new GameObject("PlayTheatre", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            _layer = (RectTransform)go.transform;
            UiKit.Anchor(_layer, Vector2.zero, Vector2.one);
            var arrows = new GameObject("Arrows", typeof(RectTransform));
            arrows.transform.SetParent(_layer, false);
            _arrowHost = (RectTransform)arrows.transform;
            UiKit.Anchor(_arrowHost, Vector2.zero, Vector2.one);
        }

        public bool IsActive => _phase != Phase.Idle;

        /// <summary>Order the stage directly over another canvas layer (a purchase's bills must land under its card).</summary>
        public void DrawAbove(RectTransform layer) => _layer.SetSiblingIndex(layer.GetSiblingIndex() + 1);

        /// <summary>True during the arrival beat and the departure; never while waiting on reactions.</summary>
        public bool IsBusy =>
            _phase == Phase.Arriving || _phase == Phase.Departing || _phase == Phase.Fading ||
            (_phase == Phase.Holding && Time.time < _holdUntil) || IsSlapping;

        /// <summary>The staged play is this owner's card of this def (owner changes on a reflection).</summary>
        public bool Matches(int ownerId, string defId) => IsActive && ownerId == _attackerId && defId == _defId;

        /// <param name="targetId">The attack's victim; -1 for a play with no victim (a take).</param>
        /// <param name="lootArt">For takes and steals, the card changing hands: staged beside the play under a grabbing hand.</param>
        /// <param name="fromWorld">Where the card lifts from; null = the player's bar. A contested purchase lifts from its shelf slot.</param>
        public void Begin(Texture2D art, string defId, int attackerId, int targetId, Texture2D lootArt = null,
            Vector3? fromWorld = null)
        {
            Clear();
            var showcase = _showcaseWorld(attackerId);
            var attackerBar = _barWorld(attackerId);
            if (art == null || showcase == null || attackerBar == null)
            {
                return; // nothing on screen to stage from
            }
            _defId = defId;
            _attackerId = attackerId;
            _targetId = targetId;
            _from = Local(fromWorld ?? attackerBar.Value);
            _showcase = Local(showcase.Value); // level with the attacker's own bar, like a purchase
            _chip = BuildChip(art);
            _group = _chip.GetComponent<CanvasGroup>();
            PinPlayerBadge(_chip, attackerId);
            _chip.anchoredPosition = _from;
            if (lootArt != null)
            {
                _lootShowcase = _showcase + new Vector2(CardWidth + LootGapPx, 0);
                _loot = BuildLoot(lootArt);
                _loot.anchoredPosition = _from;
            }
            _arriveSeconds = ArriveSeconds;
            _phase = Phase.Arriving;
            _phaseStart = Time.time;
            _holdUntil = Time.time + MinHoldSeconds;
            _targetArrowDrawn = false;
            _wasBusy = true;
        }

        /// <summary>A Tag moved the attack; a Rubber-Glue also hands it to a new owner.</summary>
        public void Retarget(int attackerId, int targetId)
        {
            if (!IsActive)
            {
                return;
            }
            bool reflected = attackerId != _attackerId;
            _attackerId = attackerId;
            _targetId = targetId;
            _targetArrowDrawn = false;
            PinPlayerBadge(_chip, attackerId); // a reflection makes the card someone else's
            // The engine drops the card-specific pick on a redirect (the attacker re-picks
            // on resolve), so the staged loot is no longer the truth.
            DestroyLoot();
            if (reflected)
            {
                CrossToNewOwner();
                return;
            }
            RedrawArrows(force: true);
            GlowVictim();
        }

        /// <summary>The attack landed: slide into the victim once the minimum hold is over.</summary>
        public void Resolve() => _resolveWhenReady = IsActive;

        /// <summary>
        /// Resolve by sliding to a spot read at departure (a bought card's place on its stand) at the given scale, with
        /// no shrink-and-fade; the bar is the fallback. The callback fires exactly once, on every exit path.
        /// </summary>
        public void ResolveTo(System.Func<Vector3?> destinationWorld, float endScale, System.Action onArrive)
        {
            if (!IsActive)
            {
                onArrive?.Invoke();
                return;
            }
            _resolveDestination = destinationWorld;
            _resolveEndScale = endScale;
            _resolveArrived = onArrive;
            _resolveWhenReady = true;
        }

        /// <summary>The attack was cancelled: fade in place once the minimum hold is over.</summary>
        public void Cancel() => _cancelWhenReady = IsActive;

        public void Clear()
        {
            if (_chip != null)
            {
                _preview.Hide(); // the magnifier may still be showing this very card
                Object.Destroy(_chip.gameObject);
            }
            _chip = null;
            _group = null;
            _badge = null; // destroyed with the chip
            DestroyLoot();
            DestroyResponses();
            UiKit.Clear(_arrowHost);
            GlowVictim(off: true);
            _phase = Phase.Idle;
            _resolveWhenReady = false;
            _cancelWhenReady = false;
            _targetArrowDrawn = false;
            _resolveDestination = null;
            var arrived = _resolveArrived;
            _resolveArrived = null;
            arrived?.Invoke();
        }

        public void Tick()
        {
            if (IsActive)
            {
                Advance();
                TickSpread();
            }
            bool busy = IsBusy;
            if (_wasBusy && !busy)
            {
                OnIdle?.Invoke();
            }
            _wasBusy = busy;
        }

        /// <summary>
        /// "As if you played it": a reflected attack slides across the table to the reflector's side,
        /// its reaction pile fading behind it, so the card is seen to BECOME theirs.
        /// </summary>
        private void CrossToNewOwner()
        {
            var newShowcase = _showcaseWorld(_attackerId);
            if (newShowcase == null)
            {
                RedrawArrows(force: true);
                GlowVictim();
                return;
            }
            FadeResponses();
            UiKit.Clear(_arrowHost);
            GlowVictim(off: true);
            _from = _chip.anchoredPosition;
            _showcase = Local(newShowcase.Value);
            _arriveSeconds = ReflectCrossSeconds;
            _holdUntil = Time.time + ReflectCrossSeconds + MinHoldSeconds;
            EnterPhase(Phase.Arriving);
        }

        private void Advance()
        {
            float elapsedSeconds = Time.time - _phaseStart;
            switch (_phase)
            {
                case Phase.Arriving:
                    AdvanceArrival(elapsedSeconds);
                    break;
                case Phase.Holding:
                    AdvanceHold();
                    break;
                case Phase.Departing:
                    AdvanceDeparture(elapsedSeconds);
                    break;
                case Phase.Fading:
                    AdvanceFade(elapsedSeconds);
                    break;
            }
        }

        private void AdvanceArrival(float elapsedSeconds)
        {
            float eased = Ease(Mathf.Clamp01(elapsedSeconds / _arriveSeconds));
            _chip.anchoredPosition = Vector2.Lerp(_from, _showcase, eased);
            if (_loot != null)
            {
                _loot.anchoredPosition = Vector2.Lerp(_from, _lootShowcase, eased);
            }
            RedrawArrows(force: false);
            if (elapsedSeconds >= _arriveSeconds)
            {
                EnterPhase(Phase.Holding);
            }
        }

        private void AdvanceHold()
        {
            RedrawArrows(force: false);
            GlowVictim(); // bars are rebuilt on every render; keep the glow with the victim
            if (Time.time >= _holdUntil)
            {
                if (_cancelWhenReady)
                {
                    FadeResponses();
                    EnterPhase(Phase.Fading);
                }
                else if (_resolveWhenReady && FadeResponses())
                {
                    _holdUntil = Time.time + SlapMotion.FadeSeconds; // the pile clears first
                }
                else if (_resolveWhenReady)
                {
                    BeginDeparture();
                }
            }
        }

        /// <summary>
        /// An attack lands on its victim; a take goes home with its player; a bought card
        /// slides to its place on the stand when that is in view.
        /// </summary>
        private void BeginDeparture()
        {
            var owner = _barWorld(_attackerId);
            var target = _targetId >= 0 ? _barWorld(_targetId) : owner;
            var spot = _resolveDestination?.Invoke();
            _departFades = spot == null;
            _departEndScale = spot == null ? EndHeight / CardHeight : _resolveEndScale;
            _to = spot != null ? Local(spot.Value)
                : target == null ? _showcase : Local(target.Value);
            // The loot is the player's now: it slides into their bar.
            _lootTo = owner == null ? _lootShowcase : Local(owner.Value);
            UiKit.Clear(_arrowHost);
            GlowVictim(off: true);
            _group.blocksRaycasts = false; // no magnifier on a card that is leaving
            if (_lootGroup != null)
            {
                _lootGroup.blocksRaycasts = false;
            }
            _preview.Hide();
            EnterPhase(Phase.Departing);
        }

        private void AdvanceDeparture(float elapsedSeconds)
        {
            float progress = Mathf.Clamp01(elapsedSeconds / DepartSeconds);
            float eased = Ease(progress);
            _chip.anchoredPosition = Vector2.Lerp(_showcase, _to, eased);
            _chip.localScale = Vector3.one * Mathf.Lerp(1f, _departEndScale, eased);
            _group.alpha = !_departFades ? 1f : progress < 0.5f ? 1f : 1f - (progress - 0.5f) * 2f;
            if (_loot != null)
            {
                _loot.anchoredPosition = Vector2.Lerp(_lootShowcase, _lootTo, eased);
                _loot.localScale = _chip.localScale;
                _lootGroup.alpha = _group.alpha;
            }
            if (progress >= 1f)
            {
                Clear();
            }
        }

        private void AdvanceFade(float elapsedSeconds)
        {
            float progress = Mathf.Clamp01(elapsedSeconds / FadeSeconds);
            _group.alpha = 1f - progress;
            if (_lootGroup != null)
            {
                _lootGroup.alpha = 1f - progress;
            }
            UiKit.Clear(_arrowHost);
            GlowVictim(off: true);
            if (progress >= 1f)
            {
                Clear();
            }
        }

        private void EnterPhase(Phase phase)
        {
            _phase = phase;
            _phaseStart = Time.time;
        }

        private Vector2 Local(Vector3 world) => _layer.InverseTransformPoint(world);

        private static float Ease(float t) => t * t * (3f - 2f * t);
    }
}
