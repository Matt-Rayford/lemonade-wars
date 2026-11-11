using System;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    public sealed partial class GreedyBot
    {
        private static double ScorePlay(Game game, PlayerState me, PlayLemonCard play)
        {
            string defId = game.State.LemonInstances[play.CardInstanceId].DefId;
            var victim = AttackVictim(game, play);
            double victimFactor = victim == null ? 1 : AttackTargetFactor(game, me, victim);
            switch (defId)
            {
                case "automation":
                    return me.Money >= 5 ? 42 : 15;
                case "market-forecasting":
                    return 30;
                case "night-shifts":
                    return 28;
                case "doorbuster-sale":
                    return ScoreDoorbusterSale(game, me, play);
                case "hoa-violation":
                case "sharing-is-caring":
                case "taxes":
                    return victim == null ? 0 : MoneyAttackScore(game, defId, victim) * victimFactor;
                case "smear-campaign":
                    return victim == null ? 0 : (20 + Math.Min(2, victim.Hand.Count) * 6) * victimFactor;
                case "trash-pandas":
                    return victim == null ? 0 : Math.Max(0, victim.Hand.Count - me.Hand.Count) * 6 * victimFactor;
                case "steal-the-cashbox":
                    return victim == null ? 0 : CashboxTrapScore(game, victim) * victimFactor;
                case "thats-not-fair":
                    return (22 + StolenValue(game, me, play.TargetEquippedInstanceId, play.EquipStandInstanceId))
                        * victimFactor;
                case "finders-keepers":
                    return (26 + StolenValue(game, me, play.TargetEquippedInstanceId, play.EquipStandInstanceId))
                        * victimFactor;
                case "connections":
                    return 44; // a free Black Market card
                case "reduce-and-reuse":
                    return 36;
                case "reverse-engineer":
                    return ScoreReverseEngineer(game, me, play);
                case "rummage-sale":
                    return 6; // usually better to keep upgrades
                case "rebrand":
                    return ScoreRebrand(game, me, play);
                case "apologize":
                    return ScoreApologize(game, me);
                case "blame-changer":
                    return ScoreBlameChanger(game, me, victimFactor);
                default:
                    return 10;
            }
        }

        private static double ScoreDoorbusterSale(Game game, PlayerState me, PlayLemonCard play)
        {
            var sellingStand = me.Stands.First(stand => stand.InstanceId == play.TargetStandInstanceId);
            return 20 + game.StandEarnings(me, sellingStand) * 4;
        }

        /// <summary>
        /// A trap on the victim's NEXT roll, not their wallet: what matters is how much
        /// their stands earn when they sell, capped at $10.
        /// </summary>
        private static double CashboxTrapScore(Game game, PlayerState victim)
        {
            double earningsPerRoll = victim.Stands.Sum(
                stand => game.StandEarnings(victim, stand) * game.SaleNumbersOf(stand).Count) / 6.0;
            return 18 + Math.Min(earningsPerRoll, 10) * 3;
        }

        /// <summary>Recovering is worth what the recovered card is worth to us.</summary>
        private static double ScoreReverseEngineer(Game game, PlayerState me, PlayLemonCard play) =>
            play.DrawInstead || !(play.DiscardedLemonInstanceId is int recoveredId)
                ? 24
                : 22 + CardValue(game, me, recoveredId) * 2;

        /// <summary>
        /// Only worth an action when it moves us TOWARD an unclaimed First Dibs
        /// collection; walking a stand out of one we're partway through is a setback.
        /// Overflow upgrades get discarded.
        /// </summary>
        private static double ScoreRebrand(Game game, PlayerState me, PlayLemonCard play)
        {
            var rebranded = me.Stands.FirstOrDefault(stand => stand.InstanceId == play.TargetStandInstanceId);
            if (rebranded == null)
            {
                return 10;
            }
            return 10 + RebrandDibsDelta(game, me, rebranded.StandTypeId, play.NewStandTypeId)
                - play.SelectedInstanceIds.Count * 5;
        }

        private static double ScoreApologize(Game game, PlayerState me)
        {
            // Expert Whiner WANTS to end the game as the Whiniest Baby.
            if (me.LemonLordKept.Contains("expert-whiner"))
            {
                return 4;
            }
            return game.State.WhiniestBabyHolder == me.PlayerId ? 35 : 8;
        }

        private static double ScoreBlameChanger(Game game, PlayerState me, double victimFactor)
        {
            if (me.LemonLordKept.Contains("expert-whiner"))
            {
                return 5;
            }
            return (game.State.WhiniestBabyHolder == me.PlayerId ? 40 : 12) * victimFactor;
        }

        /// <summary>
        /// The player an attack lands on. That's Not Fair! names only the card, so its
        /// victim is whoever has that card equipped.
        /// </summary>
        private static PlayerState? AttackVictim(Game game, PlayLemonCard play)
        {
            if (play.TargetPlayerId is int targetId)
            {
                return game.State.Players[targetId];
            }
            return play.TargetEquippedInstanceId is int equippedId
                ? OwnerOfEquipped(game, equippedId)
                : null;
        }

        private static PlayerState? OwnerOfEquipped(Game game, int equippedId) =>
            game.State.Players.FirstOrDefault(player =>
                player.Turf.Equipped.Contains(equippedId) ||
                player.Stands.Any(stand => stand.Equipped.Contains(equippedId)));

        /// <summary>
        /// An attack's worth on this victim relative to the ideal victim, in (0,1]: the
        /// leader at full value down to 0.55 for the player furthest behind, then
        /// discounted by the odds a reaction card answers it.
        /// </summary>
        private static double AttackTargetFactor(Game game, PlayerState me, PlayerState victim) =>
            (0.55 + 0.45 * ThreatAssessment.ThreatOf(game, me.PlayerId, victim))
            * (0.6 + 0.4 * ThreatAssessment.UnansweredChance(victim));

        /// <summary>
        /// Dollars a money-stealing attack would ACTUALLY move: the nominal steal capped
        /// by the victim's cash, exactly as <c>StealMoney</c> caps it. Null for cards
        /// that never touch a player's cash pile.
        /// </summary>
        public static int? MoneyRecovered(string defId, PlayerState victim)
        {
            switch (defId)
            {
                case "hoa-violation":
                    return Math.Min(5, victim.Money);
                case "taxes":
                    return Math.Min(Math.Min(2 * victim.Stands.Count, 10), victim.Money);
                case "sharing-is-caring":
                    return Math.Min((victim.Money + 1) / 2, 10);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Scored on what would ACTUALLY change hands: nothing at $0, top-tier at the
        /// $10 cap. Cash held by a player one VP from ending the game buys the last VP,
        /// so denying it is worth extra.
        /// </summary>
        private static double MoneyAttackScore(Game game, string defId, PlayerState victim)
        {
            int recovered = MoneyRecovered(defId, victim) ?? 0;
            if (recovered <= 0)
            {
                return 0;
            }
            double denial = ThreatAssessment.IsAboutToTriggerEnd(game, victim) ? 8 : 0;
            return 6 + recovered * 3.8 + denial;
        }

        /// <summary>
        /// How much the equipped card a steal would take is worth TO US — so the bot grabs
        /// the Diluted Lemonade, not whatever enumerates first.
        /// </summary>
        private static double StolenValue(Game game, PlayerState me, int? targetEquippedId, int? equipStandId)
        {
            if (!(targetEquippedId is int equippedId) ||
                !game.State.BlackMarketInstances.TryGetValue(equippedId, out var instance))
            {
                return 6;
            }
            var def = game.Db.BlackMarket(instance.DefId);
            double lordPull = LordEquipBonus(game, me, def, equipStandId) +
                              ShapeTitleBonus(game, me, instance.Shape);
            switch (def.Name)
            {
                case "Diluted Lemonade": return 12 + lordPull;   // flat +$2 on every sale
                case "Pushy Salesman": return 9 + lordPull;
                case "Spiked Lemonade":
                    return (def.Number is int pourNumber && game.PourNumbersOf(me).Contains(pourNumber) ? 3 : 9)
                        + lordPull;
                case "Pink Lemonade": return 7 + lordPull;
                default:
                    switch (def.Timing)
                    {
                        case EffectTiming.OnSale: return 8 + lordPull;
                        case EffectTiming.PowerPour: return 6 + lordPull;
                        case EffectTiming.OnYourTurn: return 5 + lordPull;
                        default: return (def.Category == "Defense" ? 6 : 4) + lordPull;
                    }
            }
        }
    }
}
