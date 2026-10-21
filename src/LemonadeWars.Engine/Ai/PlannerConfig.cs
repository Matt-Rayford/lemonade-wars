namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// How hard a <see cref="PlannerBot"/> thinks. The shipped difficulties are the
    /// three presets; the arena constructs others for A/B runs.
    /// </summary>
    public sealed class PlannerConfig
    {
        public int BudgetMs = 150;
        /// <summary>Determinizations averaged per decision; more worlds, steadier reads on hidden cards.</summary>
        public int Worlds = 2;
        /// <summary>How many opening moves are followed to the end of the turn.</summary>
        public int RootBeam = 4;
        /// <summary>
        /// Judge each line at the start of the planner's NEXT turn — after its sale roll
        /// and a turn by every rival — instead of at the end of this one, so the race
        /// and the rivals' answers are priced in. Several times the cost.
        /// </summary>
        public bool RoundLookahead;
        /// <summary>
        /// Rivals answer response windows from their sampled hands instead of passing,
        /// so cancel risk is read off the worlds rather than estimated from hand size.
        /// Only sensible with enough worlds to average over.
        /// </summary>
        public bool SampledReactions;
        /// <summary>Die outcomes averaged per lookahead leaf; 1 uses the world's own roll.</summary>
        public int RollSamples = 1;
        /// <summary>
        /// Rivals in the lookahead pick their turn actions by one-ply evaluation instead
        /// of the greedy heuristic — the same yardstick the planner judges itself by.
        /// </summary>
        public bool EvaluatorRivals;

        public static PlannerConfig Medium => new PlannerConfig();

        public static PlannerConfig Hard => new PlannerConfig
        {
            BudgetMs = 1500,
            Worlds = 6,
            RootBeam = 6,
            RoundLookahead = true,
        };

        public static PlannerConfig Wambulance => new PlannerConfig
        {
            BudgetMs = 2500,
            Worlds = 10,
            RootBeam = 8,
            RoundLookahead = true,
        };
    }
}
