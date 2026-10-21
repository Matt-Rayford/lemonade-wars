using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    // Title awareness: the pulls that First Dibs titles and the bot's secret Lemon
    // Lords exert on stand, shape, and Black Market choices.
    public sealed partial class GreedyBot
    {
        /// <summary>
        /// Pull toward the stand type an unclaimed First Dibs title rewards (penny-pincher:
        /// 5 bargain, local-legend: 4 classic, connoisseur: 3 gourmet + 1 other). Grows with
        /// each matching stand owned, so a bot that starts a collection commits to it.
        /// </summary>
        private static double StandTitleBonus(Game game, PlayerState me, string standTypeId)
        {
            var row = game.State.FirstDibsRow;
            int Owned(string type) => me.Stands.Count(st => st.StandTypeId == type);
            double dibs = DibsStandMarginal(game, standTypeId, Owned(standTypeId) + 1);
            if (dibs > 0)
            {
                return dibs;
            }
            // Three gourmets down, all-gourmet board: the title's "1 other Stand".
            if (row.Contains("connoisseur") && standTypeId != "gourmet" &&
                Owned("gourmet") >= 3 && me.Stands.Count == Owned("gourmet"))
            {
                return 12;
            }
            // Kept lords with stand-flavored conditions pull the stand mix too.
            double lordBonus = 0;
            foreach (string lordId in UnmetLords(game, me))
            {
                if (lordId == "bare-bones")
                {
                    lordBonus += 4; // more stands = easier to keep 3 of them empty
                }
                else if (lordId == "budget-beast" && standTypeId == "bargain")
                {
                    lordBonus += 4;
                }
                else if (lordId == "elite-squeezer" && standTypeId == "gourmet" &&
                         !me.Stands.Any(st => st.StandTypeId == "gourmet"))
                {
                    lordBonus += 5;
                }
            }
            // The next stand comes off the top of its supply pile: its shape may feed
            // an unclaimed First Dibs shape title.
            if (game.State.StandSupply.TryGetValue(standTypeId, out var pile) && pile.Count > 0)
            {
                lordBonus += ShapeTitleBonus(game, me, pile[0]);
            }
            return lordBonus;
        }

        // -------------------------------------------- Lemon Lord awareness

        private static IEnumerable<string> UnmetLords(Game game, PlayerState me) =>
            me.LemonLordKept.Where(id => !game.MeetsLemonLord(me, id));

        /// <summary>Does equipping this card advance the counted condition of a lord?</summary>
        private static bool LordWantsEquip(string lordId, BlackMarketCardDef def)
        {
            switch (lordId)
            {
                case "pour-master": return def.Name == "Spiked Lemonade";
                case "pushover": return def.Name == "Pushy Salesman";
                case "cash-queen": return def.Cost >= 6;
                case "card-shark": return def.Icons.Contains("draw");
                case "flow-master": return def.Timing == EffectTiming.PowerPour;
                case "no-bullies-allowed": return def.Category == "Defense";
                case "profit-pusher": return def.Icons.Contains("dollar");
                case "roll-wrangler": return def.Category == "Roll Modification";
                case "sales-engine": return def.Timing == EffectTiming.OnSale;
                case "swindling-sammy": return def.Icons.Contains("steal-card");
                case "thieving-tommy": return def.Icons.Contains("steal-dollar");
                default: return false;
            }
        }

        /// <summary>
        /// A kept lord is a full VP at game end: acquiring an equip that advances one
        /// (buy, steal, Connections take) earns a strong pull, destination-aware for
        /// the stand-specific lords, with a brake for Bare Bones boards.
        /// </summary>
        private static double LordEquipBonus(Game game, PlayerState me, BlackMarketCardDef def,
            int? destinationStandId)
        {
            double bonus = 0;
            foreach (string lordId in UnmetLords(game, me))
            {
                if (LordWantsEquip(lordId, def))
                {
                    bonus += 9;
                }
                switch (lordId)
                {
                    case "elite-squeezer":
                        if (destinationStandId is int gourmetDest && me.Stands.Any(st =>
                                st.InstanceId == gourmetDest && st.StandTypeId == "gourmet"))
                        {
                            bonus += 6; // 4 equips on one gourmet stand
                        }
                        break;
                    case "budget-beast":
                        if ((def.Id == "diluted-lemonade" || def.Id == "pink-lemonade") &&
                            destinationStandId is int bargainDest && me.Stands.Any(st =>
                                st.InstanceId == bargainDest && st.StandTypeId == "bargain"))
                        {
                            bonus += 8; // a bargain stand earning $3+
                        }
                        break;
                    case "bare-bones":
                        if (def.Target == EquipTarget.Stand &&
                            me.Stands.Count(st => st.Equipped.Count == 0) <= 3)
                        {
                            bonus -= 7; // equipping would spend a needed empty stand
                        }
                        break;
                }
            }
            return bonus;
        }

        /// <summary>
        /// First Dibs shape titles (6 squares/diamonds/circles, 2 of each): once a
        /// collection is started, matching shapes get a growing pull.
        /// </summary>
        private static double ShapeTitleBonus(Game game, PlayerState me, Shape? shape)
        {
            if (!(shape is Shape wanted))
            {
                return 0;
            }
            var row = game.State.FirstDibsRow;
            int owned = ShapeCount(game, me, wanted);
            double bonus = 0;
            string title = wanted == Shape.Square ? "block-builder"
                : wanted == Shape.Diamond ? "diamond-dealer" : "full-circle";
            if (row.Contains(title) && owned >= 2 && owned < 6)
            {
                bonus += 1.5 + owned;
            }
            if (row.Contains("balanced-set") && owned < 2)
            {
                bonus += 3;
            }
            return bonus;
        }

        private static int ShapeCount(Game game, PlayerState me, Shape shape)
        {
            int count = me.Stands.Count(st => st.Shape == shape);
            foreach (int id in me.Turf.Equipped)
            {
                if (game.State.BlackMarketInstances[id].Shape == shape)
                {
                    count++;
                }
            }
            foreach (var stand in me.Stands)
            {
                foreach (int id in stand.Equipped)
                {
                    if (game.State.BlackMarketInstances[id].Shape == shape)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        /// <summary>Static preference between Lemon Lord titles at the keep-2 choice.</summary>
        private static double LordAffinity(string titleId)
        {
            switch (titleId)
            {
                case "friendly-fran": return 9;  // passive: just avoid tantrums
                case "hoarder": return 8;        // drawing is always available
                case "bare-bones": return 7;
                case "expert-whiner": return 3;  // conflicts with friendly play
                case "elite-squeezer": return 4;
                default: return 5;
            }
        }
    }
}
