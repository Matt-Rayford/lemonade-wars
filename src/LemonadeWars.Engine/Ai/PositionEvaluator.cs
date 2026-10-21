using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Static evaluation of a position from one player's seat: how good is this state
    /// for me, relative to my rivals? One scale for everything — VP, titles in
    /// progress, cash, board income, hand — so that plans built from different move
    /// types compare honestly, which a per-move score table never managed.
    /// </summary>
    public sealed class PositionEvaluator
    {
        private static readonly HashSet<string> ReactionDefIds = new HashSet<string>
        {
            "tantrum", "tag-youre-it", "im-rubber-youre-glue",
        };
        private const int TimeoutHandLimit = 10;
        private const int MaxCountedReactions = 2;

        public EvaluationWeights Weights { get; }

        public PositionEvaluator(EvaluationWeights? weights = null)
        {
            Weights = weights ?? EvaluationWeights.Default;
        }

        /// <summary>My score minus a blend of the strongest rival and the rest of the field.</summary>
        public double Evaluate(Game game, int viewerId)
        {
            var state = game.State;
            if (state.Stage == GameStage.Finished)
            {
                return FinalResult(game, viewerId);
            }
            double horizon = RemainingRounds(game);
            double mine = 0;
            double bestRival = double.MinValue;
            double fieldTotal = 0;
            int rivals = 0;
            foreach (var player in state.Players)
            {
                double score = PlayerScore(game, player, viewerId, horizon);
                if (player.PlayerId == viewerId)
                {
                    mine = score;
                    continue;
                }
                rivals++;
                fieldTotal += score;
                bestRival = Math.Max(bestRival, score);
            }
            if (rivals == 0)
            {
                return mine;
            }
            double field = fieldTotal / rivals;
            return mine - Weights.Rival * bestRival - Weights.Field * field;
        }

        /// <summary>Absolute worth of one player's position; public so tests can probe terms.</summary>
        public double PlayerScore(Game game, PlayerState player, int viewerId, double horizon)
        {
            var w = Weights;
            bool lateGame = ThreatAssessment.IsNearGameEnd(game);
            double score = player.InGameVictoryPoints * w.VictoryPoint;
            score += LordsValue(game, player, viewerId);
            score += DibsValue(game, player);
            score += player.Money * (lateGame ? w.MoneyLate : w.Money);
            score += BraggingReach(game, player);
            score += ThreatAssessment.ExpectedIncomePerRoll(game, player) * w.IncomePerRound * horizon;
            score += HandValue(game, player);
            score += BoardValue(game, player);
            score -= player.TantrumPile.Count * w.TantrumInPile * (WantsTantrums(game, player) ? 0 : 1);
            if (game.State.WhiniestBabyHolder == player.PlayerId && !player.LemonLordKept.Contains("expert-whiner"))
            {
                score -= w.WhiniestBaby;
            }
            if (game.State.SpoiledRottenHolder == player.PlayerId)
            {
                score += w.SpoiledRotten;
            }
            if (player.Turf.TrapInstanceId != null)
            {
                score -= w.Trapped;
            }
            return score;
        }

        /// <summary>
        /// Rounds the table will still play, so income can be priced. Shrinks as the
        /// leaders close in on the trigger; the last lap is worth about one roll.
        /// </summary>
        public static double RemainingRounds(Game game)
        {
            if (ThreatAssessment.IsNearGameEnd(game))
            {
                return 1.5;
            }
            int maxVp = game.State.Players.Max(p => p.InGameVictoryPoints);
            return Math.Max(3.0, 9.0 - 2.5 * maxVp);
        }

        /// <summary>
        /// Kept lords are secret: the viewer scores their own picks, and every rival is
        /// scored on the average of what they were dealt (each dealt title is kept with
        /// probability kept/dealt) — the same guess a human can make from the setup.
        /// </summary>
        private double LordsValue(Game game, PlayerState player, int viewerId)
        {
            IReadOnlyList<string> titles = player.LemonLordKept;
            double weight = 1.0;
            if (player.PlayerId != viewerId && player.LemonLordDealt.Count > 0)
            {
                titles = player.LemonLordDealt;
                weight = (double)Math.Max(1, player.LemonLordKept.Count) / player.LemonLordDealt.Count;
            }
            double value = 0;
            foreach (string lordId in titles)
            {
                double progress = TitleProgress.LemonLord(game, player, lordId);
                value += progress >= 1
                    ? Weights.LordMet
                    : progress * progress * Weights.LordProgress;
            }
            return value * weight;
        }

        private double DibsValue(Game game, PlayerState player)
        {
            double value = 0;
            foreach (string titleId in game.State.FirstDibsRow)
            {
                double progress = TitleProgress.FirstDibs(game, player, titleId);
                value += progress * progress * Weights.DibsProgress;
            }
            return value;
        }

        private double BraggingReach(Game game, PlayerState player)
        {
            var prices = game.Db.Supporting.BraggingRightsPrices;
            int sold = game.State.BraggingRightsSold;
            if (sold >= prices.Count)
            {
                return 0;
            }
            double reach = Math.Min(1.0, player.Money / (double)prices[sold]);
            return reach * reach * Weights.BraggingReach;
        }

        private double HandValue(Game game, PlayerState player)
        {
            int reactions = 0;
            foreach (int id in player.Hand)
            {
                if (ReactionDefIds.Contains(game.State.LemonInstances[id].DefId))
                {
                    reactions++;
                }
            }
            double value = player.Hand.Count * Weights.CardInHand
                + Math.Min(reactions, MaxCountedReactions) * Weights.ReactionInHand;
            // A Timeout draw forces discards above the limit; a hoarder lord changes the sign.
            if (player.Hand.Count > TimeoutHandLimit - 1 && !player.LemonLordKept.Contains("hoarder"))
            {
                value -= (player.Hand.Count - (TimeoutHandLimit - 1)) * Weights.HandOverflow;
            }
            return value;
        }

        private double BoardValue(Game game, PlayerState player)
        {
            double value = player.Stands.Count * Weights.Stand;
            foreach (var def in TitleProgress.EquippedDefs(game, player))
            {
                value += Weights.UpgradeBase + def.Cost * Weights.UpgradeCost;
            }
            return value;
        }

        private static bool WantsTantrums(Game game, PlayerState player) =>
            player.LemonLordKept.Contains("expert-whiner") ||
            (game.State.FirstDibsRow.Contains("cranky-pants") && player.TantrumPile.Count < 3);

        /// <summary>Finished games are judged on the result alone, with a VP-margin gradient.</summary>
        private static double FinalResult(Game game, int viewerId)
        {
            var state = game.State;
            int Total(PlayerState p) => p.InGameVictoryPoints +
                p.LemonLordKept.Count(id => game.MeetsLemonLord(p, id));
            int mine = Total(state.Players[viewerId]);
            int bestOther = state.Players.Where(p => p.PlayerId != viewerId)
                .Select(Total).DefaultIfEmpty(0).Max();
            double outcome = state.Winners.Contains(viewerId) ? 10_000.0 / state.Winners.Count : -10_000;
            return outcome + (mine - bestOther) * 500;
        }
    }
}
