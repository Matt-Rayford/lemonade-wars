using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>Revision-gated rendering: the table, turn banner and action bars, plus
    /// the end-of-game scoreboard.</summary>
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
            if (_autoModalOpen &&
                (_prompt.IsOpen || _picker.IsOpen ||
                 _table.DiscardPickerOpen || _table.EquipTargetingActive) &&
                (groups == null || !groups.IsModal || groups.ModalMoves.Count == 0))
            {
                _prompt.Hide();
                _picker.Hide();
                // Silent: a stale grid/aim would submit dead moves, and its cancel
                // callback would reopen the dissolved flow.
                _table.DismissModalOverlays();
                _autoModalOpen = false;
                _modalSignature = "";
            }

            // A resolved response window: the reaction panel must not outlive the
            // stack item it was answering.
            if (_reaction.IsOpen && !ReactionWindowDue(groups))
            {
                _reaction.Hide();
            }

            try
            {
                RenderBanner();
                // A tumbling die withholds every affordance: the roll may still open
                // a response window, and acting on the next turn before the sale
                // resolves is exactly what the theatre exists to prevent. The die's
                // OnFinished forces the re-render that hands them back.
                var actionable = _dice.IsBusy ? null : groups;
                _table.Render(View, _db, actionable);
                _table.SetLog(_actionLog);
                RenderActionBar(actionable);
                RenderTurnActions(actionable);
            }
            catch (System.Exception e)
            {
                // A render throw must never silently brick the frame (stale action
                // bar, half-drawn table).
                Debug.LogException(e);
                Debug.LogError($"render error: {e.GetType().Name} — see the exception above");
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
            if (_turnBanner.IsOpen || _dice.IsBusy || _fx.IsBusy || _playTheatre.IsBusy)
            {
                return; // decisions wait behind the ONWARD! button / the die / effects / a staged attack's arrival
            }
            MaybeShowModal(groups, revision);
        }

        /// <summary>Show the YOUR TURN interstitial when the turn passes to the viewer.</summary>
        private void MaybeAnnounceTurn()
        {
            bool myTurn = IsMyTurn();
            if (myTurn && !_wasMyTurn && (_dice.IsBusy || _fx.IsBusy || _playTheatre.IsBusy))
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

        /// <summary>
        /// The players the table is blocked on, joined for the banner — when that is
        /// not simply the active player taking an ordinary turn, not you (your own
        /// waits are the reaction panel or a modal), and not the setup stages, which
        /// narrate themselves. Null when nobody else is holding things up.
        /// </summary>
        private string WaitingOnOthers()
        {
            if (View.Stage == GameStage.ChoosingLemonLords || View.Stage == GameStage.InitialBuys ||
                View.ActingPlayers.Count == 0 || View.ActingPlayers.Contains(View.ViewerId))
            {
                return null;
            }
            bool isPlainTurn = View.StackTop == null && View.PendingRollValue == null &&
                View.ActingPlayers.Count == 1 && View.ActingPlayers[0] == View.ActivePlayer;
            return isPlainTurn ? null : string.Join(" & ", View.ActingPlayers.Select(NameOf));
        }

        /// <summary>
        /// The one line of words the table keeps: whose turn, who is being waited on,
        /// what card is resolving. Yellow, centred in the clear strip between the
        /// shelf and the board, where the eye rests between the two.
        /// </summary>
        private void RenderBanner()
        {
            string banner;
            if (_reaction.IsOpen)
            {
                // The response panel's headline lives HERE while it is open, so the
                // panel itself can stay all cards and buttons.
                banner = _reactionBanner;
            }
            else if (View.Stage == GameStage.Finished)
            {
                banner = "Thanks for playing!";
            }
            else if (WaitingOnOthers() is string holdouts)
            {
                // Someone else has to respond or decide before anything moves: say
                // who (the hourglass on their bar says the same thing in pictures).
                banner = $"Waiting on {holdouts}";
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
                // Once your picks are confirmed, the only thing left is other people.
                banner = View.ActingPlayers.Contains(View.ViewerId)
                    ? "Choose your secret Lemon Lord titles"
                    : "Waiting On Everyone Else";
            }
            else if (View.Stage == GameStage.InitialBuys)
            {
                banner = $"Setup draft — {NameOf(View.CurrentInitialBuyer ?? 0)} is buying";
            }
            else if (View.ActivePlayer == View.ViewerId)
            {
                banner = $"Your Turn ({ActionsLabel(View.ActionsRemaining)})";
            }
            else
            {
                banner = $"{NameOf(View.ActivePlayer)}'s turn — {View.Phase}";
            }
            _tableMessage.text = banner;
        }

        /// <summary>
        /// Third-person headline for the table message while a card sits on the stack:
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
        /// The two "advance the game" buttons, pinned top-right on the bare table
        /// between the shelf and the board: short labels, the table's chip look.
        /// </summary>
        private void RenderTurnActions(MoveGroups groups)
        {
            UiKit.Clear(_turnActionRow);
            if (View.Stage == GameStage.Finished)
            {
                // Back on the board after the scoreboard: the exit lives where the
                // sale chip did, so leaving is never a hunt.
                AddTurnActionChip("LEAVE GAME", () => BackToMenu(""), Sfx.ButtonClick);
                return;
            }
            if (groups == null || groups.IsModal)
            {
                return;
            }
            var endTurn = groups.BarMoves.FirstOrDefault(m => m is EndTurn);
            if (endTurn != null)
            {
                // Silent: the die's own roll sound covers it. (Passing a draft visit is
                // the big table button instead — see RenderActionBar.)
                AddTurnActionChip("END TURN (SELL)", () => Submit(endTurn), null);
            }
        }

        /// <summary>One centered chip in the top-right row (End Turn, Leave Game).</summary>
        private void AddTurnActionChip(string label, UnityEngine.Events.UnityAction onClick, string clickSound)
        {
            var button = UiKit.CreateButton(_turnActionRow, label, 16, onClick,
                clickSound: clickSound);
            var element = button.GetComponent<LayoutElement>();
            element.minWidth = 190;
            element.minHeight = 36;
            var text = button.GetComponentInChildren<TMPro.TMP_Text>();
            text.alignment = TMPro.TextAlignmentOptions.Center;
            UiKit.Anchor((RectTransform)text.transform, Vector2.zero, Vector2.one);
        }

        /// <summary>Table-button height: fills the message strip at 1080p, never touches the shelf.</summary>
        private const float EndTurnButtonHeightPx = 52f;

        private void RenderActionBar(MoveGroups groups)
        {
            UiKit.Clear(_table.ActionBar);
            UiKit.Clear(_tableActionHost);
            if (groups == null)
            {
                return;
            }
            if (groups.IsModal)
            {
                if (ReactionWindowDue(groups) && View.MyDecisions.Count == 0 && !TheatreHoldsModals())
                {
                    RenderReactionStrip(groups);
                }
                return;
            }
            foreach (var move in groups.BarMoves)
            {
                if (move is RefreshMarket || move is DrawLemonCard ||
                    move is EndTurn || move is InitialBuyEnd)
                {
                    continue; // these live on the table / in the top-right chip row
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
            var endTurn = groups.BarMoves.FirstOrDefault(m => m is EndTurn);
            if (endTurn != null && View.ActionsRemaining == 0)
            {
                // Out of actions: the only thing left to do takes over the message strip
                // above the board, at the message's own size — "Your Turn (0 actions)"
                // would only repeat what the button says. (The top-right chip stays for
                // ending early.) Silent click: the sale roll has its own sound.
                AddTableActionButton("End Turn & Sell", endTurn, clickSound: null);
            }
            var passDraft = groups.BarMoves.FirstOrDefault(m => m is InitialBuyEnd);
            if (passDraft != null)
            {
                // The draft visit after the mandatory stand: buying a Black Market card
                // is optional, so the way out is the same big button, labelled plainly.
                AddTableActionButton("Pass", passDraft, Sfx.ButtonClick);
            }
        }

        /// <summary>
        /// A plain response window, answered from the table: "Play a reaction or"
        /// followed by a button per answer that is not a hand card (an Inflatable
        /// Decoy, a die ability) and Pass. The hand cards that answer are half-raised
        /// in the fan; clicking one plays it.
        /// </summary>
        private void RenderReactionStrip(MoveGroups groups)
        {
            _tableMessage.text = "";
            var lead = UiKit.CreateText(_tableActionHost, "Play a reaction or",
                Mathf.RoundToInt(_tableMessage.fontSize), TextAnchor.MiddleCenter, UiKit.ButtonColor);
            lead.raycastTarget = false;
            UiKit.AddTextShadow(lead);
            // No fixed width: the row sizes the words to their own length, so the
            // button sits one spacing step after the last letter.

            foreach (var move in groups.ModalMoves)
            {
                bool fromEquipment = move is UseTurnAbility ||
                                     (move is RespondToWindow respond && respond.EquippedInstanceId != null);
                if (fromEquipment)
                {
                    AddStripButton(_session.LabelFor(move), move, Sfx.ButtonClick);
                }
            }
            var pass = groups.ModalMoves.FirstOrDefault(m => m is PassWindow);
            if (pass != null)
            {
                AddStripButton("Pass", pass, Sfx.ButtonClick);
            }
        }

        /// <summary>A modal is held back while theatre plays; the reaction strip waits for the same beat.</summary>
        private bool TheatreHoldsModals() =>
            _turnBanner.IsOpen || _dice.IsBusy || _fx.IsBusy || _playTheatre.IsBusy;

        private void AddStripButton(string label, GameAction move, string clickSound)
        {
            var button = UiKit.CreateButton(_tableActionHost, label,
                Mathf.RoundToInt(_tableMessage.fontSize), () => Submit(move),
                clickSound: clickSound, highlighted: true);
            var layout = button.GetComponent<LayoutElement>();
            layout.minWidth = 170f;
            layout.minHeight = EndTurnButtonHeightPx;
        }

        /// <summary>
        /// The big yellow button in the message strip above the board — the one thing
        /// to do next, in place of the words that would only repeat it.
        /// </summary>
        private void AddTableActionButton(string label, GameAction move, string clickSound)
        {
            _tableMessage.text = "";
            var button = UiKit.CreateButton(_tableActionHost, label,
                Mathf.RoundToInt(_tableMessage.fontSize), () => Submit(move),
                clickSound: clickSound, highlighted: true);
            var layout = button.GetComponent<LayoutElement>();
            layout.minWidth = 360;
            layout.minHeight = EndTurnButtonHeightPx;
        }

        // ---------------------------------------------------------- game over

        /// <summary>The final scoreboard waits for the last effects to land, then shows once.</summary>
        private void MaybeShowEndSummary()
        {
            if (View != null && View.Stage == GameStage.Finished && !_endShown &&
                !_fx.IsBusy && !_dice.IsBusy && !_turnBanner.IsOpen && !_picker.IsOpen &&
                !_playTheatre.IsBusy)
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
            // Two real exits instead of a lone "Cancel": look the final boards over,
            // or head back to the menu.
            _prompt.Show($"{winners.ToUpperInvariant()} {verb} THE LEMONADE WARS!",
                new List<Texture2D>(),
                rows.Select(r => new Prompt.Option(r.label, null)).ToList(),
                showCancel: false,
                footer: new[]
                {
                    new Prompt.Option("Return to Board", () => { }),
                    new Prompt.Option("Leave Game", () => BackToMenu("")),
                });
        }
    }
}
