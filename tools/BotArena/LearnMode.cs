using System.Diagnostics;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Data;
using Newtonsoft.Json;

namespace LemonadeWars.BotArena;

/// <summary>
/// Learned evaluator, expert-iteration style: bots carrying the current weights play
/// thousands of games, a conditional-logit fit turns the sampled positions into new
/// weights, the new weights are checked at the table against the old ones and
/// against greedy, and the cycle repeats from whichever set won. Every iteration's
/// weights are written to the output directory so any of them can be baked in.
/// </summary>
internal static class LearnMode
{
    private const int ValidationGames = 200;
    private const int ValidationPlayers = 4;
    private const int MediumBudgetMs = 150;
    /// <summary>Points per unit of log-odds: rescales fitted weights so one VP reads as about 100.</summary>
    private const double PointsPerVictoryPoint = 100;
    /// <summary>Prior strengths tried per iteration: from "trust the data" to "nudge the incumbent".</summary>
    private static readonly double[] Anchors = { 0.03, 0.3, 3.0 };
    /// <summary>A candidate must at least hold its own against the incumbent (fair share minus this) to be adopted.</summary>
    private const double AdoptSlack = 0.01;

    public static void Run(int games, int iterations, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var db = ArenaTools.LoadDatabase();
        var incumbent = EvaluationWeights.Default;
        var stopwatch = Stopwatch.StartNew();
        Console.WriteLine($"learning from {games} games per iteration, {iterations} iteration(s) -> {outDir}");
        Console.WriteLine($"  baseline: incumbent vs 3 greedy = {Versus(db, incumbent, null):P1}");

        for (int iteration = 1; iteration <= iterations; iteration++)
        {
            var samples = FeatureSampler.Generate(db, incumbent, games, seedBase: 900_000UL * (ulong)iteration,
                done =>
                {
                    if (done % 500 == 0)
                    {
                        Console.WriteLine($"  it{iteration} {done}/{games} games ({stopwatch.Elapsed.TotalMinutes:F1} min)");
                    }
                });
            Console.WriteLine($"  it{iteration}: {samples.Count} samples; fitting...");
            var prior = incumbent.ToVector().Select(w => w / incumbent.RivalTemperature).ToArray();
            EvaluationWeights? best = null;
            double bestShare = 1.0 / ValidationPlayers - AdoptSlack;
            foreach (double anchor in Anchors)
            {
                var raw = LogitFitter.Fit(samples, prior, anchor, out double loss);
                var candidate = ToWeights(raw);
                string path = Path.Combine(outDir, $"weights-it{iteration}-anchor{anchor:0.###}.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(candidate, Formatting.Indented));
                double vsOld = Versus(db, candidate, incumbent);
                Console.WriteLine($"  it{iteration} anchor {anchor,-6:0.###} loss {loss:F4}: vs 3 incumbent = {vsOld:P1} " +
                    $"({stopwatch.Elapsed.TotalMinutes:F1} min) -> {Path.GetFileName(path)}");
                if (vsOld > bestShare)
                {
                    bestShare = vsOld;
                    best = candidate;
                }
            }
            if (best == null)
            {
                Console.WriteLine($"  it{iteration}: no anchor beat the incumbent; next iteration samples from it again");
                continue;
            }
            incumbent = best;
            Console.WriteLine($"  it{iteration}: ADOPTED ({bestShare:P1} vs old; vs 3 greedy = {Versus(db, incumbent, null):P1})");
            PrintWeights(incumbent);
        }
        File.WriteAllText(Path.Combine(outDir, "weights-final.json"),
            JsonConvert.SerializeObject(incumbent, Formatting.Indented));
        Console.WriteLine("final incumbent:");
        PrintWeights(incumbent);
    }

    /// <summary>
    /// Fitted logit weights become evaluator points: scaled so a victory point is worth
    /// about <see cref="PointsPerVictoryPoint"/>, with the rival temperature set to the
    /// same scale so the evaluator's softmax reproduces the model's log-odds exactly.
    /// </summary>
    private static EvaluationWeights ToWeights(double[] logitWeights)
    {
        int vpIndex = Array.IndexOf(PositionFeatures.Names, nameof(EvaluationWeights.VictoryPoint));
        double perLogit = logitWeights[vpIndex] > 1e-9
            ? PointsPerVictoryPoint / logitWeights[vpIndex]
            : PointsPerVictoryPoint;
        var scaled = logitWeights.Select(w => w * perLogit).ToArray();
        return EvaluationWeights.FromVector(scaled, rivalTemperature: perLogit);
    }

    /// <summary>Hero win share for medium-config planners: candidate vs three of the baseline (null = greedy).</summary>
    private static double Versus(CardDatabase db, EvaluationWeights candidate, EvaluationWeights? baseline)
    {
        var config = new PlannerConfig { BudgetMs = MediumBudgetMs };
        return Matchup.Play(db,
            (_, seed) => new PlannerBot(seed, config, candidate),
            (_, seed) => baseline == null ? new GreedyBot() : new PlannerBot(seed, config, baseline),
            ValidationGames, ValidationPlayers).WinShare;
    }

    private static void PrintWeights(EvaluationWeights weights)
    {
        var vector = weights.ToVector();
        for (int i = 0; i < vector.Length; i++)
        {
            Console.WriteLine($"      {PositionFeatures.Names[i],-16} {vector[i],9:F2}");
        }
        Console.WriteLine($"      {"RivalTemperature",-16} {weights.RivalTemperature,9:F2}");
    }
}
