using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Revision-gated rendering of the table, and the handover into the viewer's turn.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private void RenderIfChanged()
        {
            if (View == null)
            {
                return;
            }
            // A rebuild would destroy the drag source under the cursor. The revision stays
            // unconsumed, so the render fires the frame the drag ends.
            if (_table != null && _table.HasLiveShelfDrag)
            {
                return;
            }
            int revision = _session.Revision;
            if (revision == _renderedRevision)
            {
                return;
            }
            _renderedRevision = revision;

            SnapshotMarketBaseline();

            var groups = CurrentGroups();
            DismissModalForDissolvedChoice(groups);

            // The reaction panel must not outlive the stack item it was answering.
            if (_reaction.IsOpen && !ReactionWindowDue(groups))
            {
                _reaction.Hide();
            }

            RenderTableSurfaces(groups);
            MaybeAnnounceTurn();
            EndPlayerPickForMovedDecision(groups);
            // Decisions wait behind the theatre, and behind a turn handover the banner has yet
            // to announce: a turn-start choice must not open over the previous roll's payout.
            if (TheatreHoldsModals() || TurnHandoverPending())
            {
                return;
            }
            MaybeShowModal(groups, revision);
        }

        /// <summary>
        /// The engine auto-passes players and resolves stacks underneath an open modal.
        /// Window prompts have no Cancel, so a stale one would soft-lock the player.
        /// </summary>
        private void DismissModalForDissolvedChoice(MoveGroups groups)
        {
            if (_autoModalOpen &&
                (_prompt.IsOpen || _picker.IsOpen || HasTableDecisionOverlay) &&
                (groups == null || !groups.IsModal || groups.ModalMoves.Count == 0))
            {
                _prompt.Hide();
                _picker.Hide();
                // Silent: a stale grid/aim would submit dead moves, and its cancel callback
                // would reopen the dissolved flow.
                _table.DismissModalOverlays();
                _autoModalOpen = false;
                _modalSignature = "";
            }
        }

        private void RenderTableSurfaces(MoveGroups groups)
        {
            try
            {
                RenderBanner();
                // A tumbling die withholds every affordance: its roll may still open a response
                // window. The die's OnFinished forces the re-render that hands them back.
                var actionable = _dice.IsBusy ? null : groups;
                _table.Render(View, _db, actionable);
                _table.SetLog(_actionLog);
                RenderActionBar(actionable);
                RenderTurnActions(actionable);
            }
            catch (System.Exception e)
            {
                // A render throw must not silently leave a stale action bar or half-drawn table.
                Debug.LogException(e);
                Debug.LogError($"render error: {e.GetType().Name} — see the exception above");
            }
        }

        /// <summary>Otherwise the aiming arrow lingers over a decision that is already resolved.</summary>
        private void EndPlayerPickForMovedDecision(MoveGroups groups)
        {
            if (_table.PlayerPickActive)
            {
                var decision = View.MyDecisions.FirstOrDefault();
                if (groups == null || !groups.IsModal ||
                    decision == null || decision.Kind != DecisionKind.AbilityVictim)
                {
                    _table.EndPlayerPick();
                }
            }
        }

        private void MaybeAnnounceTurn()
        {
            bool myTurn = IsMyTurn();
            // The turn edge stays unconsumed: the die, effects and payout force a re-render
            // that lands back here once they finish.
            if (TurnHandoverPending())
            {
                return;
            }
            bool becameMyTurn = myTurn && !_wasMyTurn;
            _wasMyTurn = myTurn;
            if (!becameMyTurn || _session.HumanAutoplay || _turnBanner.IsOpen)
            {
                return;
            }
            // Leftovers from the previous turn yield to the banner; its dismiss forces the
            // re-render that reopens whatever is still relevant.
            _prompt.Hide();
            _picker.Hide();
            _reaction.Hide();
            // Your turn starts at your own board, so ONWARD! lands on a table you can act on.
            if (_table.ViewedBoardPlayer >= 0)
            {
                _table.ViewedBoardPlayer = -1;
                _renderedRevision = -1;
            }
            Sfx.Play(Sfx.YourTurn); // played, not queued: it announces the banner itself
            _turnBanner.Show(TurnSubtitle());
        }

        /// <summary>
        /// The view says it is your turn, but the previous player's theatre, roll or payout
        /// is still playing. The strip and the Your Turn banner both wait on exactly this.
        /// </summary>
        private bool TurnHandoverPending() =>
            IsMyTurn() && !_wasMyTurn &&
            (TheatreHoldsModals() || View.PendingRollValue != null || EarningsStillShowing());

        /// <summary>A modal is held back while theatre plays; the reaction strip waits for the same beat.</summary>
        private bool TheatreHoldsModals() =>
            _turnBanner.IsOpen || _dice.IsBusy || _fx.IsBusy || _playTheatre.IsBusy;

        private bool HasTableDecisionOverlay =>
            _table.DiscardPickerOpen || _table.EquipTargetingActive || _table.StickyDragActive;

        private bool IsMyTurn()
        {
            if (View.Stage == GameStage.InitialBuys)
            {
                return View.CurrentInitialBuyer == View.ViewerId;
            }
            return (View.Stage == GameStage.Playing || View.Stage == GameStage.FinalRound)
                && View.ActivePlayer == View.ViewerId;
        }

        private string TurnSubtitle()
        {
            if (View.Stage == GameStage.InitialBuys)
            {
                return "Setup draft — pick out your stands";
            }
            if (View.Stage == GameStage.FinalRound)
            {
                return "Final round — make it count!";
            }
            return $"{ActionsLabel(View.ActionsRemaining)} — the market awaits";
        }

        private static string ActionsLabel(int count) => count == 1 ? "1 action" : $"{count} actions";
    }
}
