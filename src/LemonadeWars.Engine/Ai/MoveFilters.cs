using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Moves no human would ever consider — strictly dominated placements of
    /// numbered upgrades, and money attacks that would move $0. The greedy policy
    /// already avoids them via affinities, but SEARCH bots must exclude them up
    /// front: when a turn has fewer legal moves than the candidate cap, everything
    /// gets rolled out, and rollout noise can rank a wasted move above its sensible
    /// twin (a few dollars barely moves a win-probability estimate).
    /// </summary>
    public static class MoveFilters
    {
        public static bool ObjectivelyWasted(Game game, int playerId, GameAction move)
        {
            switch (move)
            {
                case PlayLemonCard play:
                    return WastedMoneyAttack(game, play);
                case BuyBlackMarket bm:
                    return WastedPlacement(game, playerId, bm);
                case RespondToWindow respond:
                    return PointlessTantrum(game, playerId, respond);
                default:
                    return false;
            }
        }

        /// <summary>
        /// A tantrum aimed at a play that neither hurts the responder nor blocks a player
        /// about to trigger the end of the game. Legal — the rules let ANY player tantrum
        /// any tantrummable play (rulebook p11) — but strictly bad value: it spends a card,
        /// grows the responder's tantrum pile toward the Whiniest Baby ($3-per-tantrum
        /// Timeout fine), and buys them nothing. Cancelling an attack on yourself, counter-
        /// tantruming to revive your own play, and denying a 2-VP player all stay in.
        /// </summary>
        public static bool PointlessTantrum(Game game, int playerId, RespondToWindow respond)
        {
            var s = game.State;
            if (respond.EquippedInstanceId != null || s.ResponseStack.Count == 0 ||
                !s.LemonInstances.TryGetValue(respond.CardInstanceId, out var instance) ||
                instance.DefId != "tantrum")
            {
                return false; // not a tantrum response — no opinion
            }
            var top = s.ResponseStack[s.ResponseStack.Count - 1];
            // Cancelling an attack on yourself is the tantrum's day job.
            if (top.AttackTargetId == playerId)
            {
                return false;
            }
            // Counter-tantrum: cancelling this tantrum revives the responder's own play.
            if (top.LemonDefId == "tantrum" && top.RespondingToItemId is int prevId &&
                s.ResponseStack.Any(it => it.ItemId == prevId && it.OwnerId == playerId))
            {
                return false;
            }
            // A player one VP from triggering the end: denying them anything is worth a card.
            if (s.Players[top.OwnerId].InGameVictoryPoints >=
                game.Db.Config.VictoryPointsToTriggerEnd - 1)
            {
                return false;
            }
            // Expert Whiner WANTS played tantrums; spite is that lord's whole strategy.
            if (s.Players[playerId].LemonLordKept.Contains("expert-whiner"))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Taxes / HOA Violation / Sharing Is Caring all take min(nominal, the victim's
        /// cash). Against a victim holding $0 the transfer is literally a no-op, so the
        /// action buys nothing. A $1-2 victim is merely a bad target, not a wasted one.
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

        private static bool WastedPlacement(Game game, int playerId, BuyBlackMarket bm)
        {
            if (bm.ReplaceInstanceId != null || // replacing may free the very number it re-adds
                bm.MarketIndex >= game.State.Market.Count)
            {
                return false;
            }
            var me = game.State.Players[playerId];
            var def = game.Db.BlackMarket(
                game.State.BlackMarketInstances[game.State.Market[bm.MarketIndex]].DefId);
            switch (def.Name)
            {
                case "Pushy Salesman":
                {
                    // A sale number the destination stand already sells on earns nothing.
                    var stand = me.Stands.FirstOrDefault(st => st.InstanceId == bm.TargetStandInstanceId);
                    return def.Number is int sale && stand != null &&
                           game.SaleNumbersOf(stand).Contains(sale);
                }
                case "Spiked Lemonade":
                    // A pour number we already cover adds nothing (Pour Master collects anyway).
                    return def.Number is int pour && game.PourNumbersOf(me).Contains(pour) &&
                           !me.LemonLordKept.Contains("pour-master");
                default:
                    return false;
            }
        }

        /// <summary>Drop wasted moves; never empties the list.</summary>
        public static IReadOnlyList<GameAction> DropWasted(
            Game game, int playerId, IReadOnlyList<GameAction> moves)
        {
            var kept = moves.Where(m => !ObjectivelyWasted(game, playerId, m)).ToList();
            return kept.Count > 0 ? kept : moves;
        }
    }
}
