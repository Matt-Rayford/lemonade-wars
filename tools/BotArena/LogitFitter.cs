using LemonadeWars.Engine.Ai;

namespace LemonadeWars.BotArena;

/// <summary>
/// Fits a conditional-logit model — P(player q wins) proportional to exp(w · f_q) —
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
        var priorStd = new double[features];
        var w = new double[features];
        for (int i = 0; i < features; i++)
        {
            priorStd[i] = prior[i] * scale[i];
            w[i] = priorStd[i];
        }
        var m = new double[features];
        var v = new double[features];
        var gradient = new double[features];
        finalLoss = double.NaN;

        for (int step = 1; step <= Steps; step++)
        {
            Array.Clear(gradient, 0, features);
            double loss = 0;
            foreach (var sample in samples)
            {
                loss += Accumulate(sample, w, scale, gradient);
            }
            double n = samples.Count;
            for (int i = 0; i < features; i++)
            {
                double g = gradient[i] / n + anchor * (w[i] - priorStd[i]);
                m[i] = Beta1 * m[i] + (1 - Beta1) * g;
                v[i] = Beta2 * v[i] + (1 - Beta2) * g * g;
                double mHat = m[i] / (1 - Math.Pow(Beta1, step));
                double vHat = v[i] / (1 - Math.Pow(Beta2, step));
                w[i] -= LearningRate * mHat / (Math.Sqrt(vHat) + 1e-8);
                if (w[i] < 0 && PositionFeatures.Names[i] != nameof(EvaluationWeights.SeatTempo))
                {
                    w[i] = 0; // every other feature is oriented so that more is good
                }
            }
            finalLoss = loss / n;
        }

        var raw = new double[features];
        for (int i = 0; i < features; i++)
        {
            raw[i] = w[i] / scale[i];
        }
        return raw;
    }

    /// <summary>Cross-entropy of one position and its gradient contribution (standardised units).</summary>
    private static double Accumulate(PositionSample sample, double[] w, double[] scale, double[] gradient)
    {
        int players = sample.Rows.Length;
        var logits = new double[players];
        double best = double.MinValue;
        for (int q = 0; q < players; q++)
        {
            double z = 0;
            var row = sample.Rows[q];
            for (int i = 0; i < w.Length; i++)
            {
                z += w[i] * row[i] / scale[i];
            }
            logits[q] = z;
            best = Math.Max(best, z);
        }
        double sum = 0;
        for (int q = 0; q < players; q++)
        {
            sum += Math.Exp(logits[q] - best);
        }
        double logSum = best + Math.Log(sum);
        double loss = 0;
        for (int q = 0; q < players; q++)
        {
            double p = Math.Exp(logits[q] - logSum);
            double y = sample.WinShares[q];
            if (y > 0)
            {
                loss -= y * (logits[q] - logSum);
            }
            double residual = p - y;
            var row = sample.Rows[q];
            for (int i = 0; i < w.Length; i++)
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
