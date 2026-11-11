using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Moves no human would consider: dominated placements of numbered upgrades, money
    /// attacks that would move $0, spite tantrums. Search bots must drop them up front,
    /// because rollout noise can rank a wasted move above its sensible twin.
    /// </summary>
    public static class MoveFilters
    {
        public static bool ObjectivelyWasted(Game game, int playerId, GameAction move)
        {
            switch (move)
            {
                case PlayLemonCard play:
                    return WastedMoneyAttack(game, play);
                case BuyBlackMarket purchase:
                    return WastedPlacement(game, playerId, purchase);
                case RespondToWindow respond:
                    return PointlessTantrum(game, playerId, respond);
                default:
                    return false;
            }
        }

        /// <summary>
        /// A tantrum on a play that neither hurts the responder nor blocks a player about to end the game.
        /// Legal (rulebook p11) but it spends a card and grows the pile the Whiniest Baby's Timeout fine is charged on.
        /// </summary>
        public static bool PointlessTantrum(Game game, int playerId, RespondToWindow respond)
        {
            var state = game.State;
            if (respond.EquippedInstanceId != null || state.ResponseStack.Count == 0 ||
                !state.LemonInstances.TryGetValue(respond.CardInstanceId, out var instance) ||
                instance.DefId != LemonCardIds.Tantrum)
            {
                return false; // not a tantrum response: no opinion
            }
            var stackTop = state.ResponseStack[state.ResponseStack.Count - 1];
            if (stackTop.AttackTargetId == playerId)
            {
                return false;
            }
            // Counter-tantrum: cancelling a tantrum revives the responder's own play, and
            // cancelling a Tag / Rubber-Glue keeps the responder's own attack on course.
            if (stackTop.RespondingToItemId is int answeredItemId &&
                state.ResponseStack.Any(item => item.ItemId == answeredItemId && item.OwnerId == playerId))
            {
                return false;
            }
            // A player one VP from triggering the end: denying them anything is worth a card.
            if (state.Players[stackTop.OwnerId].InGameVictoryPoints >=
                game.Db.Config.VictoryPointsToTriggerEnd - 1)
            {
                return false;
            }
            // Expert Whiner WANTS played tantrums; spite is that lord's whole strategy.
            return !state.Players[playerId].LemonLordKept.Contains(LemonLordIds.ExpertWhiner);
        }

        /// <summary>
        /// Taxes / HOA Violation / Sharing Is Caring take min(nominal, the victim's cash):
        /// against $0 the transfer is a no-op. A $1-2 victim is a bad target, not a wasted one.
        /// </summary>
        private static bool WastedMoneyAttack(Game game, PlayLemonCard play)
        {
            if (!(play.TargetPlayerId is int victimId) ||
                victimId < 0 || victimId >= game.State.Players.Count ||
                !game.State.LemonInstances.TryGetValue(play.CardInstanceId, out var instance))
            {
                return false;
            }
            return GreedyBot.MoneyRecovered(instance.DefId, game.State.Players[victimId]) == 0;
        }

        private static bool WastedPlacement(Game game, int playerId, BuyBlackMarket purchase)
        {
            if (purchase.ReplaceInstanceId != null || // replacing may free the very number it re-adds
                purchase.MarketIndex >= game.State.Market.Count)
            {
                return false;
            }
            var me = game.State.Players[playerId];
            var def = game.Db.BlackMarket(
                game.State.BlackMarketInstances[game.State.Market[purchase.MarketIndex]].DefId);
            switch (def.Name)
            {
                case "Pushy Salesman":
                {
                    // A sale number the destination stand already sells on earns nothing.
                    var destination = me.Stands.FirstOrDefault(
                        stand => stand.InstanceId == purchase.TargetStandInstanceId);
                    return def.Number is int saleNumber && destination != null &&
                           game.SaleNumbersOf(destination).Contains(saleNumber);
                }
                case "Spiked Lemonade":
                    // A pour number we already cover adds nothing (Pour Master collects anyway).
                    return def.Number is int pourNumber && game.PourNumbersOf(me).Contains(pourNumber) &&
                           !me.LemonLordKept.Contains(LemonLordIds.PourMaster);
                default:
                    return false;
            }
        }

        /// <summary>Never empties the list.</summary>
        public static IReadOnlyList<GameAction> DropWasted(
            Game game, int playerId, IReadOnlyList<GameAction> moves)
        {
            var kept = moves.Where(move => !ObjectivelyWasted(game, playerId, move)).ToList();
            return kept.Count > 0 ? kept : moves;
        }
    }
}
