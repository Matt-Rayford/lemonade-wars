using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The message strip's one line of words, between the shelf and the board: whose turn,
    /// who is being waited on, what card is resolving.
    /// </summary>
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>What the message strip last said: held over while a turn change waits on theatre.</summary>
        private string _lastShownBanner;

        private void RenderBanner()
        {
            string banner = CurrentBannerText();
            // The strip keeps saying whose turn it WAS until the turn visibly changes hands.
            if (TurnHandoverPending() && _lastShownBanner != null)
            {
                banner = _lastShownBanner;
            }
            _lastShownBanner = banner;
            _tableMessage.text = banner;
        }

        private string CurrentBannerText()
        {
            if (_dice.Headline is string dieHeadline)
            {
                return dieHeadline;
            }
            // The response panel's headline lives in the strip, so the panel stays all cards and buttons.
            if (_reaction.IsOpen)
            {
                return _reactionBanner;
            }
            if (View.Stage == GameStage.Finished)
            {
                return "Thanks for playing!";
            }
            if (View.TimeoutFinePlayerId is int debtor && debtor != View.ViewerId)
            {
                return $"Timeout! {NameOf(debtor)} owes ${View.TimeoutFineAmount}";
            }
            if (WaitingOnOthers() is string holdouts)
            {
                return $"Waiting on {holdouts}";
            }
            // A card is resolving and the viewer's response panel is not open: name the play.
            if (View.StackTop != null)
            {
                return StackHeadline();
            }
            if (View.PendingRollValue is int roll && !_turnStartRollWaiting)
            {
                string what = RollScopeName(View.PendingRollPurpose, View.PendingRollStandInstanceId)
                    ?? "Sale";
                return $"{what.ToUpperInvariant()} ROLL: {roll}";
            }
            return WhoseTurnText();
        }

        private string WhoseTurnText()
        {
            if (View.Stage == GameStage.ChoosingLemonLords)
            {
                return View.ActingPlayers.Contains(View.ViewerId)
                    ? "Choose your secret Lemon Lord titles"
                    : "Waiting On Everyone Else";
            }
            if (View.Stage == GameStage.InitialBuys)
            {
                return View.CurrentInitialBuyer == View.ViewerId
                    ? "Setup Draft — Your Turn"
                    : $"Setup Draft — {NameOf(View.CurrentInitialBuyer ?? 0)} is buying";
            }
            if (View.ActivePlayer == View.ViewerId)
            {
                return $"Your Turn ({ActionsLabel(View.ActionsRemaining)})";
            }
            return $"{NameOf(View.ActivePlayer)}'s Turn ({ActionsLabel(View.ActionsRemaining)})";
        }

        /// <summary>
        /// Who the table is blocked on, when that is not simply the active player taking an
        /// ordinary turn. Null for the viewer's own waits and the setup stages, which narrate themselves.
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

        /// <summary>Third-person line while a card sits on the stack: "Dex plays Slander on Kim".</summary>
        private string StackHeadline()
        {
            var top = View.StackTop;
            // CardName checks both catalogs: purchases and some responses carry Black Market ids.
            string card = CardName(top.DefId);
            bool isOwnPlay = top.OwnerId == View.ViewerId;
            string owner = isOwnPlay ? "You" : NameOf(top.OwnerId);
            string plays = isOwnPlay ? "play" : "plays";
            if (top.AttackTargetId is int targetId)
            {
                string victim = targetId == View.ViewerId ? "you" : NameOf(targetId);
                return $"{owner} {plays} {card} on {victim}";
            }
            string answered = StackAnsweredPhrase(top);
            if (top.DefId == LemonCardIds.TagYoureIt && top.RedirectTargetId is int redirect)
            {
                string who = redirect == View.ViewerId ? "you" : NameOf(redirect);
                return $"{owner} {plays} {card} — {answered ?? "the attack"} goes to {who}";
            }
            if (top.DefId == LemonCardIds.ImRubberYoureGlue && answered != null)
            {
                return $"{owner} {plays} {card} — {answered} is reflected back";
            }
            if (answered != null)
            {
                return $"{owner} {plays} {card} on {answered}";
            }
            if (top.IsPurchase)
            {
                return isOwnPlay ? $"You buy {card}" : $"{owner} buys {card}";
            }
            return $"{owner} {plays} {card}";
        }

        /// <summary>What a reaction answers ("Kim's Slander", "the Bouncer purchase"); null for a plain play.</summary>
        private string StackAnsweredPhrase(PlayerView.StackTopInfo top)
        {
            if (string.IsNullOrEmpty(top.RespondingToDefId))
            {
                return null;
            }
            string answered = top.RespondingToIsPurchase
                ? $"the {CardName(top.RespondingToDefId)} purchase"
                : CardName(top.RespondingToDefId);
            if (top.RespondingToOwnerId is int answeredOwnerId && !top.RespondingToIsPurchase)
            {
                answered = (answeredOwnerId == View.ViewerId ? "your " : NameOf(answeredOwnerId) + "'s ") + answered;
            }
            return answered;
        }
    }
}
