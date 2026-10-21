using System;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Static evaluation of a position from one player's seat: how good is this state
    /// for me, relative to my rivals? Each player's worth is a dot product of
    /// <see cref="PositionFeatures"/> and <see cref="EvaluationWeights"/>; the result
    /// is my worth minus a soft maximum over the rivals — the log-odds of winning
    /// under a conditional-logit model, which is exactly what the arena fits the
    /// weights to. One scale for everything, so that plans built from different move
    /// types compare honestly.
    /// </summary>
    public sealed class PositionEvaluator
    {
        public EvaluationWeights Weights { get; }
        private readonly double[] _vector;

        public PositionEvaluator(EvaluationWeights? weights = null)
        {
            Weights = weights ?? EvaluationWeights.Default;
            _vector = Weights.ToVector();
        }

        /// <summary>My worth minus the soft maximum of my rivals' worth.</summary>
        public double Evaluate(Game game, int viewerId)
        {
            var state = game.State;
            if (state.Stage == GameStage.Finished)
            {
                return FinalResult(game, viewerId);
            }
            double horizon = RemainingRounds(game);
            double mine = 0;
            var rivals = new double[state.Players.Count - 1];
            int rivalCount = 0;
            foreach (var player in state.Players)
            {
                double score = PlayerScore(game, player, viewerId, horizon);
                if (player.PlayerId == viewerId)
                {
                    mine = score;
                }
                else
                {
                    rivals[rivalCount++] = score;
                }
            }
            return rivalCount == 0 ? mine : mine - SoftMax(rivals, rivalCount, Weights.RivalTemperature);
        }

        /// <summary>Absolute worth of one player's position; public so tests can probe terms.</summary>
        public double PlayerScore(Game game, PlayerState player, int viewerId, double horizon)
        {
            var features = PositionFeatures.Extract(game, player, viewerId, horizon);
            double score = 0;
            for (int i = 0; i < features.Length; i++)
            {
                score += features[i] * _vector[i];
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
        /// temperature * log(sum(exp(score / temperature))): the best rival's score when
        /// one rival dominates, a little more when several are close behind.
        /// </summary>
        private static double SoftMax(double[] scores, int count, double temperature)
        {
            double t = Math.Max(1e-6, temperature);
            double best = double.MinValue;
            for (int i = 0; i < count; i++)
            {
                best = Math.Max(best, scores[i]);
            }
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                sum += Math.Exp((scores[i] - best) / t);
            }
            return best + t * Math.Log(sum);
        }

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
