using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Another player's play, staged on the table. An attack slides from their bar to
    /// the showcase spot, a curved arrow points it at the victim whose bar glows red,
    /// and it hangs there while any response window plays out; a take (Finders
    /// Keepers, Reduce and Reuse, Reverse Engineer) stages its loot beside the card
    /// under a grabbing hand; reactions thrown at it slap down on top (see the
    /// Responses partial). On resolution the card slides into the victim's bar — or
    /// back into the player's own, for a take — shrinking and fading; cancelled, it
    /// fades where it floats. Only the arrival beat, a slap and the departure count
    /// as blocking theatre — the wait for reactions must not, or the viewer's own
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
        private const float ArrowRefreshThresholdSq = 4f;
        private const float ArrowEdgeGapPx = 10f;
        private const float BadgeDiameterPx = 41f;
        /// <summary>The badge's centre sits this far inside the card's top-left corner: overlapping the edge, not hiding it.</summary>
        private const float BadgeInsetPx = 10f;
        private const float LootGapPx = 18f;
        private const float GrabIconSizePx = 75f;
        /// <summary>The hand's centre above the loot's top edge: high enough that only the thumb tip overlaps the card.</summary>
        private const float GrabIconLiftPx = 10f;
        /// <summary>How far the arrow's handles reach, as a share of its span: a visible sweep, not a loop.</summary>
        private const float ArrowBulge = 0.5f;
        private static readonly Color ArrowColor = new Color(0.95f, 0.27f, 0.21f);

        private readonly RectTransform _layer;
        private readonly RectTransform _arrowHost;
        private readonly System.Func<int, Vector3?> _barWorld;
        private readonly System.Func<int, Vector3?> _barEdgeWorld;
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
        private bool _wasBusy;
        private Vector2 _drawnTargetEnd;
        private bool _targetArrowDrawn;
        private int _glowingTarget = -1;

        /// <summary>Fires on every busy → idle edge; the app re-renders so held modals open.</summary>
        public System.Action OnIdle;

        /// <param name="victimGlow">Turns the red attack glow on a player's bar on or off.</param>
        /// <param name="preview">The table's magnifier, so the staged card reads on hover / Alt like any other.</param>
        /// <param name="grabIcon">The hand that hovers over stolen loot; null = loot shows bare.</param>
        /// <param name="nameOf">A player's display name, for the avatar pinned to the card they played.</param>
        public PlayTheatre(RectTransform canvasRoot, System.Func<int, Vector3?> barWorld,
            System.Func<int, Vector3?> barEdgeWorld, System.Func<int, Vector3?> showcaseWorld,
            System.Action<int, bool> victimGlow, CardPreview preview, Texture2D grabIcon,
            System.Func<int, string> nameOf)
        {
            _nameOf = nameOf;
            _barWorld = barWorld;
            _barEdgeWorld = barEdgeWorld;
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

        /// <summary>True during the arrival beat and the departure; never while waiting on reactions.</summary>
        public bool IsBusy =>
            _phase == Phase.Arriving || _phase == Phase.Departing || _phase == Phase.Fading ||
            (_phase == Phase.Holding && Time.time < _holdUntil) || Slapping;

        /// <summary>The staged play is this owner's card of this def (owner changes on a reflection).</summary>
        public bool Matches(int ownerId, string defId) => IsActive && ownerId == _attackerId && defId == _defId;

        /// <param name="targetId">The attack's victim; -1 for a play with no victim (a take).</param>
        /// <param name="lootArt">For takes and steals, the card changing hands: staged beside the play under a grabbing hand.</param>
        public void Begin(Texture2D art, string defId, int attackerId, int targetId, Texture2D lootArt = null)
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
            _from = Local(attackerBar.Value);
            _showcase = Local(showcase.Value); // level with the attacker's own bar, like a purchase
            _chip = BuildChip(art);
            _group = _chip.GetComponent<CanvasGroup>(); // the builder also serves the response pile
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
            // The engine drops the card-specific pick on a redirect; the attacker
            // re-picks on resolve, so the staged loot is no longer the truth.
            DestroyLoot();
            if (reflected)
            {
                CrossToNewOwner();
                return;
            }
            RedrawArrows(force: true);
            GlowVictim();
        }

        /// <summary>
        /// "As if you played it": a reflected attack slides across the table to the
        /// reflector's side, its reaction pile fading behind it, so the card wearing
        /// their badge is seen to BECOME theirs rather than just change hats.
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

        /// <summary>The attack landed: slide into the victim once the minimum hold is over.</summary>
        public void Resolve() => _resolveWhenReady = IsActive;

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

        private void Advance()
        {
            float t = (Time.time - _phaseStart);
            switch (_phase)
            {
                case Phase.Arriving:
                {
                    float eased = Ease(Mathf.Clamp01(t / _arriveSeconds));
                    _chip.anchoredPosition = Vector2.Lerp(_from, _showcase, eased);
                    if (_loot != null)
                    {
                        _loot.anchoredPosition = Vector2.Lerp(_from, _lootShowcase, eased);
                    }
                    RedrawArrows(force: false);
                    if (t >= _arriveSeconds)
                    {
                        EnterPhase(Phase.Holding);
                    }
                    break;
                }
                case Phase.Holding:
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
                            // An attack lands on its victim; a take goes home with its player.
                            var owner = _barWorld(_attackerId);
                            var target = _targetId >= 0 ? _barWorld(_targetId) : owner;
                            _to = target == null ? _showcase : Local(target.Value);
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
                    }
                    break;
                case Phase.Departing:
                {
                    float f = Mathf.Clamp01(t / DepartSeconds);
                    float eased = Ease(f);
                    _chip.anchoredPosition = Vector2.Lerp(_showcase, _to, eased);
                    _chip.localScale = Vector3.one * Mathf.Lerp(1f, EndHeight / CardHeight, eased);
                    _group.alpha = f < 0.5f ? 1f : 1f - (f - 0.5f) * 2f;
                    if (_loot != null)
                    {
                        _loot.anchoredPosition = Vector2.Lerp(_lootShowcase, _lootTo, eased);
                        _loot.localScale = _chip.localScale;
                        _lootGroup.alpha = _group.alpha;
                    }
                    if (f >= 1f)
                    {
                        Clear();
                    }
                    break;
                }
                case Phase.Fading:
                {
                    float f = Mathf.Clamp01(t / FadeSeconds);
                    _group.alpha = 1f - f;
                    if (_lootGroup != null)
                    {
                        _lootGroup.alpha = 1f - f;
                    }
                    UiKit.Clear(_arrowHost);
                    GlowVictim(off: true);
                    if (f >= 1f)
                    {
                        Clear();
                    }
                    break;
                }
            }
        }

        private void EnterPhase(Phase phase)
        {
            _phase = phase;
            _phaseStart = Time.time;
        }

        /// <summary>
        /// The arrow re-draws only when its endpoint moved (bars re-lay out as the
        /// game goes), never every frame — each dash is a GameObject. It leaves the
        /// card through the edge facing the victim and bows outward, away from the
        /// player column, so it reads as pointing around to them rather than through.
        /// </summary>
        private void RedrawArrows(bool force)
        {
            var targetEdge = _targetId >= 0 ? _barEdgeWorld(_targetId) : null;
            if (targetEdge == null)
            {
                return; // a take points at nobody
            }
            Vector2 targetEnd = Local(targetEdge.Value);
            bool arrowDue = _phase == Phase.Holding;
            if (!force && arrowDue == _targetArrowDrawn &&
                (targetEnd - _drawnTargetEnd).sqrMagnitude < ArrowRefreshThresholdSq)
            {
                return;
            }
            _drawnTargetEnd = targetEnd;
            _targetArrowDrawn = arrowDue;
            UiKit.Clear(_arrowHost);
            if (!arrowDue)
            {
                return;
            }
            // Leave straight up (or down) out of the card's edge facing the victim, then
            // sweep round to arrive at their bar side-on: the arrow never crosses the art.
            Vector2 card = _chip.anchoredPosition;
            float up = targetEnd.y >= card.y ? 1f : -1f;
            Vector2 exit = card + new Vector2(0, up * (CardHeight / 2f + ArrowEdgeGapPx));
            float reach = (targetEnd - exit).magnitude * ArrowBulge;
            Vector2 leave = exit + new Vector2(0, up * reach);
            Vector2 approach = targetEnd + new Vector2(reach, 0);
            UiKit.DrawCurvedArrow(_arrowHost, exit, leave, approach, targetEnd, ArrowColor);
        }

        /// <summary>Red attack glow on the victim's bar while the card is staged; off on every exit path.</summary>
        private void GlowVictim(bool off = false)
        {
            int wanted = off ? -1 : _targetId;
            if (_glowingTarget >= 0 && _glowingTarget != wanted)
            {
                _victimGlow(_glowingTarget, false);
            }
            if (wanted >= 0)
            {
                _victimGlow(wanted, true);
            }
            _glowingTarget = wanted;
        }

        private RectTransform BuildChip(Texture2D art)
        {
            var image = UiKit.CreateCardImage(_layer, art, CardWidth, CardHeight);
            var chip = (RectTransform)image.transform.parent;
            foreach (var graphic in chip.GetComponentsInChildren<Graphic>())
            {
                graphic.raycastTarget = false;
            }
            // The art itself catches the pointer, so the card can be read at full size
            // while it is staged — attacks carry text people need to see.
            image.raycastTarget = true;
            _preview.Attach(image.gameObject, art);
            chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0.5f);
            chip.pivot = new Vector2(0.5f, 0.5f);
            chip.sizeDelta = new Vector2(CardWidth, CardHeight);
            var group = chip.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = true;
            return chip;
        }

        /// <summary>
        /// The stolen card, readable on hover like the attack, with the grabbing hand
        /// drawn over its top edge — mostly above the card, thumb tip reaching onto it.
        /// </summary>
        private RectTransform BuildLoot(Texture2D lootArt)
        {
            RectTransform hand = null;
            if (_grabIcon != null)
            {
                var handGo = new GameObject("GrabbingHand", typeof(RectTransform), typeof(RawImage));
                handGo.transform.SetParent(_layer, false);
                hand = (RectTransform)handGo.transform;
                hand.anchorMin = hand.anchorMax = new Vector2(0.5f, 0.5f);
                hand.pivot = new Vector2(0.5f, 0.5f);
                hand.sizeDelta = new Vector2(GrabIconSizePx, GrabIconSizePx);
                var handImage = handGo.GetComponent<RawImage>();
                handImage.texture = _grabIcon;
                handImage.raycastTarget = false;
            }
            var image = UiKit.CreateCardImage(_layer, lootArt, CardWidth, CardHeight);
            var chip = (RectTransform)image.transform.parent;
            foreach (var graphic in chip.GetComponentsInChildren<Graphic>())
            {
                graphic.raycastTarget = false;
            }
            image.raycastTarget = true;
            _preview.Attach(image.gameObject, lootArt);
            chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0.5f);
            chip.pivot = new Vector2(0.5f, 0.5f);
            chip.sizeDelta = new Vector2(CardWidth, CardHeight);
            _lootGroup = chip.gameObject.AddComponent<CanvasGroup>();
            _lootGroup.blocksRaycasts = true;
            if (hand != null)
            {
                // Child of the chip so it moves, scales and fades with it; last in the
                // hierarchy so it draws over the card.
                hand.SetParent(chip, false);
                hand.SetAsLastSibling();
                hand.anchoredPosition = new Vector2(0, CardHeight / 2f + GrabIconLiftPx);
            }
            return chip;
        }

        /// <summary>
        /// The player's avatar on the card's top-right corner, so a staged card says who
        /// played it at a glance.
        /// </summary>
        private void PinPlayerBadge(RectTransform chip, int playerId, bool remember = true)
        {
            if (remember && _badge != null)
            {
                Object.Destroy(_badge.gameObject); // the staged card changed hands
            }
            var badge = TableView.CreatePlayerBadge(chip, _nameOf(playerId), BadgeDiameterPx);
            badge.anchorMin = badge.anchorMax = new Vector2(0f, 1f);
            badge.pivot = new Vector2(0.5f, 0.5f);
            badge.anchoredPosition = new Vector2(BadgeInsetPx, -BadgeInsetPx);
            if (remember)
            {
                _badge = badge;
            }
        }

        private void DestroyLoot()
        {
            if (_loot != null)
            {
                Object.Destroy(_loot.gameObject);
            }
            _loot = null;
            _lootGroup = null;
        }

        private Vector2 Local(Vector3 world) => _layer.InverseTransformPoint(world);

        private static float Ease(float t) => t * t * (3f - 2f * t);
    }
}
