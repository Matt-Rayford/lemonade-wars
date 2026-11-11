using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    public sealed partial class GreedyBot
    {
        private static double ScoreAbilityChoice(Game game, PlayerState me, SubmitAbilityChoice choice)
        {
            var state = game.State;
            if (choice.Decline)
            {
                return 5; // a free steal beats passing, whoever the victim
            }
            if (choice.TargetPlayerId is int victimId)
            {
                var victim = state.Players[victimId];
                return 20 + victim.Money * 0.6 + 10 * ThreatAssessment.ThreatOf(game, me.PlayerId, victim);
            }
            if (choice.CardInstanceIds.Count > 0)
            {
                double cardsValue = choice.CardInstanceIds.Sum(id => CardValue(game, me, id));
                // Picking from a revealed hand takes the best card; a give-back or a
                // forced discard sheds the worst one.
                bool picksFromVictim = FirstAbilityDecision(state, me)?.Kind == DecisionKind.AbilityPickCard;
                return picksFromVictim ? 20 + cardsValue : 20 - cardsValue;
            }
            if (choice.EquippedInstanceId is int copiedId)
            {
                // Innovation copies a Power Pour: the pricier card has the bigger effect.
                return 20 + game.Db.BlackMarket(state.BlackMarketInstances[copiedId].DefId).Cost;
            }
            if (choice.StandInstanceId is int sellingStandId)
            {
                var sellingStand = me.Stands.First(stand => stand.InstanceId == sellingStandId);
                return 20 + game.StandEarnings(me, sellingStand);
            }
            return 20;
        }

        /// <summary>
        /// The decision a SubmitAbilityChoice answers: the engine takes the player's first
        /// pending decision of the ability group, and legal moves are enumerated for it.
        /// </summary>
        private static PendingDecision? FirstAbilityDecision(GameState state, PlayerState me) =>
            state.PendingDecisions.FirstOrDefault(
                decision => decision.PlayerId == me.PlayerId && IsAbilityDecision(decision.Kind));

        private static bool IsAbilityDecision(DecisionKind kind) =>
            kind == DecisionKind.AbilityVictim || kind == DecisionKind.AbilityPickCard ||
            kind == DecisionKind.AbilityGiveBack || kind == DecisionKind.AbilityDiscard ||
            kind == DecisionKind.InnovationCopy || kind == DecisionKind.WordOfMouthStand;

        /// <summary>
        /// Re-picking the loot after a Tag moved our attack: Finders Keepers takes what
        /// helps US most, That's Not Fair! trashes the victim's priciest card.
        /// </summary>
        private static double ScoreRetarget(Game game, PlayerState me, SubmitRetarget retarget)
        {
            var state = game.State;
            var attack = state.ResponseStack.FirstOrDefault(item => item.ItemId == retarget.StackItemId);
            if (attack == null || !(retarget.TargetEquippedInstanceId is int lootId))
            {
                return 20;
            }
            if (attack.LemonDefId == "finders-keepers")
            {
                return 20 + StolenValue(game, me, lootId, retarget.EquipStandInstanceId);
            }
            return 20 + game.Db.BlackMarket(state.BlackMarketInstances[lootId].DefId).Cost;
        }
    }
}
