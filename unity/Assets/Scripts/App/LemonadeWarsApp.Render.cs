using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>Revision-gated rendering: the table, status bar, turn banner and action
    /// bars, plus the end-of-game scoreboard.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private bool _endShown;

        private void RenderIfChanged()
        {
            if (View == null)
            {
                return;
            }
            // Hold renders while the player drags from the shelf: a rebuild would
            // destroy the drag source under the cursor, cancelling the drag (and,
            // before DragSource.OnDisable existed, leaking the ghost). The revision
            // stays unconsumed, so the render fires the frame the drag ends.
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

            // A window/decision can dissolve underneath an open modal — the engine
            // auto-passes players who become ineligible when a window recomputes, and
            // others' responses can resolve the whole stack. Window prompts have no
            // Cancel, so a stale one soft-locks the player: dismiss it.
            if (_autoModalOpen && (_prompt.IsOpen || _picker.IsOpen) &&
                (groups == null || !groups.IsModal || groups.ModalMoves.Count == 0))
            {
                _prompt.Hide();
                _picker.Hide();
                _autoModalOpen = false;
                _modalSignature = "";
            }

            try
            {
                RenderStatus();
                RenderBanner();
                _table.Render(View, _db, groups);
                _table.SetLog(_actionLog);
                RenderActionBar(groups);
                RenderTurnActions(groups);
            }
            catch (System.Exception e)
            {
                // A render throw must never silently brick the frame (stale action
                // bar, half-drawn table). Name the culprit where the player looks.
                Debug.LogException(e);
                _statusText.text = $"render error: {e.GetType().Name} — check the console";
            }
            MaybeAnnounceTurn();
            // A finished player-pick (or a state that moved past it) must drop the
            // aiming overlay, or the arrow lingers over a resolved decision.
            if (_table.PlayerPickActive)
            {
                var decision = View.MyDecisions.FirstOrDefault();
                if (groups == null || !groups.IsModal ||
                    decision == null || decision.Kind != DecisionKind.AbilityVictim)
                {
                    _table.EndPlayerPick();
                }
            }
            // Same for a resolved response window: the reaction panel must not
            // outlive the stack item it was answering.
            if (_reaction.IsOpen && !ReactionWindowDue(groups))
            {
                _reaction.Hide();
            }
            if (_turnBanner.IsOpen || _dice.IsBusy || _fx.IsBusy)
            {
                return; // decisions wait behind the ONWARD! button / the die / effects
            }
            MaybeShowModal(groups, revision);
        }

        /// <summary>Show the YOUR TURN interstitial when the turn passes to the viewer.</summary>
        private void MaybeAnnounceTurn()
        {
            bool myTurn = IsMyTurn();
            if (myTurn && !_wasMyTurn && (_dice.IsBusy || _fx.IsBusy))
            {
                // Let the die and effects finish first; their OnFinished forces a
                // re-render that lands back here with the turn edge still unconsumed.
                return;
            }
            bool becameMyTurn = myTurn && !_wasMyTurn;
            _wasMyTurn = myTurn;
            if (!becameMyTurn || _session.HumanAutoplay || _turnBanner.IsOpen)
            {
                return;
            }
            // Anything left open from the previous turn yields to the banner; the
            // forced re-render on dismiss re-opens whatever is still relevant.
            _prompt.Hide();
            _picker.Hide();
            _reaction.Hide();
            // Your turn starts at YOUR board: snap back from wherever you were
            // snooping, so ONWARD! lands on a table you can act on.
            if (_table.ViewedBoardPlayer >= 0)
            {
                _table.ViewedBoardPlayer = -1;
                _renderedRevision = -1;
            }
            Sfx.Play(Sfx.YourTurn); // played, not queued: it announces the banner itself
            _turnBanner.Show(TurnSubtitle());
        }

        private static string ActionsLabel(int count) => count == 1 ? "1 action" : $"{count} actions";

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

        private void RenderStatus()
        {
            // VP/cash live on your player panel now; the top-left keeps utility info.
            var me = View.Players[View.ViewerId];
            string status;
            if (View.Stage == GameStage.Finished)
            {
                status = $"GAME OVER — winner: {string.Join(", ", View.Winners.Select(NameOf))}" +
                         (_session is LocalGameSession ? "   [N] new game" : "");
            }
            else
            {
                // Tantrum counts moved onto the player bars, where everyone's shows.
                status = (_session.HumanAutoplay ? "AUTOPILOT [B]" : "[B] autopilot") +
                         (View.WhiniestBabyHolder == View.ViewerId ? "  |  WHINIEST BABY" : "") +
                         (View.SpoiledRottenHolder == View.ViewerId ? "  |  SPOILED ROTTEN" : "") +
                         (_remote != null ? $"  |  room {_remote.Room.Code}" : "");
                // The table is stalled on someone else: say who, so a quiet moment
                // (their response window, their discard) never reads as a hang.
                if (!View.ActingPlayers.Contains(View.ViewerId) && View.ActingPlayers.Count > 0)
                {
                    string verb = View.StackTop != null ? " to respond" : "";
                    status += $"  |  WAITING ON {string.Join(", ", View.ActingPlayers.Select(NameOf)).ToUpperInvariant()}{verb}";
                }
            }
            _statusText.text = status;
        }

        private void RenderBanner()
        {
            string banner;
            if (_reaction.IsOpen)
            {
                // The response panel's headline lives HERE while it is open: the bar
                // was saying nearly the same thing, and the freed band keeps showing
                // the die (or the attack) you are responding to.
                banner = _reactionBanner;
            }
            else if (View.Stage == GameStage.Finished)
            {
                banner = "Thanks for playing!";
            }
            else if (View.StackTop != null)
            {
                // Someone's card is resolving but OUR response panel isn't open
                // (nothing to respond with, or it's someone else's window). Name
                // the play — "Dex's turn — Play" told bystanders nothing.
                banner = StackHeadline();
            }
            else if (View.PendingRollValue is int roll)
            {
                string what = RollScopeName(View.PendingRollPurpose, View.PendingRollStandInstanceId)
                    ?? "Sale";
                banner = $"{what.ToUpperInvariant()} ROLL: {roll}";
            }
            else if (View.Stage == GameStage.ChoosingLemonLords)
            {
                banner = "Choose your secret Lemon Lord titles";
            }
            else if (View.Stage == GameStage.InitialBuys)
            {
                banner = $"Setup draft — {NameOf(View.CurrentInitialBuyer ?? 0)} is buying";
            }
            else if (View.ActivePlayer == View.ViewerId)
            {
                banner = $"Your turn — {View.Phase} ({ActionsLabel(View.ActionsRemaining)} left)";
            }
            else
            {
                banner = $"{NameOf(View.ActivePlayer)}'s turn — {View.Phase}";
            }
            _topBanner.text = banner;
        }

        /// <summary>
        /// Third-person headline for the yellow bar while a card sits on the stack:
        /// plain plays ("Dex plays Automation"), attacks ("Dex plays Slander on Kim"),
        /// and reactions (tags, reflections, tantrums) all name the card instead of
        /// falling back to the generic "X's turn — Phase" label.
        /// </summary>
        private string StackHeadline()
        {
            var top = View.StackTop;
            // CardName, not LemonName: purchases and some responses carry
            // Black Market ids, and CardName checks both catalogs.
            string card = CardName(top.DefId);
            bool self = top.OwnerId == View.ViewerId;
            string owner = self ? "You" : NameOf(top.OwnerId);
            string plays = self ? "play" : "plays";
            if (top.AttackTargetId is int t)
            {
                string victim = t == View.ViewerId ? "you" : NameOf(t);
                return $"{owner} {plays} {card} on {victim}";
            }
            // Reactions name WHAT they answer, mirroring the response panel.
            string answered = null;
            if (!string.IsNullOrEmpty(top.RespondingToDefId))
            {
                answered = top.RespondingToIsPurchase
                    ? $"the {CardName(top.RespondingToDefId)} purchase"
                    : CardName(top.RespondingToDefId);
                if (top.RespondingToOwnerId is int by && !top.RespondingToIsPurchase)
                {
                    answered = (by == View.ViewerId ? "your " : NameOf(by) + "'s ") + answered;
                }
            }
            if (top.DefId == "tag-youre-it" && top.RedirectTargetId is int redirect)
            {
                string who = redirect == View.ViewerId ? "you" : NameOf(redirect);
                return $"{owner} {plays} {card} — {answered ?? "the attack"} goes to {who}";
            }
            if (top.DefId == "im-rubber-youre-glue" && answered != null)
            {
                return $"{owner} {plays} {card} — {answered} is reflected back";
            }
            if (answered != null)
            {
                return $"{owner} {plays} {card} on {answered}";
            }
            if (top.IsPurchase)
            {
                return self ? $"You buy {card}" : $"{owner} buys {card}";
            }
            return $"{owner} {plays} {card}";
        }

        /// <summary>
        /// The two "advance the game" buttons, pinned top-right in the status bar:
        /// short labels, dark chips against the yellow.
        /// </summary>
        private void RenderTurnActions(MoveGroups groups)
        {
            UiKit.Clear(_turnActionRow);
            if (groups == null || groups.IsModal)
            {
                return;
            }
            foreach (var move in groups.BarMoves)
            {
                string label = move is EndTurn ? "END TURN (SELL)"
                    : move is InitialBuyEnd ? "FINISH DRAFT"
                    : null;
                if (label == null)
                {
                    continue;
                }
                var captured = move;
                // End Turn stays silent: the die's own roll sound covers it.
                var button = UiKit.CreateButton(_turnActionRow, label, 16,
                    () => Submit(captured), dark: true,
                    clickSound: captured is EndTurn ? null : Sfx.ButtonClick);
                var element = button.GetComponent<LayoutElement>();
                element.minWidth = 190;
                element.minHeight = 36;
                var text = button.GetComponentInChildren<TMPro.TMP_Text>();
                text.alignment = TMPro.TextAlignmentOptions.Center;
                UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one);
            }
        }

        private void RenderActionBar(MoveGroups groups)
        {
            UiKit.Clear(_table.ActionBar);
            if (groups == null || groups.IsModal)
            {
                return;
            }
            foreach (var move in groups.BarMoves)
            {
                if (move is RefreshMarket || move is DrawLemonCard ||
                    move is EndTurn || move is InitialBuyEnd)
                {
                    continue; // these live on the table / in the top bar now
                }
                var captured = move;
                // Buttons whose ACTION has a sound stay silent, rather than clicking
                // over their own tumble a frame later.
                bool ownsItsSound = captured is EndTurn;
                var button = UiKit.CreateButton(_table.ActionBar,
                    _session.LabelFor(captured), 15, () => Submit(captured),
                    clickSound: ownsItsSound ? null : Sfx.ButtonClick);
                button.GetComponent<LayoutElement>().minWidth = 150;
            }
            if (groups.BarMoves.Count == 0 && groups.SupplyMoves.Count > 0)
            {
                // Draft visits before the mandatory stand: every move is a drag, so
                // the empty bar says what to do instead of looking broken.
                var hint = UiKit.CreateText(_table.ActionBar,
                    "Drag a stand from the shelf onto your row", 15,
                    TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.55f), body: true);
                hint.gameObject.AddComponent<LayoutElement>().minWidth = 320;
            }
        }

        // ---------------------------------------------------------- game over

        /// <summary>The final scoreboard waits for the last effects to land, then shows once.</summary>
        private void MaybeShowEndSummary()
        {
            if (View != null && View.Stage == GameStage.Finished && !_endShown &&
                !_fx.IsBusy && !_dice.IsBusy && !_turnBanner.IsOpen && !_picker.IsOpen)
            {
                _endShown = true;
                ShowEndSummary();
            }
        }

        /// <summary>
        /// Game over: announce the winner and reveal everyone's full score — in-game VP
        /// plus the secret Lemon Lords, met or missed.
        /// </summary>
        private void ShowEndSummary()
        {
            _prompt.Hide();
            var rows = View.Players
                .Select(p =>
                {
                    int lordVp = p.FinishedLords.Count(l => l.Met);
                    int total = p.InGameVictoryPoints + lordVp;
                    string lords = p.FinishedLords.Count > 0
                        ? "   [" + string.Join(", ", p.FinishedLords.Select(l =>
                            _db.Title(l.TitleId).Name + (l.Met ? " +1" : " —"))) + "]"
                        : "";
                    return (total, label: $"{p.Name} — {total} VP{lords}");
                })
                .OrderByDescending(r => r.total)
                .ToList();
            string winners = string.Join(" & ", View.Winners.Select(NameOf));
            string verb = View.Winners.Count > 1 ? "WIN" : "WINS";
            _prompt.Show($"{winners.ToUpperInvariant()} {verb} THE LEMONADE WARS!",
                new List<Texture2D>(),
                rows.Select(r => new Prompt.Option(r.label, null)).ToList(),
                showCancel: true);
        }
    }
}
