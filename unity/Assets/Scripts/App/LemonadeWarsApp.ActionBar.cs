using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The buttons that advance the game: the hand-side action bar, and the End Turn button
    /// in the message strip, beside its words or in place of them.
    /// </summary>
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>Table-button height: fills the message strip at 1080p, never touches the shelf.</summary>
        private const float EndTurnButtonHeightPx = 52f;

        private void RenderActionBar(MoveGroups groups)
        {
            UiKit.Clear(_table.ActionBar);
            UiKit.Clear(_tableActionHost);
            DropStaleAssetSale(groups);
            if (groups == null)
            {
                return;
            }
            if (_sale != null)
            {
                RenderSaleStrip();
                return;
            }
            if (groups.IsModal)
            {
                RenderDecisionStrip(groups);
                return;
            }
            AddBarMoveButtons(groups);
            AddNextStepTableButtons(groups);
        }

        /// <summary>The decisions answered from the message strip; every other modal leaves it empty.</summary>
        private void RenderDecisionStrip(MoveGroups groups)
        {
            // Nothing to choose: it passes itself (see TryAutoPassEmptyWindow).
            if (IsPassOnlyWindow(groups))
            {
                return;
            }
            if (IsAnsweredFromHandAndStrip(groups) && !TheatreHoldsModals())
            {
                RenderReactionStrip(groups);
            }
            else if (TimeoutFineDue() && !TheatreHoldsModals())
            {
                // Short of the fine: the player picks what to sell, as for a Rummage Sale.
                if (TimeoutNeedsSale())
                {
                    BeginTimeoutSale();
                    RenderSaleStrip();
                }
                else
                {
                    RenderTimeoutStrip(groups);
                }
            }
        }

        private void AddBarMoveButtons(MoveGroups groups)
        {
            foreach (var move in groups.BarMoves)
            {
                // These live on the table or in the top-right chip row.
                if (move is RefreshMarket || move is DrawLemonCard ||
                    move is EndTurn || move is InitialBuyEnd)
                {
                    continue;
                }
                var button = UiKit.CreateButton(_table.ActionBar,
                    _session.LabelFor(move), 15, () => Submit(move));
                button.GetComponent<LayoutElement>().minWidth = 150;
            }
        }

        /// <summary>The one thing left to do takes over the message strip, in place of words that would repeat it.</summary>
        private void AddNextStepTableButtons(MoveGroups groups)
        {
            var endTurn = groups.BarMoves.FirstOrDefault(m => m is EndTurn);
            if (endTurn != null && View.ActionsRemaining == 0)
            {
                // Silent click: the sale roll has its own sound.
                AddTableActionButton("End Turn & Sell", endTurn, clickSound: null);
            }
            else if (endTurn != null && !TurnHandoverPending())
            {
                OfferEarlyEndTurnBesideTheWords(endTurn);
            }
            var passDraft = groups.BarMoves.FirstOrDefault(m => m is InitialBuyEnd);
            if (passDraft != null)
            {
                // The draft's Black Market card is optional: its way out is the same big button.
                AddTableActionButton("Pass", passDraft, Sfx.ButtonClick);
            }
        }

        /// <summary>
        /// Actions still in hand: the strip keeps its words and adds the way out after them,
        /// "Your Turn (2 actions) or [End Turn & Sell]". The words move into the button row so
        /// the two sit side by side, centred as one line.
        /// </summary>
        private void OfferEarlyEndTurnBesideTheWords(GameAction endTurn)
        {
            string words = _tableMessage.text;
            _tableMessage.text = "";
            if (!string.IsNullOrEmpty(words))
            {
                AddStripText(words);
                AddStripText("or");
            }
            AddStripButton("End Turn & Sell", endTurn, clickSound: null); // silent, as above
        }

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
    }
}
