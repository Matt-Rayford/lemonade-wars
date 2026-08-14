using System.Collections.Generic;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Attack targeting: aim a hand attack card at a player bar, armed by clicking
    // the card or by dragging it out of the hand band. Red glow on the hovered bar,
    // red dashed arrow from the card to the bar (or the cursor while searching).
    public sealed partial class TableView
    {
        public System.Action<int, int> OnAttackPick;         // hand id, victim player id
        public System.Action<int, int> OnMarketPick;         // hand id, market index
        private static readonly Color AttackGlowColor = new Color(1f, 0.30f, 0.22f, 0.95f);
        private static readonly Color AttackArrowColor = new Color(0.95f, 0.27f, 0.21f);
        // Reactions (Tag redirect, Rubber Glue reflect) aim in lemonade-yellow: same
        // gesture as an attack, visibly NOT one.
        private static readonly Color ReactionGlowColor = new Color(1f, 0.86f, 0.22f, 0.95f);
        private static readonly Color ReactionArrowColor = new Color(0.98f, 0.83f, 0.10f);
        // Connections reaches into the market instead: blue, aimed at the shelf.
        private static readonly Color MarketGlowColor = new Color(0.36f, 0.72f, 1f, 0.95f);
        private static readonly Color MarketArrowColor = new Color(0.30f, 0.68f, 1f);

        /// <summary>What an armed hand card is currently aiming at.</summary>
        private enum AimTarget
        {
            PlayerBars,
            MarketCards,
        }

        private AimTarget _aimTarget = AimTarget.PlayerBars;
        /// <summary>Set by the app: true while the current aim is a window reaction.</summary>
        public System.Func<bool> AimIsReaction;
        private Color _aimGlowColor = AttackGlowColor;
        private Color _aimArrowColor = AttackArrowColor;
        private RectTransform _attackRoot;
        private RectTransform _attackArrowHost;
        private ISet<int> _attackValid;
        private int _attackCardId = -1;
        private int _attackHover = -1;   // player id, -1 = none
        private bool _attackDragMode;    // true: release fires; false: second click fires
        private bool _attackDragAborted; // Esc mid-drag: stay disarmed until release
        /// <summary>The armed card's frame at the moment it was armed. A re-render can
        /// rebuild the fan under an in-progress aim, which destroys the DragRelay that
        /// owns the gesture — comparing identity after a render tells us the release
        /// event will never arrive (see _attackDragRelayLost).</summary>
        private RectTransform _attackFrame;
        /// <summary>Drag-mode aim whose originating DragRelay was destroyed by a
        /// re-render: nothing will deliver the pointer-up, so the Tick fires it.</summary>
        private bool _attackDragRelayLost;
        private Vector2 _attackArrowFrom;
        private Vector2 _attackArrowTo;
        // Direct aim at an opponent's EQUIPPED cards (That's Not Fair, Finders
        // Keepers): the visible peek strip of each tucked card on the viewed board is
        // a target of its own, beside the player bars.
        public System.Func<int, ISet<int>> EquipTargetsFor; // hand id -> equipped ids
        public System.Action<int, int> OnEquipPick;         // hand id, equipped id
        private ISet<int> _attackEquipValid;
        private int _equipHover = -1;    // equipped instance id, -1 = none

        /// <summary>
        /// Invisible full-screen layer for aiming an attack at a player bar. Like equip
        /// targeting it owns the pointer and hit-tests the bars manually, but the table
        /// stays live underneath — the armed card just holds its raised pose in the hand.
        /// </summary>
        private void BuildAttackTargeting()
        {
            _attackRoot = UiKit.CreatePanel(Root, "AttackTargeting", new Color(0, 0, 0, 0));
            UiKit.Anchor(_attackRoot, Vector2.zero, Vector2.one);
            // Click mode: a click on a glowing bar fires, anywhere else disarms.
            UiKit.AddClick(_attackRoot.gameObject, FinishAttackTargeting);

            var arrowGo = new GameObject("Arrow", typeof(RectTransform));
            arrowGo.transform.SetParent(_attackRoot, false);
            _attackArrowHost = (RectTransform)arrowGo.transform;
            UiKit.Anchor(_attackArrowHost, Vector2.zero, Vector2.one);

            _attackRoot.gameObject.SetActive(false);
            BuildPlayerPick();
        }

        private void BeginAttackTargeting(int cardInstanceId, ISet<int> validTargets, bool dragMode,
            AimTarget target = AimTarget.PlayerBars)
        {
            if (_attackCardId >= 0)
            {
                return;
            }
            _attackCardId = cardInstanceId;
            _attackValid = validTargets;
            _attackDragMode = dragMode;
            _attackDragRelayLost = false;
            _attackFrame = null;
            _attackHover = -1;
            _equipHover = -1;
            // Bar aiming may ALSO hit equipped cards directly when the card names them.
            _attackEquipValid = target == AimTarget.PlayerBars
                ? EquipTargetsFor?.Invoke(cardInstanceId)
                : null;
            _aimTarget = target;
            _attackArrowFrom = _attackArrowTo = new Vector2(float.NaN, float.NaN);
            bool reaction = AimIsReaction?.Invoke() == true;
            _aimGlowColor = target == AimTarget.MarketCards ? MarketGlowColor
                : reaction ? ReactionGlowColor : AttackGlowColor;
            _aimArrowColor = target == AimTarget.MarketCards ? MarketArrowColor
                : reaction ? ReactionArrowColor : AttackArrowColor;
            _preview.SetDragging(true); // no magnify pop-ups while aiming

            // Hold the armed card raised and on top: the intercept layer eats the hover
            // events that would otherwise drop it back into the fan.
            foreach (var entry in _handFrames)
            {
                if (entry.Id == cardInstanceId && entry.Frame != null)
                {
                    entry.Frame.SetAsLastSibling();
                    entry.Motion.TargetY = 12f; // the fan's raised pose
                    _attackFrame = entry.Frame;
                    break;
                }
            }
            _attackRoot.gameObject.SetActive(true);
        }

        /// <summary>
        /// Re-bind an in-progress aim to the just-rebuilt hand / bars / shelf, or tear
        /// it down when the aim has genuinely gone stale. Called at the very end of
        /// Render, after every list it reads (_handFrames, _playerRows, _equipCells,
        /// _marketCells) has been repopulated.
        ///
        /// Aiming used to be cancelled at the TOP of Render, which meant any
        /// revision-driven re-render — bot pacing ticks, another player's pass, a
        /// keepalive — silently disarmed the player's red arrow a second or two after
        /// they drew it. The aiming state is plain data, so it survives the rebuild;
        /// only the VISUALS have to be re-acquired.
        /// </summary>
        private void ReacquireAttackTargeting(PlayerView view)
        {
            if (_attackCardId < 0)
            {
                return;
            }
            // 1. The armed card must still be in hand (it may have been played,
            //    stolen or discarded by whatever caused this render).
            bool inHand = false;
            for (int i = 0; i < view.Hand.Count; i++)
            {
                if (view.Hand[i].InstanceId == _attackCardId)
                {
                    inHand = true;
                    break;
                }
            }
            // 2. …and it must still have somewhere legal to point (a closed window
            //    takes the moves with it even when the card stays in hand).
            var valid = !inHand ? null
                : _aimTarget == AimTarget.MarketCards
                    ? MarketTargetsFor?.Invoke(_attackCardId)
                    : AttackTargetsFor?.Invoke(_attackCardId);
            // Bar aiming may also hit equipped cards directly, exactly as at arming.
            var equipValid = inHand && _aimTarget == AimTarget.PlayerBars
                ? EquipTargetsFor?.Invoke(_attackCardId)
                : null;
            if ((valid == null || valid.Count == 0) &&
                (equipValid == null || equipValid.Count == 0))
            {
                EndAttackTargeting(); // the ONLY teardown here: the aim is truly stale
                return;
            }
            // Target ids can shift under us (a victim left, the shelf refreshed).
            _attackValid = valid ?? new HashSet<int>();
            _attackEquipValid = equipValid;
            // Keep the hover only while it is still legal. The glow objects are NEW,
            // so re-apply the highlight now: the next Tick would see an unchanged
            // hover id and skip the work, leaving the new bar dark.
            if (_attackHover >= 0 && !_attackValid.Contains(_attackHover))
            {
                _attackHover = -1;
            }
            if (_equipHover >= 0 &&
                (_attackEquipValid == null || !_attackEquipValid.Contains(_equipHover)))
            {
                _equipHover = -1;
            }
            ApplyAimGlows();

            // Re-raise the armed card. RenderHand either rebuilt the fan (new frame,
            // sitting at rest) or short-circuited on its signature (same frame, still
            // raised — this is then a no-op).
            var previousFrame = _attackFrame;
            _attackFrame = null;
            foreach (var entry in _handFrames)
            {
                if (entry.Id == _attackCardId && entry.Frame != null)
                {
                    entry.Frame.SetAsLastSibling();
                    entry.Motion.TargetY = 12f; // the fan's raised pose, as at arming
                    _attackFrame = entry.Frame;
                    break;
                }
            }
            // A rebuilt fan destroyed the DragRelay that began a held-mouse aim, so no
            // pointer-up will ever reach it; the Tick fires on release instead.
            if (_attackDragMode && _attackFrame != previousFrame)
            {
                _attackDragRelayLost = true;
            }
            // Force the next Tick to redraw the arrow from the (possibly new) frame.
            _attackArrowFrom = _attackArrowTo = new Vector2(float.NaN, float.NaN);
            _attackRoot.gameObject.SetActive(true);
        }

        /// <summary>Re-arm targeting for a hand card (BACK from a follow-up picker).</summary>
        public void RestartAttackTargeting(int cardInstanceId)
        {
            var targets = AttackTargetsFor?.Invoke(cardInstanceId);
            if (targets != null && targets.Count > 0)
            {
                BeginAttackTargeting(cardInstanceId, targets, dragMode: false);
            }
        }

        /// <summary>
        /// Light exactly the currently hovered target (bar, equipped strip or market
        /// cell) in the aim's colour. Split out of the Tick because a re-render hands
        /// the aim brand-new glow objects that must be re-lit without a hover CHANGE
        /// to trigger it (see ReacquireAttackTargeting).
        /// </summary>
        private void ApplyAimGlows()
        {
            if (_aimTarget == AimTarget.MarketCards)
            {
                // Reuse each cell's own buy-glow, tinted to the aiming colour.
                foreach (var (index, _, outer, inner) in _marketCells)
                {
                    foreach (var glow in new[] { outer, inner })
                    {
                        if (glow != null)
                        {
                            glow.GetComponent<Image>().color = _aimGlowColor;
                            glow.SetActive(index == _attackHover);
                        }
                    }
                }
                return;
            }
            foreach (var (playerId, _, glow) in _playerRows)
            {
                if (glow != null)
                {
                    glow.GetComponent<Image>().color = _aimGlowColor;
                    glow.SetActive(playerId == _attackHover);
                }
            }
            foreach (var (id, _, glow) in _equipCells)
            {
                if (glow != null)
                {
                    glow.GetComponent<Image>().color = _aimGlowColor;
                    glow.SetActive(id == _equipHover);
                }
            }
        }

        /// <summary>Called every frame by the app; drives the bar glow + the red arrow.</summary>
        public void TickAttackTargeting(Vector2 screenPosition)
        {
            if (_attackCardId < 0)
            {
                return;
            }

            // The viewed board's equipped cards outrank the bars: their strips are
            // precise targets, the bar is the broad one — regions never overlap.
            int equipHover = -1;
            if (_aimTarget == AimTarget.PlayerBars && _attackEquipValid != null)
            {
                foreach (var (id, strip, _) in _equipCells)
                {
                    if (strip != null && _attackEquipValid.Contains(id) &&
                        RectTransformUtility.RectangleContainsScreenPoint(strip, screenPosition))
                    {
                        equipHover = id;
                        break;
                    }
                }
            }
            int hover = -1;
            if (_aimTarget == AimTarget.MarketCards)
            {
                foreach (var (index, cell, _, _) in _marketCells)
                {
                    if (cell != null && _attackValid.Contains(index) &&
                        RectTransformUtility.RectangleContainsScreenPoint(cell, screenPosition))
                    {
                        hover = index;
                        break;
                    }
                }
            }
            else if (equipHover < 0)
            {
                foreach (var (playerId, row, _) in _playerRows)
                {
                    if (row != null && _attackValid.Contains(playerId) &&
                        RectTransformUtility.RectangleContainsScreenPoint(row, screenPosition))
                    {
                        hover = playerId;
                        break;
                    }
                }
            }
            if (hover != _attackHover || equipHover != _equipHover)
            {
                _attackHover = hover;
                _equipHover = equipHover;
                ApplyAimGlows();
            }

            // Held-mouse aim whose DragRelay a re-render destroyed: its Ended callback
            // died with the old card object, so nothing else can fire on release.
            // Hover is up to date at this point, so this lands on the same target the
            // relay would have picked.
            if (_attackDragMode && _attackDragRelayLost && !Input.GetMouseButton(0))
            {
                FinishAttackTargeting();
                return;
            }

            // Arrow: from the armed card's top edge, trailing the cursor.
            var from = _attackArrowFrom;
            foreach (var entry in _handFrames)
            {
                if (entry.Id == _attackCardId && entry.Frame != null)
                {
                    var world = entry.Frame.TransformPoint(
                        new Vector3(0, entry.Frame.rect.yMax, 0));
                    from = _attackRoot.InverseTransformPoint(world);
                    break;
                }
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _attackRoot, screenPosition, null, out var to);
            // Redraw only on real movement — the arrow is rebuilt from scratch.
            if ((from - _attackArrowFrom).sqrMagnitude < 4f &&
                (to - _attackArrowTo).sqrMagnitude < 4f)
            {
                return;
            }
            _attackArrowFrom = from;
            _attackArrowTo = to;
            UiKit.Clear(_attackArrowHost);
            UiKit.DrawDashedArrow(_attackArrowHost, from, to, _aimArrowColor);
        }

        /// <summary>Fire on the hovered bar/card, or just disarm when aimed at nothing.</summary>
        private void FinishAttackTargeting()
        {
            int cardId = _attackCardId;
            int target = _attackHover;
            int equip = _equipHover;
            var kind = _aimTarget;
            EndAttackTargeting();
            if (cardId < 0)
            {
                return;
            }
            if (equip >= 0)
            {
                OnEquipPick?.Invoke(cardId, equip);
                return;
            }
            if (target < 0)
            {
                return;
            }
            if (kind == AimTarget.MarketCards)
            {
                OnMarketPick?.Invoke(cardId, target);
            }
            else
            {
                OnAttackPick?.Invoke(cardId, target);
            }
        }

        private void EndAttackTargeting()
        {
            if (_attackCardId < 0)
            {
                return;
            }
            for (int i = 0; i < _handFrames.Count; i++)
            {
                var entry = _handFrames[i];
                if (entry.Id == _attackCardId && entry.Frame != null)
                {
                    entry.Frame.SetSiblingIndex(i);
                    entry.Motion.TargetY = entry.RestY;
                    break;
                }
            }
            foreach (var (_, _, glow) in _playerRows)
            {
                if (glow != null)
                {
                    glow.SetActive(false);
                }
            }
            foreach (var (_, _, outer, inner) in _marketCells)
            {
                outer?.SetActive(false);
                inner?.SetActive(false);
            }
            foreach (var (_, _, glow) in _equipCells)
            {
                glow?.SetActive(false);
            }
            _attackCardId = -1;
            _attackHover = -1;
            _equipHover = -1;
            _attackValid = null;
            _attackEquipValid = null;
            _attackFrame = null;
            _attackDragRelayLost = false;
            _aimTarget = AimTarget.PlayerBars;
            UiKit.Clear(_attackArrowHost);
            _attackRoot.gameObject.SetActive(false);
            _preview.SetDragging(false);
        }
    }
}
