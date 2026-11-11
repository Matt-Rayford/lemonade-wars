using System.Collections.Generic;

namespace LemonadeWars.Engine.Data
{
    /// <summary>A unique Black Market card. Physical copies differ only by printed shape.</summary>
    public sealed class BlackMarketCardDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public EquipTarget Target { get; set; }
        public int Cost { get; set; }
        public int Count { get; set; }
        public EffectTiming Timing { get; set; }
        /// <summary>Designer taxonomy (e.g. "Defense", "Roll Modification"); Lemon Lord titles count these.</summary>
        public string Category { get; set; } = "";
        public string Effect { get; set; } = "";
        public string Flavor { get; set; } = "";
        /// <summary>One shape per physical copy, index-aligned with copy order.</summary>
        public IReadOnlyList<Shape> Shapes { get; set; } = new List<Shape>();
        /// <summary>Icon glyphs printed on the card; some Lemon Lord titles count cards bearing an icon.</summary>
        public IReadOnlyList<string> Icons { get; set; } = new List<string>();
        /// <summary>Die face added by numbered cards (Pushy Salesman: sale numbers; Spiked Lemonade: power pour numbers).</summary>
        public int? Number { get; set; }
    }
}
