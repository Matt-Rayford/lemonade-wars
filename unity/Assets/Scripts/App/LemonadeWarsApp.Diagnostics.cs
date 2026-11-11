using System.Linq;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Presentation self-healing (the soft-lock watchdog) and the [D]
    /// frozen-state console dump.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private const float WedgedEffectSeconds = 8f;
        /// <summary>A fresh modal legitimately takes a frame or two to appear.</summary>
        private const float StuckGraceSeconds = 2f;

        private float _stuckSince = -1f;
        private float _fxBusySince = -1f;
        private int _fxBlocksSeen = -1;

        /// <summary>
        /// Soft-locks are pure presentation (the game state underneath is always fine),
        /// so when one is seen it is logged and the presentation machinery is reset.
        /// </summary>
        private void TickPresentationWatchdog()
        {
            ClearWedgedEffect();
            HealStuckPresentation();
        }

        /// <summary>
        /// Measures PROGRESS, not total time: a big exchange legitimately chains theatre
        /// well past the limit. Only one effect sitting that long without a new one starting is wedged.
        /// </summary>
        private void ClearWedgedEffect()
        {
            if (_fx.IsBusy)
            {
                if (_fxBusySince < 0f || _fx.BlocksStarted != _fxBlocksSeen)
                {
                    _fxBusySince = Time.time;
                    _fxBlocksSeen = _fx.BlocksStarted;
                }
                else if (Time.time - _fxBusySince > WedgedEffectSeconds)
                {
                    Debug.LogWarning("[LW] watchdog: one effect busy >8s — clearing. " + _fx.DebugState());
                    _fx.Clear();
                    _playTheatre.Clear();
                    _playTheatre.Clear();
                    _fxBusySince = -1f;
                }
            }
            else
            {
                _fxBusySince = -1f;
            }
        }

        /// <summary>The player is owed a modal but nothing is on screen, or an overlay is open but invisible.</summary>
        private void HealStuckPresentation()
        {
            var groups = CurrentGroups();
            bool blocked = _turnBanner.IsOpen || _dice.IsBusy || _fx.IsBusy || _playTheatre.IsBusy;
            bool modalDue = groups != null && groups.IsModal && groups.ModalMoves.Count > 0;
            bool modalVisible = IsModalAnswerableOnScreen();
            bool bannerGhost = _turnBanner.IsOpen && !_turnBanner.RootVisible;
            bool overlayGhost = (_prompt.IsOpen && !_prompt.RootVisible) ||
                                (_picker.IsOpen && !_picker.RootVisible);
            bool stuck = (modalDue && !modalVisible && !blocked) || bannerGhost || overlayGhost;
            if (!stuck)
            {
                _stuckSince = -1f;
                return;
            }
            if (_stuckSince < 0f)
            {
                _stuckSince = Time.time;
                return;
            }
            if (Time.time - _stuckSince < StuckGraceSeconds)
            {
                return;
            }
            _stuckSince = -1f;
            Debug.LogWarning("[LW] watchdog: presentation stuck — healing. " +
                $"modalDue={modalDue} modalVisible={modalVisible} bannerGhost={bannerGhost} " +
                $"overlayGhost={overlayGhost} dice={_dice.IsBusy} fx=({_fx.DebugState()})");
            if (bannerGhost)
            {
                _turnBanner.Hide();
            }
            if (overlayGhost)
            {
                _prompt.Hide();
                _picker.Hide();
                _autoModalOpen = false;
            }
            _modalSignature = "";
            _modalRevision = -1;
            _renderedRevision = -1; // full re-render; MaybeShowModal runs fresh
        }

        /// <summary>
        /// Table-live surfaces answer a modal without a modal: the message-strip buttons
        /// (reactions, the Timeout fine, Rummage Sale), a held card, the aim layers and the discard browser.
        /// </summary>
        private bool IsModalAnswerableOnScreen() =>
            (_prompt.IsOpen && _prompt.RootVisible) ||
            (_picker.IsOpen && _picker.RootVisible) ||
            _table.PlayerPickActive ||
            _reaction.IsOpen ||
            _tableActionHost.childCount > 0 ||
            _table.StickyDragActive || _table.EquipTargetingActive ||
            _table.DiscardPickerOpen || _table.RummagePickActive;

        /// <summary>[D]: dumps every presentation gate to the console. Each line names a
        /// layer that can silently block the table.</summary>
        private void DumpDebugState()
        {
            var groups = CurrentGroups();
            var dump = new System.Text.StringBuilder();
            dump.AppendLine($"revision={_session.Revision} rendered={_renderedRevision} " +
                $"modalRev={_modalRevision} wasMyTurn={_wasMyTurn} autoplay={_session.HumanAutoplay}");
            dump.AppendLine($"banner: open={_turnBanner.IsOpen} visible={_turnBanner.RootVisible}");
            dump.AppendLine($"prompt: open={_prompt.IsOpen} visible={_prompt.RootVisible}   " +
                $"picker: open={_picker.IsOpen} visible={_picker.RootVisible}");
            dump.AppendLine($"dice: busy={_dice.IsBusy}   fx: {_fx.DebugState()}");
            dump.AppendLine($"groups: isModal={groups?.IsModal} modal={groups?.ModalMoves.Count} " +
                $"bar={groups?.BarMoves.Count} hand={groups?.HandMoves.Count} " +
                $"supply={groups?.SupplyMoves.Count} market={groups?.MarketMoves.Count}");
            dump.AppendLine($"moves({_session.Moves.Count}): " + string.Join(" | ",
                _session.Moves.Take(8).Select(m => _session.LabelFor(m))));
            if (View != null)
            {
                dump.AppendLine($"view: stage={View.Stage} phase={View.Phase} " +
                    $"active={View.ActivePlayer} decisions=[{string.Join(",", View.MyDecisions.Select(d => d.Kind))}] " +
                    $"awaiting=[{string.Join(",", View.AwaitingResponse)}]");
            }
            Debug.Log("[LW DUMP]\n" + dump);
        }
    }
}
