using System.Linq;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Presentation self-healing (the soft-lock watchdog) and the [D]
    /// frozen-state console dump.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private float _stuckSince = -1f;
        private float _fxBusySince = -1f;
        private int _fxBlocksSeen = -1;

        /// <summary>
        /// Self-healing for the soft-lock family: when the player is owed a modal but
        /// nothing is on screen (or an overlay is open-but-invisible, or the effects
        /// queue has been "busy" implausibly long), log exactly what was seen and
        /// force the presentation machinery to recover. The game state is always
        /// fine underneath — these locks are pure presentation, so healing is safe.
        /// </summary>
        private void TickPresentationWatchdog()
        {
            // Effects stuck: blocking theatre should advance every ~2s on its own.
            // Measure PROGRESS, not total time — a big exchange (attack, tag,
            // retarget, steals) legitimately chains theatre well past 8s, and the old
            // total-time check cleared it mid-show. Only a single effect that sits
            // for 8s without a new one starting is actually wedged.
            if (_fx.IsBusy)
            {
                if (_fxBusySince < 0f || _fx.BlocksStarted != _fxBlocksSeen)
                {
                    _fxBusySince = Time.time;
                    _fxBlocksSeen = _fx.BlocksStarted;
                }
                else if (Time.time - _fxBusySince > 8f)
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

            var groups = CurrentGroups();
            bool blocked = _turnBanner.IsOpen || _dice.IsBusy || _fx.IsBusy || _playTheatre.IsBusy;
            bool modalDue = groups != null && groups.IsModal && groups.ModalMoves.Count > 0;
            bool modalVisible = (_prompt.IsOpen && _prompt.RootVisible) ||
                                (_picker.IsOpen && _picker.RootVisible) ||
                                _table.PlayerPickActive ||
                                _reaction.IsOpen;
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
            if (Time.time - _stuckSince < 2f)
            {
                return; // a fresh modal legitimately takes a frame or two
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

        /// <summary>[D]: dump every presentation gate to the console — the frozen-state
        /// forensics kit. Each line names a layer that can silently block the table.</summary>
        private void DumpDebugState()
        {
            var groups = CurrentGroups();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"revision={_session.Revision} rendered={_renderedRevision} " +
                $"modalRev={_modalRevision} wasMyTurn={_wasMyTurn} autoplay={_session.HumanAutoplay}");
            sb.AppendLine($"banner: open={_turnBanner.IsOpen} visible={_turnBanner.RootVisible}");
            sb.AppendLine($"prompt: open={_prompt.IsOpen} visible={_prompt.RootVisible}   " +
                $"picker: open={_picker.IsOpen} visible={_picker.RootVisible}");
            sb.AppendLine($"dice: busy={_dice.IsBusy}   fx: {_fx.DebugState()}");
            sb.AppendLine($"groups: isModal={groups?.IsModal} modal={groups?.ModalMoves.Count} " +
                $"bar={groups?.BarMoves.Count} hand={groups?.HandMoves.Count} " +
                $"supply={groups?.SupplyMoves.Count} market={groups?.MarketMoves.Count}");
            sb.AppendLine($"moves({_session.Moves.Count}): " + string.Join(" | ",
                _session.Moves.Take(8).Select(m => _session.LabelFor(m))));
            if (View != null)
            {
                sb.AppendLine($"view: stage={View.Stage} phase={View.Phase} " +
                    $"active={View.ActivePlayer} decisions=[{string.Join(",", View.MyDecisions.Select(d => d.Kind))}] " +
                    $"awaiting=[{string.Join(",", View.AwaitingResponse)}]");
            }
            Debug.Log("[LW DUMP]\n" + sb);
        }
    }
}
