using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    /// <summary>Choose-N-cards decisions, routed to the lift-and-glow card picker.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private bool TryShowPicker()
        {
            if (View.Stage == GameStage.ChoosingLemonLords)
            {
                ShowLemonLordPicker();
                return true;
            }

            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null)
            {
                return false;
            }

            switch (decision.Kind)
            {
                case DecisionKind.DiscardToHandLimit:
                    ShowCardPicker($"Timeout! Discard {decision.RequiredCount} card(s)",
                        View.Hand.ToList(), decision.RequiredCount, SubmitDiscardOf);
                    return true;

                case DecisionKind.WhiniestBabyDiscard:
                    ShowWhiniestBabyKeepPicker(decision);
                    return true;

                case DecisionKind.AbilityDiscard:
                    ShowAbilityDiscardPicker(decision);
                    return true;

                case DecisionKind.AbilityPickCard:
                    ShowCardPicker(
                        WithSourcePrefix(decision, $"pick a card from {NameOf(decision.ChosenPlayerId ?? 0)}'s hand"),
                        View.RevealedHand ?? new List<PlayerView.CardInfo>(), 1, SubmitAbilityCards);
                    return true;

                case DecisionKind.AbilityGiveBack:
                    ShowCardPicker(WithSourcePrefix(decision, "give back a different card"),
                        View.Hand.Where(c => c.InstanceId != decision.StolenCardId).ToList(), 1, SubmitAbilityCards);
                    return true;

                default:
                    return false;
            }
        }

        private void ShowLemonLordPicker()
        {
            var dealt = View.LemonLordDealt.ToList();
            _picker.Show($"Keep {_db.Config.LemonLordKept} secret Lemon Lord titles",
                dealt.Select(id => _art.Title(id)).ToList(),
                _db.Config.LemonLordKept,
                picked => Submit(new ChooseLemonLords
                {
                    KeepTitleIds = picked.Select(i => dealt[i]).ToList(),
                }),
                preview: false, // three near-full-size cards need no magnifier
                // The First Dibs on offer steer which lords are worth keeping.
                context: View.FirstDibsRow.Select(id => _art.Title(id)).ToList(),
                contextLabel: "FIRST DIBS CARDS");
        }

        /// <summary>Restricted to the cards just drawn, and phrased positively: pick what you KEEP.</summary>
        private void ShowWhiniestBabyKeepPicker(PlayerView.DecisionInfo decision)
        {
            var drawn = EligibleHandCards(decision);
            int keepCount = drawn.Count - decision.RequiredCount;
            if (keepCount > 0)
            {
                string title = keepCount == 1
                    ? "Whiniest Baby: pick 1 new card to keep"
                    : $"Whiniest Baby: pick {keepCount} new cards to keep";
                ShowCardPicker(title, drawn, keepCount,
                    keptIds => SubmitDiscardOf(InstanceIdsExcept(drawn, keptIds)));
            }
            else
            {
                // Thin deck: everything drawn must go, so no keep choice exists.
                ShowCardPicker($"Whiniest Baby: discard {decision.RequiredCount} card(s)",
                    drawn, decision.RequiredCount, SubmitDiscardOf);
            }
        }

        /// <summary>Whispers of Fate's "drew 2, discard 1 of them": the fresh draws, phrased as a keep-pick.</summary>
        private void ShowAbilityDiscardPicker(PlayerView.DecisionInfo decision)
        {
            var pool = EligibleHandCards(decision);
            int keepCount = pool.Count - decision.RequiredCount;
            if (decision.EligibleCardIds != null && keepCount > 0)
            {
                string title = WithSourcePrefix(decision, keepCount == 1
                    ? "pick 1 new card to keep"
                    : $"pick {keepCount} new cards to keep");
                ShowCardPicker(title, pool, keepCount,
                    keptIds => SubmitAbilityCards(InstanceIdsExcept(pool, keptIds)));
            }
            else
            {
                ShowCardPicker(WithSourcePrefix(decision, $"discard {decision.RequiredCount} card(s)"),
                    pool, decision.RequiredCount, SubmitAbilityCards);
            }
        }

        private List<PlayerView.CardInfo> EligibleHandCards(PlayerView.DecisionInfo decision) =>
            decision.EligibleCardIds != null
                ? View.Hand.Where(c => decision.EligibleCardIds.Contains(c.InstanceId)).ToList()
                : View.Hand.ToList();

        private static List<int> InstanceIdsExcept(List<PlayerView.CardInfo> pool, List<int> keptIds) =>
            pool.Select(c => c.InstanceId).Where(id => !keptIds.Contains(id)).ToList();

        private void SubmitDiscardOf(List<int> instanceIds) =>
            Submit(new SubmitDiscard { InstanceIds = instanceIds });

        private void SubmitAbilityCards(List<int> instanceIds) =>
            Submit(new SubmitAbilityChoice { CardInstanceIds = instanceIds });

        private void ShowCardPicker(string title, List<PlayerView.CardInfo> pool, int requiredCount,
            System.Action<List<int>> acceptInstanceIds)
        {
            _picker.Show(title,
                pool.Select(c => _art.Lemon(c.DefId)).ToList(),
                requiredCount,
                pickedIndices => acceptInstanceIds(pickedIndices.Select(i => pool[i].InstanceId).ToList()));
        }
    }
}
