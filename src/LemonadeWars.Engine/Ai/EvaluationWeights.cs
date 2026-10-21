namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Tunable coefficients of <see cref="PositionEvaluator"/>. Mutable so the arena's
    /// self-play tuner (BotArena "tune") can perturb them; everything ships with
    /// <see cref="Default"/>. Units: one in-game victory point is 100. The shipped
    /// values are the 2026-10-20 tuner output rounded: against three greedy bots the
    /// planner went from 1.84x to 2.12x (200 paired games) and held parity against the
    /// untuned planner.
    /// </summary>
    public sealed class EvaluationWeights
    {
        public double VictoryPoint = 100;
        public double LordMet = 85;
        /// <summary>Scales a lord's progress fraction; convex, so finishing beats dabbling.</summary>
        public double LordProgress = 40;
        public double DibsProgress = 80;
        public double Money = 3.0;
        /// <summary>Money's worth once the game is in its last lap.</summary>
        public double MoneyLate = 1.2;
        /// <summary>Squared progress toward affording the next Bragging Rights card.</summary>
        public double BraggingReach = 25;
        /// <summary>Worth of one expected dollar per sale roll, per remaining round.</summary>
        public double IncomePerRound = 4.0;
        public double CardInHand = 3.5;
        public double ReactionInHand = 4.0;
        public double HandOverflow = 4.0;
        public double TantrumInPile = 5.5;
        public double WhiniestBaby = 12;
        public double SpoiledRotten = 6;
        public double UpgradeBase = 4.0;
        public double UpgradeCost = 0.6;
        public double Stand = 2.0;
        public double Trapped = 6;
        /// <summary>How much the strongest rival's score counts against ours.</summary>
        public double Rival = 1.0;
        public double Field = 0.25;

        public static EvaluationWeights Default => new EvaluationWeights();

        public EvaluationWeights Clone() => (EvaluationWeights)MemberwiseClone();
    }
}
