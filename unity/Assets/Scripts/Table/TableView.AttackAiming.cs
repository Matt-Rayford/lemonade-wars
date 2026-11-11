using System.Collections.Generic;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    // Aiming a hand card: armed by clicking the card or dragging it out of the hand
    // band, it glows the hovered target (a player bar, a rival's equipped card, or a
    // shelf card for Connections) and trails an arrow from the card to the cursor.
    public sealed partial class TableView
    {
        public System.Action<int, int> OnAttackPick;         // hand id, victim player id
        public System.Action<int, int> OnMarketPick;         // hand id, market index
        private static readonly Color AttackGlowColor = new Color(1f, 0.30f, 0.22f, 0.95f);
        private static readonly Color AttackArrowColor = new Color(0.95f, 0.27f, 0.21f);
        // Reactions (Tag redirect, Rubber Glue reflect) use the attack gesture, so they
        // aim in lemonade-yellow to read as something else.
        private static readonly Color ReactionGlowColor = new Color(1f, 0.86f, 0.22f, 0.95f);
        private static readonly Color ReactionArrowColor = new Color(0.98f, 0.83f, 0.10f);
        private static readonly Color MarketGlowColor = new Color(0.36f, 0.72f, 1f, 0.95f);
        private static readonly Color MarketArrowColor = new Color(0.30f, 0.68f, 1f);

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
        /// <summary>Hovered player id, or market index when aiming at the shelf; -1 = none.</summary>
        private int _attackHover = -1;
        private bool _attackDragMode;    // true: release fires; false: second click fires
        private bool _attackDragAborted; // Esc mid-drag: stay disarmed until release
        /// <summary>The armed card's frame; a different one after a render means the fan was rebuilt under the aim.</summary>
        private RectTransform _attackFrame;
        /// <summary>A re-render destroyed the DragRelay that began this held-mouse aim, so the Tick fires the release.</summary>
        private bool _attackDragRelayLost;
        private Vector2 _attackArrowFrom;
        private Vector2 _attackArrowTo;
        // That's Not Fair and Finders Keepers also aim straight at a rival's equipped
        // cards: each tucked card's visible strip on the viewed board is a target.
        public System.Func<int, ISet<int>> EquipTargetsFor; // hand id -> equipped ids
        public System.Action<int, int> OnEquipPick;         // hand id, equipped id
        private ISet<int> _attackEquipValid;
        private int _equipHover = -1;    // equipped instance id, -1 = none

        /// <summary>
        /// An invisible full-screen layer that owns the pointer and hit-tests the targets
        /// itself; the table stays live underneath.
        /// </summary>
        private void BuildAttackTargeting()
        {
            _attackRoot = UiKit.CreatePanel(_canvasRoot, "AttackTargeting", new Color(0, 0, 0, 0));
            UiKit.Anchor(_attackRoot, Vector2.zero, Vector2.one);
            // Click mode: a click on a glowing target fires, anywhere else disarms.
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
            _attackHover = -1;
            _equipHover = -1;
            _attackEquipValid = target == AimTarget.PlayerBars
                ? EquipTargetsFor?.Invoke(cardInstanceId)
                : null;
            _aimTarget = target;
            InvalidateAttackArrow();
            bool reaction = AimIsReaction?.Invoke() == true;
            _aimGlowColor = target == AimTarget.MarketCards ? MarketGlowColor
                : reaction ? ReactionGlowColor : AttackGlowColor;
            _aimArrowColor = target == AimTarget.MarketCards ? MarketArrowColor
                : reaction ? ReactionArrowColor : AttackArrowColor;
            _preview.SetDragging(true); // no magnify pop-ups while aiming

            HoldArmedCardRaised();
            ShowAttackLayerOnTop();
        }

        /// <summary>The layer eats the hover events that would otherwise drop the armed card back into the fan.</summary>
        private void HoldArmedCardRaised()
        {
            _attackFrame = null;
            foreach (var entry in _handFrames)
            {
                if (entry.Id == _attackCardId && entry.Frame != null)
                {
                    entry.Frame.SetAsLastSibling();
                    entry.Motion.TargetY = HandRaisedY;
                    _attackFrame = entry.Frame;
                    break;
                }
            }
        }

        /// <summary>The next Tick redraws the arrow whatever the pointer does.</summary>
        private void InvalidateAttackArrow()
        {
            _attackArrowFrom = _attackArrowTo = new Vector2(float.NaN, float.NaN);
        }

        private void ShowAttackLayerOnTop()
        {
            _attackRoot.SetAsLastSibling(); // over the HUD strip, which is built after the table
            _attackRoot.gameObject.SetActive(true);
        }

        /// <summary>
        /// Re-binds an in-progress aim to the just-rebuilt hand, bars and shelf, ending it
        /// only when it has truly gone stale. Must run at the end of Render, once
        /// _handFrames, _playerRows, _equipCells and _marketCells are repopulated.
        /// </summary>
        private void ReacquireAttackTargeting(PlayerView view)
        {
            if (_attackCardId < 0)
            {
                return;
            }
            // A closed window takes the card's moves with it even when the card stays in hand.
            bool inHand = IsArmedCardInHand(view);
            var valid = !inHand ? null
                : _aimTarget == AimTarget.MarketCards
                    ? MarketTargetsFor?.Invoke(_attackCardId)
                    : AttackTargetsFor?.Invoke(_attackCardId);
            var equipValid = inHand && _aimTarget == AimTarget.PlayerBars
                ? EquipTargetsFor?.Invoke(_attackCardId)
                : null;
            if ((valid == null || valid.Count == 0) &&
                (equipValid == null || equipValid.Count == 0))
            {
                EndAttackTargeting();
                return;
            }
            _attackValid = valid ?? new HashSet<int>();
            _attackEquipValid = equipValid;
            DropAimHoversNoLongerValid();
            // The glow objects are new: the next Tick would see an unchanged hover and leave them dark.
            ApplyAimGlows();

            var previousFrame = _attackFrame;
            HoldArmedCardRaised();
            if (_attackDragMode && _attackFrame != previousFrame)
            {
                _attackDragRelayLost = true;
            }
            InvalidateAttackArrow();
            ShowAttackLayerOnTop();
        }

        private bool IsArmedCardInHand(PlayerView view)
        {
            for (int i = 0; i < view.Hand.Count; i++)
            {
                if (view.Hand[i].InstanceId == _attackCardId)
                {
                    return true;
                }
            }
            return false;
        }

        private void DropAimHoversNoLongerValid()
        {
            if (_attackHover >= 0 && !_attackValid.Contains(_attackHover))
            {
                _attackHover = -1;
            }
            if (_equipHover >= 0 &&
                (_attackEquipValid == null || !_attackEquipValid.Contains(_equipHover)))
            {
                _equipHover = -1;
            }
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

        /// <summary>Lights exactly the hovered target (bar, equipped strip or market cell) in the aim's colour.</summary>
        private void ApplyAimGlows()
        {
            if (_aimTarget == AimTarget.MarketCards)
            {
                // Each cell's own buy-glows, tinted to the aiming colour.
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

        /// <summary>Call every frame: drives the target glow and the arrow.</summary>
        public void TickAttackTargeting(Vector2 screenPosition)
        {
            if (_attackCardId < 0)
            {
                return;
            }

            // Equipped strips are precise targets and outrank the broad bar; the regions never overlap.
            int equipHover = AimedEquipStripUnder(screenPosition);
            int hover = -1;
            if (_aimTarget == AimTarget.MarketCards)
            {
                hover = AimedMarketCellUnder(screenPosition);
            }
            else if (equipHover < 0)
            {
                hover = ValidPlayerRowUnder(screenPosition, _attackValid);
            }
            if (hover != _attackHover || equipHover != _equipHover)
            {
                _attackHover = hover;
                _equipHover = equipHover;
                ApplyAimGlows();
            }

            // After the hover update, so this lands on the target the lost relay would have picked.
            if (_attackDragMode && _attackDragRelayLost && !Input.GetMouseButton(0))
            {
                FinishAttackTargeting();
                return;
            }

            RedrawAttackArrowIfMoved(screenPosition);
        }

        private int AimedEquipStripUnder(Vector2 screenPosition)
        {
            if (_aimTarget == AimTarget.PlayerBars && _attackEquipValid != null)
            {
                foreach (var (id, strip, _) in _equipCells)
                {
                    if (strip != null && _attackEquipValid.Contains(id) &&
                        RectTransformUtility.RectangleContainsScreenPoint(strip, screenPosition))
                    {
                        return id;
                    }
                }
            }
            return -1;
        }

        private int AimedMarketCellUnder(Vector2 screenPosition)
        {
            foreach (var (index, cell, _, _) in _marketCells)
            {
                if (cell != null && _attackValid.Contains(index) &&
                    RectTransformUtility.RectangleContainsScreenPoint(cell, screenPosition))
                {
                    return index;
                }
            }
            return -1;
        }

        /// <returns>The player id of the first valid bar under the pointer, or -1.</returns>
        private int ValidPlayerRowUnder(Vector2 screenPosition, ISet<int> validPlayerIds)
        {
            foreach (var (playerId, row, _) in _playerRows)
            {
                if (row != null && validPlayerIds.Contains(playerId) &&
                    RectTransformUtility.RectangleContainsScreenPoint(row, screenPosition))
                {
                    return playerId;
                }
            }
            return -1;
        }

        /// <summary>From the armed card's top edge to the cursor.</summary>
        private void RedrawAttackArrowIfMoved(Vector2 screenPosition)
        {
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
            // The arrow is rebuilt from scratch, so only on real movement.
            if ((from - _attackArrowFrom).sqrMagnitude < 4f &&
                (to - _attackArrowTo).sqrMagnitude < 4f)
            {
                return;
            }
            _attackArrowFrom = from;
            _attackArrowTo = to;
            UiKit.Clear(_attackArrowHost);
            UiKit.DrawArrow(_attackArrowHost, from, to, _aimArrowColor);
        }

        /// <summary>Fires on the hovered target, or just disarms when aimed at nothing.</summary>
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
            LowerArmedCard();
            SwitchOffAimGlows();
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

        private void LowerArmedCard()
        {
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
        }

        private void SwitchOffAimGlows()
        {
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
        }

        /// <summary>
        /// Red glow on one player's bar for someone else's staged attack. Rows are rebuilt
        /// on every render, so callers re-assert it while it matters.
        /// </summary>
        public void SetVictimGlow(int playerId, bool on)
        {
            foreach (var (id, _, glow) in _playerRows)
            {
                if (id == playerId && glow != null)
                {
                    if (on)
                    {
                        glow.GetComponent<Image>().color = AttackGlowColor;
                    }
                    glow.SetActive(on);
                }
            }
        }
    }
}
