using System;
using System.Reflection;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// Coefficients of <see cref="PositionEvaluator"/>, one per
    /// <see cref="PositionFeatures"/> entry, in "points" where one in-game victory
    /// point is about 100. Mutable so the arena can tune or fit them; everything
    /// ships with <see cref="Default"/>.
    /// </summary>
    public sealed class EvaluationWeights
    {
        public double VictoryPoint = 100;
        public double LordMet = 85;
        public double LordProgress = 40;
        public double DibsProgress = 80;
        public double Money = 3.0;
        /// <summary>Money's worth once the game is in its last lap.</summary>
        public double MoneyLate = 1.2;
        /// <summary>Squared progress toward affording the next Bragging Rights card.</summary>
        public double BraggingReach = 25;
        /// <summary>Worth of one expected dollar per sale roll, per remaining round.</summary>
        public double IncomePerRound = 4.0;
        /// <summary>Worth of one expected dollar per sale roll regardless of how long the game lasts.</summary>
        public double IncomePerRoll = 0;
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
        /// <summary>Worth of being further from acting again (penalised when negative).</summary>
        public double SeatTempo = 0;
        /// <summary>Worth of an unspent action for the active player.</summary>
        public double ActionsLeft = 0;
        /// <summary>
        /// Softness of the rival combination: how many points of lead over the field
        /// count as one unit of log-odds. Smaller is closer to "beat the single best rival".
        /// </summary>
        public double RivalTemperature = 60;

        public static EvaluationWeights Default => new EvaluationWeights();

        public EvaluationWeights Clone() => (EvaluationWeights)MemberwiseClone();

        /// <summary>Coefficients in <see cref="PositionFeatures.Names"/> order.</summary>
        public double[] ToVector()
        {
            var vector = new double[PositionFeatures.Count];
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (double)Field(PositionFeatures.Names[i]).GetValue(this)!;
            }
            return vector;
        }

        public static EvaluationWeights FromVector(double[] vector, double rivalTemperature)
        {
            if (vector.Length != PositionFeatures.Count)
            {
                throw new ArgumentException($"expected {PositionFeatures.Count} weights, got {vector.Length}");
            }
            var weights = new EvaluationWeights { RivalTemperature = rivalTemperature };
            for (int i = 0; i < vector.Length; i++)
            {
                Field(PositionFeatures.Names[i]).SetValue(weights, vector[i]);
            }
            return weights;
        }

        private static FieldInfo Field(string name) =>
            typeof(EvaluationWeights).GetField(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingFieldException(nameof(EvaluationWeights), name);
    }
}
