using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.BotArena;

internal static class ArenaTools
{
    public static ParallelOptions AllCoresButOne =>
        new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount - 1 };

    /// <summary>
    /// The shipped difficulties plus arena-only levels, kept for A/Bs against whatever
    /// BotFactory currently ships. "planner:&lt;weights.json&gt;" loads a tuner output file.
    /// </summary>
    public static IBot MakeBot(string level, ulong seed, int budgetMs)
    {
        if (level.StartsWith("planner:", StringComparison.Ordinal))
        {
            return new PlannerBot(seed, new PlannerConfig { BudgetMs = budgetMs },
                WeightsFrom(level.Substring("planner:".Length)));
        }
        switch (level)
        {
            case "random": return new RandomBot(seed);
            // The pre-planner medium.
            case "greedy": return new GreedyBot();
            case "planner": return new PlannerBot(seed, new PlannerConfig { BudgetMs = budgetMs });
            case "planner+":
                return new PlannerBot(seed, new PlannerConfig { BudgetMs = budgetMs, Worlds = 6, RootBeam = 8 });
            case "planner-la":
                return new PlannerBot(seed, new PlannerConfig
                    { BudgetMs = budgetMs, Worlds = 3, RootBeam = 6, RoundLookahead = true });
            case "planner-la6":
                return new PlannerBot(seed, new PlannerConfig
                    { BudgetMs = budgetMs, Worlds = 6, RootBeam = 6, RoundLookahead = true });
            // Wambulance research candidates, each one idea on top of the shipped config.
            case "wam-sampled":
                return new PlannerBot(seed, Wambulance(budgetMs, config => config.SampledReactions = true));
            case "wam-rolls":
                return new PlannerBot(seed, Wambulance(budgetMs, config => config.RollSamples = 3));
            case "wam-evalrivals":
                return new PlannerBot(seed, Wambulance(budgetMs, config =>
                {
                    config.EvaluatorRivals = true;
                    config.Worlds = 6;
                }));
            case "wam-all":
                return new PlannerBot(seed, Wambulance(budgetMs, config =>
                {
                    config.SampledReactions = true;
                    config.RollSamples = 2;
                    config.EvaluatorRivals = true;
                    config.Worlds = 6;
                }));
            case "ismcts": return new IsmctsBot(seed, budgetMs);
            case "ismcts-eval": return new IsmctsBot(seed, budgetMs, rolloutDepth: 60, evaluator: new PositionEvaluator());
            // ISMCTS with truncated rollouts.
            case "wam": return new IsmctsBot(seed, budgetMs, rolloutDepth: 80);
            case "pimc+": return new SearchBot(seed, budgetMs, maxCandidates: 18, maxWorlds: 30);
            case "pimc++": return new SearchBot(seed, budgetMs, maxCandidates: 24, maxWorlds: 50);
            case "pimc": return new SearchBot(seed, budgetMs);
            // A shipped difficulty runs exactly as shipped, on its own budget.
            default: return BotFactory.Create(level, seed);
        }
    }

    private static PlannerConfig Wambulance(int budgetMs, Action<PlannerConfig> tweak)
    {
        var config = PlannerConfig.Wambulance;
        config.BudgetMs = budgetMs;
        tweak(config);
        return config;
    }

    private static EvaluationWeights WeightsFrom(string tunerOutputPath) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<EvaluationWeights>(File.ReadAllText(tunerOutputPath))
        ?? throw new InvalidDataException($"no weights in {tunerOutputPath}");

    public static CardDatabase LoadDatabase()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "game-data");
            if (Directory.Exists(candidate))
            {
                return CardDatabase.Load(candidate);
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("game-data not found above the arena binary.");
    }
}
