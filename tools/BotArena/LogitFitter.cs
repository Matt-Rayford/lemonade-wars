using LemonadeWars.Engine.Ai;

namespace LemonadeWars.BotArena;

/// <summary>
/// Fits a conditional-logit model — P(player wins) proportional to exp(weights · features) —
/// to sampled positions by full-batch Adam on the cross-entropy against the actual
/// win shares. Features are standardised for conditioning; the weights are pulled
/// toward a prior (the weights the bots played with) rather than toward zero, and
/// asset weights are kept non-negative, because a pure predictor learns correlations
/// ("stand-spammers lose") that a planner then obeys as instructions ("never build").
/// Returns weights in raw feature units.
/// </summary>
internal static class LogitFitter
{
    private const int Steps = 2500;
    private const double LearningRate = 0.03;
    private const double Beta1 = 0.9;
    private const double Beta2 = 0.999;

    /// <param name="prior">Logit-unit weights to shrink toward, raw feature units.</param>
    /// <param name="anchor">Strength of the pull toward the prior, per standardised unit squared.</param>
    public static double[] Fit(IReadOnlyList<PositionSample> samples, double[] prior, double anchor,
        out double finalLoss)
    {
        int features = PositionFeatures.Count;
        var scale = FeatureScales(samples, features);
        var standardisedPrior = new double[features];
        var weights = new double[features];
        for (int i = 0; i < features; i++)
        {
            standardisedPrior[i] = prior[i] * scale[i];
            weights[i] = standardisedPrior[i];
        }
        var firstMoment = new double[features];
        var secondMoment = new double[features];
        var gradient = new double[features];
        finalLoss = double.NaN;

        for (int step = 1; step <= Steps; step++)
        {
            Array.Clear(gradient, 0, features);
            double loss = 0;
            foreach (var sample in samples)
            {
                loss += Accumulate(sample, weights, scale, gradient);
            }
            double sampleCount = samples.Count;
            for (int i = 0; i < features; i++)
            {
                double anchoredGradient = gradient[i] / sampleCount + anchor * (weights[i] - standardisedPrior[i]);
                firstMoment[i] = Beta1 * firstMoment[i] + (1 - Beta1) * anchoredGradient;
                secondMoment[i] = Beta2 * secondMoment[i] + (1 - Beta2) * anchoredGradient * anchoredGradient;
                double correctedFirst = firstMoment[i] / (1 - Math.Pow(Beta1, step));
                double correctedSecond = secondMoment[i] / (1 - Math.Pow(Beta2, step));
                weights[i] -= LearningRate * correctedFirst / (Math.Sqrt(correctedSecond) + 1e-8);
                if (weights[i] < 0 && PositionFeatures.Names[i] != nameof(EvaluationWeights.SeatTempo))
                {
                    weights[i] = 0; // every other feature is oriented so that more is good
                }
            }
            finalLoss = loss / sampleCount;
        }

        var rawWeights = new double[features];
        for (int i = 0; i < features; i++)
        {
            rawWeights[i] = weights[i] / scale[i];
        }
        return rawWeights;
    }

    /// <summary>Cross-entropy of one position and its gradient contribution (standardised units).</summary>
    private static double Accumulate(PositionSample sample, double[] weights, double[] scale, double[] gradient)
    {
        int players = sample.Rows.Length;
        var logits = new double[players];
        double maxLogit = double.MinValue;
        for (int player = 0; player < players; player++)
        {
            double logit = 0;
            var row = sample.Rows[player];
            for (int i = 0; i < weights.Length; i++)
            {
                logit += weights[i] * row[i] / scale[i];
            }
            logits[player] = logit;
            maxLogit = Math.Max(maxLogit, logit);
        }
        // Log-sum-exp around the largest logit, so big scores cannot overflow.
        double sum = 0;
        for (int player = 0; player < players; player++)
        {
            sum += Math.Exp(logits[player] - maxLogit);
        }
        double logSum = maxLogit + Math.Log(sum);
        double loss = 0;
        for (int player = 0; player < players; player++)
        {
            double predictedShare = Math.Exp(logits[player] - logSum);
            double actualShare = sample.WinShares[player];
            if (actualShare > 0)
            {
                loss -= actualShare * (logits[player] - logSum);
            }
            double residual = predictedShare - actualShare;
            var row = sample.Rows[player];
            for (int i = 0; i < weights.Length; i++)
            {
                gradient[i] += residual * row[i] / scale[i];
            }
        }
        return loss;
    }

    /// <summary>Per-feature standard deviation across all rows (1 for a constant feature).</summary>
    private static double[] FeatureScales(IReadOnlyList<PositionSample> samples, int features)
    {
        var sum = new double[features];
        var sumSquares = new double[features];
        long rows = 0;
        foreach (var sample in samples)
        {
            foreach (var row in sample.Rows)
            {
                rows++;
                for (int i = 0; i < features; i++)
                {
                    sum[i] += row[i];
                    sumSquares[i] += row[i] * row[i];
                }
            }
        }
        var scale = new double[features];
        for (int i = 0; i < features; i++)
        {
            double mean = sum[i] / rows;
            double variance = sumSquares[i] / rows - mean * mean;
            scale[i] = variance > 1e-12 ? Math.Sqrt(variance) : 1.0;
        }
        return scale;
    }
}
