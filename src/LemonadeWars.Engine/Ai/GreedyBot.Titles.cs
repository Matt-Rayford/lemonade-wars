using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    public sealed partial class GreedyBot
    {
        /// <summary>
        /// Pull toward the stand type an unclaimed First Dibs title rewards. Grows with
        /// each matching stand owned, so a bot that starts a collection commits to it.
        /// </summary>
        private static double StandTitleBonus(Game game, PlayerState me, string standTypeId)
        {
            double dibs = DibsStandMarginal(game, standTypeId, OwnedStands(me, standTypeId) + 1);
            if (dibs > 0)
            {
                return dibs;
            }
            // Three gourmets down, all-gourmet board: the title's "1 other Stand".
            if (game.State.FirstDibsRow.Contains(FirstDibsIds.Connoisseur) && standTypeId != StandTypeIds.Gourmet &&
                OwnedStands(me, StandTypeIds.Gourmet) >= 3 && me.Stands.Count == OwnedStands(me, StandTypeIds.Gourmet))
            {
                return 12;
            }
            double lordBonus = 0;
            foreach (string lordId in UnmetLords(game, me))
            {
                if (lordId == LemonLordIds.BareBones)
                {
                    lordBonus += 4; // more stands = easier to keep 3 of them empty
                }
                else if (lordId == LemonLordIds.BudgetBeast && standTypeId == StandTypeIds.Bargain)
                {
                    lordBonus += 4;
                }
                else if (lordId == LemonLordIds.EliteSqueezer && standTypeId == StandTypeIds.Gourmet &&
                         !me.Stands.Any(stand => stand.StandTypeId == StandTypeIds.Gourmet))
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

        private static int OwnedStands(PlayerState me, string standTypeId) =>
            me.Stands.Count(stand => stand.StandTypeId == standTypeId);

        /// <summary>How many stands of a type an unclaimed First Dibs title wants (0 = none on offer).</summary>
        private static int DibsStandGoal(Game game, string standTypeId)
        {
            var row = game.State.FirstDibsRow;
            if (standTypeId == StandTypeIds.Bargain && row.Contains(FirstDibsIds.PennyPincher)) return 5;
            if (standTypeId == StandTypeIds.Classic && row.Contains(FirstDibsIds.LocalLegend)) return 4;
            if (standTypeId == StandTypeIds.Gourmet && row.Contains(FirstDibsIds.Connoisseur)) return 3;
            return 0;
        }

        /// <summary>
        /// Worth of the <paramref name="nth"/> stand of a type toward an unclaimed First Dibs title:
        /// growing, so a started collection gets finished; zero once the title's count is covered.
        /// </summary>
        private static double DibsStandMarginal(Game game, string standTypeId, int nth)
        {
            int goal = DibsStandGoal(game, standTypeId);
            return goal == 0 || nth < 1 || nth > goal ? 0 : 6 + (nth - 1) * 4;
        }

        private static double RebrandDibsDelta(Game game, PlayerState me, string fromType, string toType)
        {
            // Connoisseur wants 3 Gourmet AND 1 other: on an all-gourmet board the spare
            // gourmet is exactly the stand that should be converted.
            if (game.State.FirstDibsRow.Contains(FirstDibsIds.Connoisseur) && fromType == StandTypeIds.Gourmet &&
                toType != StandTypeIds.Gourmet && OwnedStands(me, StandTypeIds.Gourmet) >= 4 &&
                me.Stands.Count == OwnedStands(me, StandTypeIds.Gourmet))
            {
                return 12;
            }
            return DibsStandMarginal(game, toType, OwnedStands(me, toType) + 1)
                - DibsStandMarginal(game, fromType, OwnedStands(me, fromType));
        }

        // ---- Lemon Lord awareness

        private static IEnumerable<string> UnmetLords(Game game, PlayerState me) =>
            me.LemonLordKept.Where(id => !game.MeetsLemonLord(me, id));

        private static bool LordWantsEquip(string lordId, BlackMarketCardDef def)
        {
            switch (lordId)
            {
                case LemonLordIds.PourMaster: return def.Name == "Spiked Lemonade";
                case LemonLordIds.Pushover: return def.Name == "Pushy Salesman";
                case LemonLordIds.CashQueen: return def.Cost >= 6;
                case LemonLordIds.CardShark: return def.Icons.Contains("draw");
                case LemonLordIds.FlowMaster: return def.Timing == EffectTiming.PowerPour;
                case LemonLordIds.NoBulliesAllowed: return def.Category == "Defense";
                case LemonLordIds.ProfitPusher: return def.Icons.Contains("dollar");
                case LemonLordIds.RollWrangler: return def.Category == "Roll Modification";
                case LemonLordIds.SalesEngine: return def.Timing == EffectTiming.OnSale;
                case LemonLordIds.SwindlingSammy: return def.Icons.Contains("steal-card");
                case LemonLordIds.ThievingTommy: return def.Icons.Contains("steal-dollar");
                default: return false;
            }
        }

        /// <summary>
        /// A kept lord is a full VP at game end, so an equip that advances one earns a strong pull:
        /// destination-aware for the stand-specific lords, with a brake for Bare Bones boards.
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
                    case LemonLordIds.EliteSqueezer:
                        if (destinationStandId is int gourmetStandId && me.Stands.Any(stand =>
                                stand.InstanceId == gourmetStandId && stand.StandTypeId == StandTypeIds.Gourmet))
                        {
                            bonus += 6; // 4 equips on one gourmet stand
                        }
                        break;
                    case LemonLordIds.BudgetBeast:
                        if ((def.Id == BlackMarketCardIds.DilutedLemonade || def.Id == BlackMarketCardIds.PinkLemonade) &&
                            destinationStandId is int bargainStandId && me.Stands.Any(stand =>
                                stand.InstanceId == bargainStandId && stand.StandTypeId == StandTypeIds.Bargain))
                        {
                            bonus += 8; // a bargain stand earning $3+
                        }
                        break;
                    case LemonLordIds.BareBones:
                        if (def.Target == EquipTarget.Stand &&
                            me.Stands.Count(stand => stand.Equipped.Count == 0) <= 3)
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
            string title = wanted == Shape.Square ? FirstDibsIds.BlockBuilder
                : wanted == Shape.Diamond ? FirstDibsIds.DiamondDealer : FirstDibsIds.FullCircle;
            if (row.Contains(title) && owned >= 2 && owned < 6)
            {
                bonus += 1.5 + owned;
            }
            if (row.Contains(FirstDibsIds.BalancedSet) && owned < 2)
            {
                bonus += 3;
            }
            return bonus;
        }

        private static int ShapeCount(Game game, PlayerState me, Shape shape)
        {
            int count = me.Stands.Count(stand => stand.Shape == shape);
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
                case LemonLordIds.FriendlyFran: return 9;  // passive: just avoid tantrums
                case LemonLordIds.Hoarder: return 8;        // drawing is always available
                case LemonLordIds.BareBones: return 7;
                case LemonLordIds.ExpertWhiner: return 3;  // conflicts with friendly play
                case LemonLordIds.EliteSqueezer: return 4;
                default: return 5;
            }
        }
    }
}
