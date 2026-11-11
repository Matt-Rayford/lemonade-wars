using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Decision moments: which surface answers each engine decision (blur prompt, live
    /// reaction panel, card picker, table aiming, message strip), and the ones that answer themselves.
    /// </summary>
    public sealed partial class LemonadeWarsApp
    {
        // Online, the revision stands still until the server answers: auto-submitting twice
        // for one revision would be a second move.
        private int _autoForcedPlayRevision = -1;
        private int _autoPassRevision = -1;

        /// <summary>
        /// A stack moment needs the viewer AND the table should stay live: a response window, or a
        /// retarget of the viewer's own attack. Both are decided by reading boards a blur modal hides.
        /// </summary>
        private bool ReactionWindowDue(MoveGroups groups)
        {
            if (groups == null || !groups.IsModal || groups.ModalMoves.Count == 0)
            {
                return false;
            }
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null)
            {
                // A stack play, a settled die that can still be tweaked, or money just stolen
                // from the viewer (Profit Share): all answered from the table.
                return (View.StackTop != null || View.PendingRollValue != null || View.TheftOnMe) &&
                       View.AwaitingResponse.Contains(View.ViewerId);
            }
            // A Bouncer strike-back has no stack top (its attack already left the stack), but
            // picking who to hit back needs the table just as much; so does Smear Campaign's free play.
            return decision.Kind == DecisionKind.AttackRetarget || IsFreePlayDecision();
        }

        /// <summary>A Bouncer strike-back or Smear Campaign's free play: "play a card from your hand, or skip".</summary>
        private bool IsFreePlayDecision()
        {
            var kind = View.MyDecisions.FirstOrDefault()?.Kind;
            return kind == DecisionKind.BouncerAttack || kind == DecisionKind.FreePlayOffer;
        }

        /// <summary>A plain response window or a free play: answered by a half-raised hand card or a strip button.</summary>
        private bool IsAnsweredFromHandAndStrip(MoveGroups groups) =>
            ReactionWindowDue(groups) && (View.MyDecisions.Count == 0 || IsFreePlayDecision());

        /// <summary>The viewer is the Whiniest Baby and the Timeout just came up.</summary>
        private bool TimeoutFineDue() =>
            View.MyDecisions.FirstOrDefault()?.Kind == DecisionKind.TimeoutFine;

        private bool IsPassOnlyWindow(MoveGroups groups) =>
            groups != null && View.MyDecisions.Count == 0 &&
            groups.ModalMoves.Count == 1 && groups.ModalMoves[0] is PassWindow;

        private static bool IsPassOrSkip(GameAction move) => move is SkipFreePlay || move is PassWindow;

        private void MaybeShowModal(MoveGroups groups, int revision)
        {
            if (groups == null || !groups.IsModal || groups.ModalMoves.Count == 0)
            {
                return;
            }
            // Online, harmless updates bump the revision while a modal is open. Rebuilding identical
            // buttons under the cursor eats the click, so a choice already on screen is left alone.
            string signature = ModalTitle() + "|" +
                string.Join("|", groups.ModalMoves.Select(m => _session.LabelFor(m)));
            bool surfaceOpen = _prompt.IsOpen || _picker.IsOpen || _reaction.IsOpen || HasTableDecisionOverlay;
            if (surfaceOpen && signature == _modalSignature)
            {
                _modalRevision = revision;
                return;
            }
            if (_modalRevision == revision && surfaceOpen)
            {
                return;
            }
            _modalRevision = revision;
            _modalSignature = signature;
            // The choice this grid/aim was serving changed under it. Silent: its cancel
            // callback would reopen it.
            if (HasTableDecisionOverlay)
            {
                _table.DismissModalOverlays();
            }
            if (TryAutoForcedPlay(groups, revision) || TryAutoPassEmptyWindow(groups, revision))
            {
                return;
            }
            // Answered from the table: the card lies in the middle, the strip carries the
            // Pay button (see RenderTimeoutStrip).
            if (TimeoutFineDue())
            {
                _reaction.Hide();
                _prompt.Hide();
                return;
            }
            _autoModalOpen = true;
            if (TryShowPlayerPick(groups) || TryShowStandPick(groups) || TryShowPicker())
            {
                return;
            }
            // The strip offers what the hand's half-raised cards do not (see RenderDecisionStrip).
            if (IsAnsweredFromHandAndStrip(groups))
            {
                _reaction.Hide();
                _prompt.Hide();
                return;
            }
            // Plain windows and free plays returned above: the reaction still due is a retarget.
            if (ReactionWindowDue(groups))
            {
                ShowRetargetPanel(groups);
                return;
            }
            ShowBlurPrompt(groups);
        }

        /// <summary>Re-aimed on the board when the pick is a card; otherwise from the log column, table live.</summary>
        private void ShowRetargetPanel(MoveGroups groups)
        {
            if (TryBeginRetargetPick(groups))
            {
                return;
            }
            // Reactions read first; "do nothing" is the fallback under them.
            var reactionMoves = groups.ModalMoves
                .OrderBy(m => IsPassOrSkip(m) ? 1 : 0)
                .ToList();
            _prompt.Hide();
            _reactionBanner = ModalTitle();
            _reaction.Show(ModalCards(), GroupedPlayOptions(reactionMoves, tableLive: true),
                "PICK A NEW TARGET");
        }

        private void ShowBlurPrompt(MoveGroups groups)
        {
            // The escape hatch leads, so it never scrolls off-screen.
            var orderedMoves = groups.ModalMoves
                .OrderBy(m => IsPassOrSkip(m) ? 0 : 1)
                .ToList();
            _reaction.Hide();
            _prompt.Show(ModalTitle(), ModalCards(),
                GroupedPlayOptions(orderedMoves, tableLive: false), showCancel: false);
        }

        /// <summary>Rewinds the modal gate: the next tick re-renders and reopens the surface of the pending decision.</summary>
        private void ReopenModalNextTick()
        {
            _modalRevision = -1;
            _modalSignature = "";
            _renderedRevision = -1;
        }

        /// <summary>
        /// Reverse Engineer's recovered card MUST be played: with exactly one way to play it there
        /// is nothing to decide, so it is submitted. Any real choice falls through to the normal flows.
        /// </summary>
        private bool TryAutoForcedPlay(MoveGroups groups, int revision)
        {
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null || decision.Kind != DecisionKind.ForcedPlay ||
                _autoForcedPlayRevision == revision)
            {
                return false;
            }
            // A lone SkipFreePlay means the card cannot be played at all: the player must see that.
            if (groups.ModalMoves.Count != 1 || !(groups.ModalMoves[0] is PlayLemonCard only))
            {
                return false;
            }
            _autoForcedPlayRevision = revision;
            _prompt.Hide();
            _reaction.Hide();
            Submit(only);
            return true;
        }

        /// <summary>
        /// A response window whose ONLY move is Pass is not a choice, and must never be a modal
        /// the table waits on: it is passed without a screen.
        /// </summary>
        private bool TryAutoPassEmptyWindow(MoveGroups groups, int revision)
        {
            if (!IsPassOnlyWindow(groups))
            {
                return false;
            }
            if (_autoPassRevision != revision)
            {
                _autoPassRevision = revision;
                _prompt.Hide();
                _reaction.Hide();
                Submit(groups.ModalMoves[0]);
            }
            return true;
        }
    }
}
