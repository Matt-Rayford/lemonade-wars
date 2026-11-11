using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    public sealed partial class GreedyBot
    {
        /// <summary>
        /// Classic first for the mid-range economy, then bargain for frequency — unless
        /// an unclaimed First Dibs stand title tips the scales.
        /// </summary>
        private static double ScoreDraftStand(Game game, PlayerState me, InitialBuyStand draftStand) =>
            (draftStand.StandTypeId == "classic" ? 60
                : draftStand.StandTypeId == "bargain" ? 55 : 50)
                + StandTitleBonus(game, me, draftStand.StandTypeId);

        private static double ScoreBraggingRights(Game game, PlayerState me, bool isLastLap)
        {
            int price = game.Db.Supporting.BraggingRightsPrices[game.State.BraggingRightsSold];
            if (me.InGameVictoryPoints >= 2)
            {
                return 500; // closes out the game trigger
            }
            // In the last lap cash has no future: a VP now beats any economy.
            return isLastLap || me.Money - price >= 5 ? 90 : 40;
        }

        private static double ScoreStand(Game game, PlayerState me, BuyStand buyStand, bool isLastLap)
        {
            if (me.Stands.Count >= 5)
            {
                return 8;
            }
            double typeValue = buyStand.StandTypeId == "gourmet" ? 46
                : buyStand.StandTypeId == "classic" ? 48 : 44;
            return typeValue - me.Stands.Count * 4 + StandTitleBonus(game, me, buyStand.StandTypeId)
                - (isLastLap ? 10 : 0);
        }

        private static double ScoreBlackMarketPurchase(Game game, PlayerState me, BuyBlackMarket purchase)
        {
            var state = game.State;
            var instance = state.BlackMarketInstances[state.Market[purchase.MarketIndex]];
            var def = game.Db.BlackMarket(instance.DefId);
            double value = 30 + BlackMarketAffinity(game, me, def, purchase) - def.Cost * 0.5
                + LordEquipBonus(game, me, def, purchase.TargetStandInstanceId)
                + ShapeTitleBonus(game, me, instance.Shape);
            // Replacing an existing card is usually a downgrade path.
            if (purchase.ReplaceInstanceId != null)
            {
                value -= 15;
            }
            return value;
        }

        /// <summary>Rough desirability of a Black Market card beyond its sticker price.</summary>
        private static double BlackMarketAffinity(Game game, PlayerState me, BlackMarketCardDef def,
            BuyBlackMarket purchase)
        {
            switch (def.Name)
            {
                case "Pushy Salesman":
                {
                    // A sale number the target stand already sells on earns nothing.
                    var targetStand = me.Stands.FirstOrDefault(
                        stand => stand.InstanceId == purchase.TargetStandInstanceId);
                    if (def.Number is int saleNumber && targetStand != null &&
                        game.SaleNumbersOf(targetStand).Contains(saleNumber))
                    {
                        return -25;
                    }
                    return 8;   // more sale numbers = more income
                }
                case "Spiked Lemonade":
                {
                    // A pour number we already cover adds nothing — unless we secretly
                    // hold Pour Master (end with 2+ Spiked Lemonades) and collect anyway.
                    if (def.Number is int pourNumber && game.PourNumbersOf(me).Contains(pourNumber))
                    {
                        return me.LemonLordKept.Contains("pour-master") ? 5 : -25;
                    }
                    return 7;
                }
                default:
                    switch (def.Timing)
                    {
                        case EffectTiming.OnSale: return 6;
                        case EffectTiming.PowerPour: return 4;
                        case EffectTiming.OnYourTurn: return 3;
                        default: return def.Category == "Defense" ? 5 : 2;
                    }
            }
        }

        private static double ScoreDraw(Game game, PlayerState me, bool isLastLap)
        {
            double draw = me.Hand.Count < 7 ? 25 : 12;
            bool feedsHoarder = false;
            foreach (string lordId in UnmetLords(game, me))
            {
                if (lordId == "hoarder")
                {
                    draw = 32; // needs 10 cards in hand — keep drawing
                    feedsHoarder = true;
                }
                else if (lordId == "fuming-phil" || lordId == "pam-the-planner" ||
                         lordId == "meltdown-master")
                {
                    draw += 5; // fishing for the counted card type
                }
            }
            // A card drawn in the last lap rarely gets played before the end.
            return isLastLap && !feedsHoarder ? draw - 10 : draw;
        }
    }
}
