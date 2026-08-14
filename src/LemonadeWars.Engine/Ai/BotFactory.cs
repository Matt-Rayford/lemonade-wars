namespace LemonadeWars.Engine.Ai
{
    /// <summary>Difficulty levels exposed to the UI and the server protocol.</summary>
    public static class BotFactory
    {
        public const string Easy = "easy";
        public const string Medium = "medium";
        public const string Hard = "hard";
        /// <summary>Above hard: the same search with double the time and a wider net.</summary>
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
                // Tournament-tuned: at ~750ms the wider search dominates both the old
                // 300ms/14x14 config AND a same-budget ISMCTS (see tools/BotArena;
                // "ismcts" and "pimc+" remain as arena levels for future A/Bs).
                case Hard: return new SearchBot(seed, budgetMs: 750, maxCandidates: 18, maxWorlds: 30);
                // The wambulance: 1.12x over three HARD bots in the arena. Truncated
                // ISMCTS only ever TIED this config at equal budget — flat keeps the
                // crown until someone builds tree reuse.
                case Wambulance: return new SearchBot(seed, budgetMs: 1400, maxCandidates: 24, maxWorlds: 50);
                default: return new GreedyBot();
            }
        }
    }
}
