using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.BotArena;

/// <summary>Shared arena plumbing: game data and the extended bot-level roster.</summary>
internal static class ArenaTools
{
    /// <summary>
    /// Arena-only levels beyond the shipped difficulties: "random", raw "ismcts",
    /// truncated-rollout "wam", and pimc variants at explicit configs — kept for A/Bs
    /// against whatever BotFactory currently ships.
    /// </summary>
    public static IBot MakeBot(string level, ulong seed, int budgetMs) =>
        level == "random"
            ? new RandomBot(seed)
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
