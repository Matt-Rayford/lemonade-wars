using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// How close a player is to each title, as a fraction in [0,1]. The engine only
    /// answers "met or not"; bots need the gradient so a half-built collection is
    /// worth something and the last piece is worth the most.
    /// </summary>
    public static class TitleProgress
    {
        public static double FirstDibs(Game game, PlayerState player, string titleId)
        {
            switch (titleId)
            {
                case "balanced-set":
                {
                    var shapes = ShapesOf(game, player);
                    return (Fraction(shapes.Count(shape => shape == Shape.Circle), 2)
                        + Fraction(shapes.Count(shape => shape == Shape.Diamond), 2)
                        + Fraction(shapes.Count(shape => shape == Shape.Square), 2)) / 3;
                }
                case "block-builder":
                    return Fraction(ShapesOf(game, player).Count(shape => shape == Shape.Square), 6);
                case "diamond-dealer":
                    return Fraction(ShapesOf(game, player).Count(shape => shape == Shape.Diamond), 6);
                case "full-circle":
                    return Fraction(ShapesOf(game, player).Count(shape => shape == Shape.Circle), 6);
                case "connoisseur":
                    return Fraction(CountStands(player, "gourmet"), 3) * 0.75
                        + (player.Stands.Any(stand => stand.StandTypeId != "gourmet") ? 0.25 : 0);
                case "local-legend":
                    return Fraction(CountStands(player, "classic"), 4);
                case "penny-pincher":
                    return Fraction(CountStands(player, "bargain"), 5);
                case "cranky-pants":
                    return Fraction(player.TantrumPile.Count, 3);
                case "expander":
                    return Fraction(player.Turf.Equipped.Count, 5);
                case "innovator":
                    return Fraction(player.Stands.Sum(stand => stand.Equipped.Count), 5);
                case "big-earner":
                    return Fraction(BestSingleRollEarnings(game, player), 9);
                case "stand-surge":
                    return Fraction(player.Stands.Count == 0 ? 0 : player.Stands.Max(stand =>
                        game.StandEarnings(player, stand) - game.Db.StandType(stand.StandTypeId).BaseEarnings), 3);
                default:
                    return 0; // one-roll feats (steals, double draws) are luck, not a plan
            }
        }

        /// <summary>Progress toward a Lemon Lord's end-game condition.</summary>
        public static double LemonLord(Game game, PlayerState player, string lordId)
        {
            if (game.MeetsLemonLord(player, lordId))
            {
                return 1;
            }
            var equipped = EquippedDefs(game, player).ToList();
            switch (lordId)
            {
                case "bare-bones":
                    return Fraction(player.Stands.Count(stand => stand.Equipped.Count == 0), 3);
                case "budget-beast":
                    return Fraction(player.Stands
                        .Where(stand => stand.StandTypeId == "bargain")
                        .Select(stand => game.StandEarnings(player, stand))
                        .DefaultIfEmpty(0).Max(), 3);
                case "elite-squeezer":
                    return Fraction(player.Stands
                        .Where(stand => stand.StandTypeId == "gourmet")
                        .Select(stand => stand.Equipped.Count)
                        .DefaultIfEmpty(0).Max(), 4);
                case "expert-whiner":
                    return ExpertWhinerProgress(game, player);
                case "friendly-fran":
                    return 0; // met or not: a dirty pile is only cleaned by Apologize
                case "fuming-phil":
                    return Fraction(CountHand(game, player, LemonCardType.Attack), 5);
                case "pam-the-planner":
                    return Fraction(CountHand(game, player, LemonCardType.Plan), 5);
                case "hoarder":
                    return Fraction(player.Hand.Count, 10);
                case "meltdown-master":
                    return Fraction(player.Hand.Count(id =>
                        game.State.LemonInstances[id].DefId == "tantrum"), 3);
                case "cash-queen":
                    return Fraction(equipped.Count(def => def.Cost >= 6), 3);
                case "pour-master":
                    return Fraction(equipped.Count(def => def.Name == "Spiked Lemonade"), 2);
                case "pushover":
                    return Fraction(equipped.Count(def => def.Name == "Pushy Salesman"), 4);
                case "card-shark":
                    return Fraction(equipped.Count(def => def.Icons.Contains("draw")), 2);
                case "flow-master":
                    return Fraction(equipped.Count(def => def.Timing == EffectTiming.PowerPour), 4);
                case "no-bullies-allowed":
                    return Fraction(equipped.Count(def => def.Category == "Defense"), 2);
                case "profit-pusher":
                    return Fraction(equipped.Count(def => def.Icons.Contains("dollar")), 3);
                case "roll-wrangler":
                    return Fraction(equipped.Count(def => def.Category == "Roll Modification"), 3);
                case "sales-engine":
                    return Fraction(equipped.Count(def => def.Timing == EffectTiming.OnSale), 3);
                case "swindling-sammy":
                    return Fraction(equipped.Count(def => def.Icons.Contains("steal-card")), 2);
                case "thieving-tommy":
                    return Fraction(equipped.Count(def => def.Icons.Contains("steal-dollar")), 2);
                default:
                    return 0;
            }
        }

        /// <summary>The most any single die face would pay this player.</summary>
        public static int BestSingleRollEarnings(Game game, PlayerState player)
        {
            int best = 0;
            for (int face = 1; face <= game.Db.Config.SaleDieSides; face++)
            {
                int earned = player.Stands
                    .Where(stand => game.SaleNumbersOf(stand).Contains(face))
                    .Sum(stand => game.StandEarnings(player, stand));
                if (game.PourNumbersOf(player).Contains(face))
                {
                    earned += 1;
                }
                best = Math.Max(best, earned);
            }
            return best;
        }

        public static IEnumerable<BlackMarketCardDef> EquippedDefs(Game game, PlayerState player) =>
            player.Turf.Equipped
                .Concat(player.Stands.SelectMany(stand => stand.Equipped))
                .Select(id => game.Db.BlackMarket(game.State.BlackMarketInstances[id].DefId));

        private static double ExpertWhinerProgress(Game game, PlayerState player)
        {
            // The Baby goes to the biggest tantrum pile: progress is our pile against
            // the largest rival pile, which still needs one more to overtake.
            int biggestRival = game.State.Players
                .Where(rival => rival.PlayerId != player.PlayerId)
                .Select(rival => rival.TantrumPile.Count)
                .DefaultIfEmpty(0).Max();
            return Fraction(player.TantrumPile.Count, biggestRival + 1);
        }

        private static List<Shape> ShapesOf(Game game, PlayerState player)
        {
            var shapes = player.Stands.Select(stand => stand.Shape).ToList();
            shapes.AddRange(player.Turf.Equipped
                .Concat(player.Stands.SelectMany(stand => stand.Equipped))
                .Select(id => game.State.BlackMarketInstances[id].Shape));
            return shapes;
        }

        private static int CountStands(PlayerState player, string typeId) =>
            player.Stands.Count(stand => stand.StandTypeId == typeId);

        private static int CountHand(Game game, PlayerState player, LemonCardType type) =>
            player.Hand.Count(id => game.Db.Lemon(game.State.LemonInstances[id].DefId).Type == type);

        private static double Fraction(int have, int need) =>
            need <= 0 ? 1 : Math.Min(1.0, Math.Max(0, have) / (double)need);
    }
}
