using System.Collections.Generic;

namespace LemonadeWars.Engine.Data
{
    /// <summary>One of the three purchasable stand tiers.</summary>
    public sealed class StandTypeDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int BaseCost { get; set; }
        public int UpgradeSlots { get; set; }
        public IReadOnlyList<int> SaleNumbers { get; set; } = new List<int>();
        public int BaseEarnings { get; set; }
        public int Count { get; set; }
        /// <summary>How many physical copies bear each shape.</summary>
        public IReadOnlyDictionary<Shape, int> Shapes { get; set; } = new Dictionary<Shape, int>();
    }
}
