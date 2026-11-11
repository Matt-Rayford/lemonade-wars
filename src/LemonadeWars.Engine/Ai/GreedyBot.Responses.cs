using System;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    public sealed partial class GreedyBot
    {
        /// <summary>Passing a window scores 10: a response must beat that to be played.</summary>
        private static double ScoreResponse(Game game, PlayerState me, RespondToWindow respond)
        {
            var state = game.State;
            if (respond.EquippedInstanceId != null)
            {
                // Inflatable Decoy: use it when the attack is aimed at us.
                var decoyed = state.ResponseStack[state.ResponseStack.Count - 1];
                return decoyed.AttackTargetId == me.PlayerId ? 70 : 4;
            }

            string defId = state.LemonInstances[respond.CardInstanceId].DefId;
            var stackTop = state.ResponseStack.Count > 0
                ? state.ResponseStack[state.ResponseStack.Count - 1]
                : null;
            switch (defId)
            {
                case "tantrum":
                    return stackTop == null ? 0 : ScoreTantrum(game, me, stackTop);
                case "tag-youre-it":
                    return ScoreTag(game, me, respond, stackTop);
                case "im-rubber-youre-glue":
                    return stackTop?.AttackTargetId == me.PlayerId ? 68 : 4;
                case "out-of-stock":
                    return ScoreOutOfStock(game, me);
                case "profit-share":
                    return 80;
                default:
                    return 5;
            }
        }

        private static double ScoreTantrum(Game game, PlayerState me, StackItem stackTop)
        {
            var state = game.State;
            // Friendly Fran (zero tantrums) is a whole VP: a spotless pile makes
            // throwing one much less attractive.
            double franBrake = me.TantrumPile.Count == 0 &&
                me.LemonLordKept.Contains("friendly-fran") ? 30 : 0;
            if (stackTop.AttackTargetId == me.PlayerId)
            {
                return 60 - franBrake;
            }
            if (IsResponseToOwnPlay(state, stackTop, me))
            {
                // Reviving our cancelled play is 55. A Tag or Rubber-Glue on our attack
                // rates higher: left alone it turns the attack around, and the bot would
                // end up tantruming its own card one step later.
                bool redirectsOurAttack = stackTop.LemonDefId == "tag-youre-it" ||
                                          stackTop.LemonDefId == "im-rubber-youre-glue";
                return (redirectsOurAttack ? 58 : 55) - franBrake;
            }
            // A player one VP from triggering the end: deny them anything big.
            if (state.Players[stackTop.OwnerId].InGameVictoryPoints >=
                game.Db.Config.VictoryPointsToTriggerEnd - 1)
            {
                if (stackTop.Kind == StackItemKind.BlackMarketPurchase)
                {
                    var purchased = game.Db.BlackMarket(
                        state.BlackMarketInstances[stackTop.BmInstanceId!.Value].DefId);
                    return (purchased.Cost >= 6 ? 30 : 12) - franBrake;
                }
                return 25 - franBrake;
            }
            // A play that doesn't touch us: a tantrum on it is spite (and feeds the
            // Whiniest Baby fine), so passing wins. Expert Whiner is the exception:
            // played tantrums ARE its VP condition.
            return me.LemonLordKept.Contains("expert-whiner") ? 12 : (2 - franBrake);
        }

        private static double ScoreTag(Game game, PlayerState me, RespondToWindow respond, StackItem? stackTop)
        {
            if (stackTop?.AttackTargetId != me.PlayerId)
            {
                return 5;
            }
            // Pass the attack on to whoever is winning (2-player: no choice).
            double redirectThreat = respond.RedirectTargetId is int newVictimId
                ? ThreatAssessment.ThreatOf(game, me.PlayerId, game.State.Players[newVictimId])
                : 0;
            return 65 + 5 * redirectThreat;
        }

        /// <summary>Reroll when the current value earns us nothing.</summary>
        private static double ScoreOutOfStock(Game game, PlayerState me)
        {
            int rolled = game.State.PendingRoll?.Value ?? 0;
            bool earnsOnRoll = me.Stands.Any(stand => game.SaleNumbersOf(stand).Contains(rolled)) ||
                               game.PourNumbersOf(me).Contains(rolled);
            return earnsOnRoll ? 2 : 14;
        }

        /// <summary>The stack top is a reaction (Tantrum / Tag / Rubber-Glue) aimed at a play of ours.</summary>
        private static bool IsResponseToOwnPlay(GameState state, StackItem stackTop, PlayerState me) =>
            stackTop.RespondingToItemId is int answeredId &&
            state.ResponseStack.Any(item => item.ItemId == answeredId && item.OwnerId == me.PlayerId);

        private static double ScoreAbility(Game game, PlayerState me, UseTurnAbility ability)
        {
            var state = game.State;
            string defId = game.Db.BlackMarket(state.BlackMarketInstances[ability.EquippedInstanceId].DefId).Id;
            if (defId == "liquid-energy")
            {
                return 26;
            }
            if (state.PendingRoll == null)
            {
                return 0;
            }

            int rolled = state.PendingRoll.Value;
            int earningsNow = EarningsOnRoll(game, me, rolled);
            switch (defId)
            {
                case "downsell":
                    return EarningsOnRoll(game, me, Math.Max(1, rolled - 1)) > earningsNow ? 30 : 1;
                case "sugared-up":
                    return EarningsOnRoll(game, me, Math.Min(6, rolled + 1)) > earningsNow ? 30 : 1;
                case "take-two":
                    return earningsNow == 0 ? 22 : 1;
                default:
                    return 1;
            }
        }

        private static int EarningsOnRoll(Game game, PlayerState me, int dieValue) =>
            me.Stands.Sum(stand =>
                game.SaleNumbersOf(stand).Contains(dieValue) ? game.StandEarnings(me, stand) : 0) +
            (game.PourNumbersOf(me).Contains(dieValue) ? 1 : 0);
    }
}
