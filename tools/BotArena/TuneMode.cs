using System.Diagnostics;
using System.Reflection;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Data;
using Newtonsoft.Json;

namespace LemonadeWars.BotArena;

/// <summary>
/// Self-play coordinate descent over <see cref="EvaluationWeights"/>: each weight is
/// nudged up and down in turn, a planner carrying the nudge plays a table of planners
/// carrying the incumbent weights (plus one greedy seat, so tuning cannot drift into
/// a style that only beats itself), and a nudge that lifts the win share clearly above
/// the fair share is adopted. Prints every trial and dumps the final weights as JSON.
/// </summary>
internal static class TuneMode
{
    private const double StepUp = 1.35;
    private const double StepDown = 0.74;
    /// <summary>Win-share points above fair a nudge must gain to be adopted (about 1.6 standard errors at 200 games).</summary>
    private const double AdoptMargin = 0.05;
    private const int Worlds = 1;

    /// <summary>The weights worth the arena's time; the rest are structural and left at their defaults.</summary>
    private static readonly string[] TunedFields =
    {
        nameof(EvaluationWeights.Money), nameof(EvaluationWeights.IncomePerRound),
        nameof(EvaluationWeights.CardInHand), nameof(EvaluationWeights.ReactionInHand),
        nameof(EvaluationWeights.DibsProgress), nameof(EvaluationWeights.LordProgress),
        nameof(EvaluationWeights.LordMet), nameof(EvaluationWeights.UpgradeBase),
        nameof(EvaluationWeights.Stand), nameof(EvaluationWeights.Rival),
        nameof(EvaluationWeights.BraggingReach), nameof(EvaluationWeights.TantrumInPile),
    };

    public static void Run(int games, int players, int rounds, int budgetMs, string outPath)
    {
        var db = ArenaTools.LoadDatabase();
        var incumbent = EvaluationWeights.Default;
        var stopwatch = Stopwatch.StartNew();
        double fair = 1.0 / players;

        Console.WriteLine($"tuning {TunedFields.Length} weights, {rounds} round(s) of {games} games " +
            $"x{players}, planner budget {budgetMs}ms ({Worlds} world)");
        Console.WriteLine($"  sanity: incumbent vs itself = {Trial(db, incumbent, incumbent, games, players, budgetMs):P1} (fair {fair:P1})");

        for (int round = 1; round <= rounds; round++)
        {
            int adopted = 0;
            foreach (string fieldName in TunedFields)
            {
                var field = typeof(EvaluationWeights).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)!;
                EvaluationWeights? best = null;
                double bestShare = fair + AdoptMargin;
                foreach (double factor in new[] { StepUp, StepDown })
                {
                    var candidate = incumbent.Clone();
                    double value = (double)field.GetValue(candidate)! * factor;
                    field.SetValue(candidate, value);
                    double share = Trial(db, candidate, incumbent, games, players, budgetMs);
                    Console.WriteLine($"  r{round} {fieldName,-16} x{factor:F2} -> {value,8:F2}: {share:P1}  ({stopwatch.Elapsed.TotalMinutes:F1} min)");
                    if (share > bestShare)
                    {
                        bestShare = share;
                        best = candidate;
                    }
                }
                if (best != null)
                {
                    incumbent = best;
                    adopted++;
                    Console.WriteLine($"  ADOPT {fieldName} = {field.GetValue(incumbent):F2} ({bestShare:P1})");
                }
            }
            Console.WriteLine($"round {round}: {adopted} weights changed");
            File.WriteAllText(outPath, JsonConvert.SerializeObject(incumbent, Formatting.Indented));
            if (adopted == 0)
            {
                break;
            }
        }
        Console.WriteLine($"final weights -> {outPath}");
        Console.WriteLine(JsonConvert.SerializeObject(incumbent, Formatting.Indented));
    }

    /// <summary>
    /// Hero win share with the candidate weights. Seat 1 of the table (never the hero's
    /// seat in the same game, since the hero rotates) is greedy when the table has
    /// room; every other seat carries the incumbent weights.
    /// </summary>
    private static double Trial(CardDatabase db, EvaluationWeights candidate, EvaluationWeights incumbent,
        int games, int players, int budgetMs)
    {
        var result = Matchup.Play(db,
            (_, seed) => new PlannerBot(seed, budgetMs, Worlds, candidate),
            (seat, seed) => players >= 3 && seat == players - 1
                ? new GreedyBot()
                : new PlannerBot(seed, budgetMs, Worlds, incumbent),
            games, players);
        return result.WinShare;
    }
}
