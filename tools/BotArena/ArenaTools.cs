using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.BotArena;

/// <summary>Shared arena plumbing: game data and the extended bot-level roster.</summary>
internal static class ArenaTools
{
    /// <summary>
    /// Arena-only levels beyond the shipped difficulties: "random", bare "greedy" (the
    /// pre-planner medium), "planner" at an explicit budget, "planner:&lt;weights.json&gt;"
    /// (a tuner output file), the planner's wide / lookahead variants, raw "ismcts",
    /// truncated-rollout "wam", and pimc variants at explicit configs — kept for A/Bs
    /// against whatever BotFactory currently ships.
    /// </summary>
    public static IBot MakeBot(string level, ulong seed, int budgetMs) =>
        level.StartsWith("planner:", StringComparison.Ordinal)
            ? new PlannerBot(seed, budgetMs, weights: WeightsFrom(level.Substring("planner:".Length)))
        : level == "random"
            ? new RandomBot(seed)
            : level == "greedy"
                ? new GreedyBot()
            : level == "planner"
                ? new PlannerBot(seed, budgetMs)
            : level == "planner+"
                ? new PlannerBot(seed, budgetMs, worlds: 6, rootBeam: 8)
            : level == "planner-la"
                ? new PlannerBot(seed, budgetMs, worlds: 3, rootBeam: 6, roundLookahead: true)
            : level == "planner-la6"
                ? new PlannerBot(seed, budgetMs, worlds: 6, rootBeam: 6, roundLookahead: true)
            : level == "ismcts"
                ? new IsmctsBot(seed, budgetMs)
                : level == "wam"
                    ? new IsmctsBot(seed, budgetMs, rolloutDepth: 80)
                    : level == "pimc+"
                        ? new SearchBot(seed, budgetMs, maxCandidates: 18, maxWorlds: 30)
                        : level == "pimc++"
                            ? new SearchBot(seed, budgetMs, maxCandidates: 24, maxWorlds: 50)
                            : level == "pimc"
                                ? new SearchBot(seed, budgetMs)
                                // "hard"/"wambulance" etc: exactly as shipped, own budgets.
                                : (IBot)BotFactory.Create(level, seed);

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
