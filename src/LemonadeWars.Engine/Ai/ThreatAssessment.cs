using System;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Who is winning, judged from public information only — the same read a human
    /// at the table has. Shared by every bot so that "attack the leader" means the
    /// same thing in the greedy policy, the planner, and the search evaluators.
    /// </summary>
    public static class ThreatAssessment
    {
        /// <summary>
        /// 20 of the 71 Lemon cards answer an attack (Tantrum, Tag, Rubber-Glue); this is the share
        /// that does not, so a victim holding n cards is defenseless with odds of roughly 0.72^n.
        /// </summary>
        private const double NonReactionCardShare = 0.72;

        /// <summary>
        /// Visible race position. VP dominate; board income is the future; cash is the
        /// present. A player at 2 VP is a bigger threat than one with $20 and a bare board.
        /// </summary>
        public static double PublicStanding(Game game, PlayerState player) =>
            player.InGameVictoryPoints * 10
            + ExpectedIncomePerRoll(game, player) * 2.5
            + player.Money * 0.4;

        public static double ExpectedIncomePerRoll(Game game, PlayerState player)
        {
            double income = 0;
            foreach (var stand in player.Stands)
            {
                income += game.StandEarnings(player, stand) * game.SaleNumbersOf(stand).Count / 6.0;
            }
            // A power pour pays $1 and fires every Power Pour card on the turf.
            int pourCards = player.Turf.Equipped.Count(id =>
                game.Db.BlackMarket(game.State.BlackMarketInstances[id].DefId).Timing == EffectTiming.PowerPour);
            income += game.PourNumbersOf(player).Count / 6.0 * (1 + pourCards * 1.5);
            return income;
        }

        /// <summary>Among the viewer's opponents; ties go to the lower seat.</summary>
        public static PlayerState Leader(Game game, int viewerId) =>
            game.State.Players
                .Where(p => p.PlayerId != viewerId)
                .OrderByDescending(p => PublicStanding(game, p))
                .ThenBy(p => p.PlayerId)
                .First();

        /// <summary>
        /// How much hurting this opponent helps the viewer, in [0,1]: 1 for the leading opponent,
        /// 0 for the one furthest behind, linear in between. Always 1 with a single opponent.
        /// </summary>
        public static double ThreatOf(Game game, int viewerId, PlayerState victim)
        {
            var rivals = game.State.Players.Where(p => p.PlayerId != viewerId).ToList();
            if (rivals.Count <= 1)
            {
                return 1.0;
            }
            double best = rivals.Max(p => PublicStanding(game, p));
            double worst = rivals.Min(p => PublicStanding(game, p));
            if (best - worst < 1e-9)
            {
                return 1.0;
            }
            return (PublicStanding(game, victim) - worst) / (best - worst);
        }

        public static bool IsAboutToTriggerEnd(Game game, PlayerState player) =>
            player.InGameVictoryPoints >= game.Db.Config.VictoryPointsToTriggerEnd - 1;

        /// <summary>From here on cash stops mattering unless it buys a VP now.</summary>
        public static bool IsNearGameEnd(Game game) =>
            game.State.Stage == GameStage.FinalRound ||
            game.State.Players.Any(p => IsAboutToTriggerEnd(game, p));

        /// <summary>Hand contents are hidden, so this is a deck-composition estimate from hand size alone.</summary>
        public static double UnansweredChance(PlayerState victim) =>
            Math.Pow(NonReactionCardShare, victim.Hand.Count);
    }
}
