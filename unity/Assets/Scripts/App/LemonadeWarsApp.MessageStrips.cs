using System.Linq;
using LemonadeWars.Engine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LemonadeWars.Unity
{
    /// <summary>Decisions answered from the message strip with the table live: a response window, the Timeout fine.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private const float StripButtonMinWidthPx = 170f;
        /// <summary>Room around a strip button's label, both sides together.</summary>
        private const float StripButtonPaddingPx = 48f;
        /// <summary>A Timeout that fined nobody still lies on the table this long after landing.</summary>
        private const float TimeoutCardLingerSeconds = 1.6f;

        /// <summary>
        /// Equipped abilities and the way out are strip buttons; the hand cards that answer
        /// sit half-raised in the fan, and clicking one plays it.
        /// </summary>
        private void RenderReactionStrip(MoveGroups groups)
        {
            _tableMessage.text = "";
            // A theft or a free play asks a question, then lists its answers. Anything else is
            // a list of choices with the way out last: [Use Sugared Up] or [Pass].
            bool asksQuestion = View.TheftOnMe || IsFreePlayDecision();
            bool handCanAnswer = groups.HandMoves.Values.Any(moves => moves.Any(m => m is RespondToWindow));
            var abilities = groups.ModalMoves
                .Where(move => move is UseTurnAbility ||
                               (move is RespondToWindow respond && respond.EquippedInstanceId != null))
                .ToList();
            var wayOut = groups.ModalMoves.FirstOrDefault(m => m is PassWindow)
                ?? groups.ModalMoves.FirstOrDefault(m => m is SkipFreePlay);

            if (asksQuestion)
            {
                AddStripText(ModalTitle());
            }
            else if (handCanAnswer)
            {
                AddStripText("Play a reaction");
            }
            foreach (var ability in abilities)
            {
                AddStripButton(_session.LabelFor(ability), ability, Sfx.ButtonClick);
            }
            if (wayOut == null)
            {
                return;
            }
            if (!asksQuestion && (handCanAnswer || abilities.Count > 0))
            {
                AddStripText("or");
            }
            AddStripButton(wayOut is PassWindow ? "Pass" : "Skip", wayOut, Sfx.ButtonClick);
        }

        /// <summary>The engine's one payment move already lists the cheapest assets to sell, so the button says so.</summary>
        private void RenderTimeoutStrip(MoveGroups groups)
        {
            var payment = groups.ModalMoves.OfType<SubmitTimeoutPayment>().FirstOrDefault();
            if (payment == null)
            {
                return;
            }
            _tableMessage.text = "";
            AddStripText("Timeout!");
            bool sellsAssets = payment.SellStandInstanceIds.Count > 0 || payment.SellBmInstanceIds.Count > 0;
            string label = sellsAssets
                ? $"Sell assets & pay ${View.TimeoutFineAmount}"
                : $"Pay ${View.TimeoutFineAmount}";
            AddStripButton(label, payment, Sfx.ButtonClick);
        }

        /// <summary>
        /// The Timeout card lies on the table from its landing until the fine is paid: never under
        /// the turn banner, mid-flight, or while its owner sells for it. Runs every frame.
        /// </summary>
        private void SyncTimeoutCard()
        {
            if (View == null)
            {
                return;
            }
            bool owed = View.TimeoutFinePlayerId != null;
            bool lingering = Time.time - _timeoutCardShownAt < TimeoutCardLingerSeconds;
            bool wanted = (owed || lingering) && !_turnBanner.IsOpen && !_timeoutCardInFlight && !TimeoutSaleActive;
            if (wanted && !_table.TimeoutCardShown)
            {
                _timeoutCardShownAt = Time.time;
                _table.ShowTimeoutCard(_art.Lemon(TimeoutDefId));
            }
            else if (!wanted)
            {
                _table.HideTimeoutCard();
            }
        }

        private void AddStripText(string words)
        {
            var text = UiKit.CreateText(_tableActionHost, words,
                Mathf.RoundToInt(_tableMessage.fontSize), TextAnchor.MiddleCenter, UiKit.ButtonColor);
            text.raycastTarget = false;
            UiKit.AddTextShadow(text);
        }

        private void AddStripButton(string label, GameAction move, string clickSound)
        {
            var button = UiKit.CreateButton(_tableActionHost, label,
                Mathf.RoundToInt(_tableMessage.fontSize), () => Submit(move),
                clickSound: clickSound, highlighted: true);
            // Wide enough for its whole label on one line.
            var text = button.GetComponentInChildren<TMPro.TMP_Text>();
            text.enableWordWrapping = false;
            var layout = button.GetComponent<LayoutElement>();
            layout.minWidth = Mathf.Max(StripButtonMinWidthPx, text.preferredWidth + StripButtonPaddingPx);
            layout.minHeight = EndTurnButtonHeightPx;
        }
    }
}
