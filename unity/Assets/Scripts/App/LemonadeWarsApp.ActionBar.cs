using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// The buttons that advance the game: the hand-side action bar, the top-right End Turn
    /// chip, and the big button that takes over the message strip.
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
                // Silent click: the sale roll has its own sound. The top-right chip stays for ending early.
                AddTableActionButton("End Turn & Sell", endTurn, clickSound: null);
            }
            var passDraft = groups.BarMoves.FirstOrDefault(m => m is InitialBuyEnd);
            if (passDraft != null)
            {
                // The draft's Black Market card is optional: its way out is the same big button.
                AddTableActionButton("Pass", passDraft, Sfx.ButtonClick);
            }
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

        /// <summary>The top-right chip on the bare table: ending the turn before the actions run out.</summary>
        private void RenderTurnActions(MoveGroups groups)
        {
            UiKit.Clear(_turnActionRow);
            if (View.Stage == GameStage.Finished)
            {
                return; // the scoreboard carries the exit; Esc's pause menu has the other
            }
            if (groups == null || groups.IsModal || _turnBanner.IsOpen)
            {
                return; // the banner's dismiss re-renders, and the chip comes back then
            }
            var endTurn = groups.BarMoves.FirstOrDefault(m => m is EndTurn);
            if (endTurn != null)
            {
                // Silent: the die's own roll sound covers it.
                AddTurnActionChip("END TURN (SELL)", () => Submit(endTurn), null);
            }
        }

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
    }
}
