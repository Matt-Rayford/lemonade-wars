using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.BotArena;

/// <summary>Shared arena plumbing: game data and the extended bot-level roster.</summary>
internal static class ArenaTools
{
    /// <summary>
    /// Arena-only levels beyond the shipped difficulties: "random", bare "greedy" (the
    /// pre-planner medium), "planner" at an explicit budget, "planner:&lt;weights.json&gt;"
    /// (a tuner output file), the planner's wide / lookahead variants, the wam-* research
    /// candidates, raw "ismcts" (+ "ismcts-eval"), truncated-rollout "wam", and pimc
    /// variants at explicit configs — kept for A/Bs
    /// against whatever BotFactory currently ships.
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
                return new PlannerBot(seed, Wambulance(budgetMs, c => c.SampledReactions = true));
            case "wam-rolls":
                return new PlannerBot(seed, Wambulance(budgetMs, c => c.RollSamples = 3));
            case "wam-evalrivals":
                return new PlannerBot(seed, Wambulance(budgetMs, c => { c.EvaluatorRivals = true; c.Worlds = 6; }));
            case "wam-all":
                return new PlannerBot(seed, Wambulance(budgetMs, c =>
                {
                    c.SampledReactions = true;
                    c.RollSamples = 2;
                    c.EvaluatorRivals = true;
                    c.Worlds = 6;
                }));
            case "ismcts": return new IsmctsBot(seed, budgetMs);
            case "ismcts-eval": return new IsmctsBot(seed, budgetMs, rolloutDepth: 60, evaluator: new PositionEvaluator());
            case "wam": return new IsmctsBot(seed, budgetMs, rolloutDepth: 80);
            case "pimc+": return new SearchBot(seed, budgetMs, maxCandidates: 18, maxWorlds: 30);
            case "pimc++": return new SearchBot(seed, budgetMs, maxCandidates: 24, maxWorlds: 50);
            case "pimc": return new SearchBot(seed, budgetMs);
            // "hard"/"wambulance" etc: exactly as shipped, own budgets.
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

    /// <summary>A weights file as the tuner writes it, for A/B runs before baking values in.</summary>
    private static EvaluationWeights WeightsFrom(string path) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<EvaluationWeights>(File.ReadAllText(path))
        ?? throw new InvalidDataException($"no weights in {path}");

    public static CardDatabase LoadDatabase()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "game-data");
            if (Directory.Exists(candidate))
            {
                return CardDatabase.Load(candidate);
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("game-data not found above the arena binary.");
    }
}
