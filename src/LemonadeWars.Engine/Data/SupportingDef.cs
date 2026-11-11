using System.Collections.Generic;

namespace LemonadeWars.Engine.Data
{
    /// <summary>The Bragging Rights economy from supporting.json.</summary>
    public sealed class SupportingDef
    {
        public IReadOnlyList<int> BraggingRightsPrices { get; set; } = new List<int>();
        public int BraggingRightsPurchaseLimitPerTurn { get; set; } = 1;
    }
}
