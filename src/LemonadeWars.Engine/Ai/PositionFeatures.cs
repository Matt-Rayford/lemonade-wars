using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// The numbers a position is judged by, one row per player, all computed from the
    /// viewer's seat (rivals' secret lords are averaged over what they were dealt).
    /// <see cref="EvaluationWeights"/> is the matching coefficient vector, so the
    /// evaluator is a dot product and the arena can fit the weights to real outcomes.
    /// </summary>
    public static class PositionFeatures
    {
        private static readonly HashSet<string> ReactionDefIds = new HashSet<string>
        {
            "tantrum", "tag-youre-it", "im-rubber-youre-glue",
        };
        private const int TimeoutHandLimit = 10;
        private const int MaxCountedReactions = 2;

        /// <summary>Feature order; <see cref="EvaluationWeights.ToVector"/> follows it.</summary>
        public static readonly string[] Names =
        {
            nameof(EvaluationWeights.VictoryPoint),
            nameof(EvaluationWeights.LordMet),
            nameof(EvaluationWeights.LordProgress),
            nameof(EvaluationWeights.DibsProgress),
            nameof(EvaluationWeights.Money),
            nameof(EvaluationWeights.MoneyLate),
            nameof(EvaluationWeights.BraggingReach),
            nameof(EvaluationWeights.IncomePerRound),
            nameof(EvaluationWeights.IncomePerRoll),
            nameof(EvaluationWeights.CardInHand),
            nameof(EvaluationWeights.ReactionInHand),
            nameof(EvaluationWeights.HandOverflow),
            nameof(EvaluationWeights.TantrumInPile),
            nameof(EvaluationWeights.WhiniestBaby),
            nameof(EvaluationWeights.SpoiledRotten),
            nameof(EvaluationWeights.UpgradeBase),
            nameof(EvaluationWeights.UpgradeCost),
            nameof(EvaluationWeights.Stand),
            nameof(EvaluationWeights.Trapped),
            nameof(EvaluationWeights.SeatTempo),
            nameof(EvaluationWeights.ActionsLeft),
        };

        public static int Count => Names.Length;

        /// <summary>
        /// Penalty features are stored NEGATED, so every weight reads as "worth of one
        /// unit" and a fitter can start from positive priors throughout.
        /// </summary>
        public static double[] Extract(Game game, PlayerState player, int viewerId, double horizon)
        {
            var s = game.State;
            bool lateGame = ThreatAssessment.IsNearGameEnd(game);
            var (lordsMet, lordProgress) = Lords(game, player, viewerId);
            double income = ThreatAssessment.ExpectedIncomePerRoll(game, player);
            var (reactions, overflow) = Hand(game, player);
            var upgrades = TitleProgress.EquippedDefs(game, player).ToList();
            int n = s.Players.Count;
            return new[]
            {
                player.InGameVictoryPoints,
                lordsMet,
                lordProgress,
                DibsProgress(game, player),
                lateGame ? 0 : player.Money,
                lateGame ? player.Money : 0,
                BraggingReach(game, player),
                income * horizon,
                income,
                player.Hand.Count,
                reactions,
                -overflow,
                -(WantsTantrums(game, player) ? 0 : player.TantrumPile.Count),
                -(s.WhiniestBabyHolder == player.PlayerId && !player.LemonLordKept.Contains("expert-whiner") ? 1 : 0),
                s.SpoiledRottenHolder == player.PlayerId ? 1 : 0,
                upgrades.Count,
                upgrades.Sum(def => def.Cost),
                player.Stands.Count,
                -(player.Turf.TrapInstanceId != null ? 1 : 0),
                // Turns until this player acts again, as a share of the round: 0 for
                // the active player, nearly 1 for the one who just went.
                n <= 1 ? 0 : ((player.PlayerId - s.ActivePlayer + n) % n) / (double)n,
                player.PlayerId == s.ActivePlayer && s.Phase == TurnPhase.Play ? s.ActionsRemaining : 0,
            };
        }

        /// <summary>
        /// Kept lords are secret: the viewer scores their own picks; every rival is
        /// scored on the average of what they were dealt (each dealt title kept with
        /// probability kept/dealt) — the same guess a human can make from the setup.
        /// Progress is squared so finishing a lord beats dabbling in two.
        /// </summary>
        private static (double Met, double Progress) Lords(Game game, PlayerState player, int viewerId)
        {
            IReadOnlyList<string> titles = player.LemonLordKept;
            double weight = 1.0;
            if (player.PlayerId != viewerId && player.LemonLordDealt.Count > 0)
            {
                titles = player.LemonLordDealt;
                weight = (double)Math.Max(1, player.LemonLordKept.Count) / player.LemonLordDealt.Count;
            }
            double met = 0;
            double progress = 0;
            foreach (string lordId in titles)
            {
                double fraction = TitleProgress.LemonLord(game, player, lordId);
                if (fraction >= 1)
                {
                    met += 1;
                }
                else
                {
                    progress += fraction * fraction;
                }
            }
            return (met * weight, progress * weight);
        }

        private static double DibsProgress(Game game, PlayerState player)
        {
            double total = 0;
            foreach (string titleId in game.State.FirstDibsRow)
            {
                double fraction = TitleProgress.FirstDibs(game, player, titleId);
                total += fraction * fraction;
            }
            return total;
        }

        private static double BraggingReach(Game game, PlayerState player)
        {
            var prices = game.Db.Supporting.BraggingRightsPrices;
            int sold = game.State.BraggingRightsSold;
            if (sold >= prices.Count)
            {
                return 0;
            }
            double reach = Math.Min(1.0, player.Money / (double)prices[sold]);
            return reach * reach;
        }

        private static (int Reactions, int Overflow) Hand(Game game, PlayerState player)
        {
            int reactions = 0;
            foreach (int id in player.Hand)
            {
                if (ReactionDefIds.Contains(game.State.LemonInstances[id].DefId))
                {
                    reactions++;
                }
            }
            // A Timeout draw forces discards above the limit; a hoarder lord changes the sign.
            int overflow = player.Hand.Count > TimeoutHandLimit - 1 && !player.LemonLordKept.Contains("hoarder")
                ? player.Hand.Count - (TimeoutHandLimit - 1)
                : 0;
            return (Math.Min(reactions, MaxCountedReactions), overflow);
        }

        private static bool WantsTantrums(Game game, PlayerState player) =>
            player.LemonLordKept.Contains("expert-whiner") ||
            (game.State.FirstDibsRow.Contains("cranky-pants") && player.TantrumPile.Count < 3);
    }
}
