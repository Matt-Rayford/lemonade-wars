using System.Collections.Generic;

namespace LemonadeWars.Engine.Data
{
    /// <summary>Static facts about Turf cards (all six are identical except their power pour number).</summary>
    public sealed class TurfDef
    {
        public int Count { get; set; }
        public int UpgradeSlots { get; set; }
        public IReadOnlyList<int> PowerPourNumbers { get; set; } = new List<int>();
        /// <summary>Base ability: take $1 from the bank when your power pour number is rolled.</summary>
        public int BasePowerPourMoney { get; set; } = 1;
    }
}
