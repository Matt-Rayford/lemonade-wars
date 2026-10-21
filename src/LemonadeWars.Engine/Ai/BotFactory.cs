namespace LemonadeWars.Engine.Ai
{
    /// <summary>Difficulty levels exposed to the UI and the server protocol.</summary>
    public static class BotFactory
    {
        public const string Easy = "easy";
        public const string Medium = "medium";
        public const string Hard = "hard";
        /// <summary>Above hard: the same planner with more worlds, a wider beam, and more time.</summary>
        public const string Wambulance = "wambulance";

        public static string Normalize(string? level)
        {
            switch ((level ?? "").Trim().ToLowerInvariant())
            {
                case Easy: return Easy;
                case Hard: return Hard;
                case Wambulance: return Wambulance;
                // Persisted rooms/action logs carry the old spelling; keep it as an alias.
                case "wambulence": return Wambulance;
                default: return Medium;
            }
        }

        public static IBot Create(string? level, ulong seed)
        {
            switch (Normalize(level))
            {
                case Easy: return new EasyBot(seed);
                // Hard and the wambulance are the planner judging every line from the
                // start of its NEXT turn (sale roll + a greedy turn by every rival) over
                // more sampled worlds. Arena, 150 games vs three medium bots: 1.15x for
                // this config; the flat-search hard it replaces had fallen to 0.80x once
                // medium learned to plan. See tools/BotArena for the levels kept for A/Bs.
                case Hard:
                    return new PlannerBot(seed, budgetMs: 1500, worlds: 6, rootBeam: 6, roundLookahead: true);
                case Wambulance:
                    return new PlannerBot(seed, budgetMs: 2500, worlds: 10, rootBeam: 8, roundLookahead: true);
                // Medium plans its turn on a determinized copy; the greedy policy it
                // replaced lives on as Easy's brain and every search's rollout policy.
                default: return new PlannerBot(seed, budgetMs: 150, worlds: 2);
            }
        }
    }
}
