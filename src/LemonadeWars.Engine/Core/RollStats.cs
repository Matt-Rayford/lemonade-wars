namespace LemonadeWars.Engine.Core
{
    /// <summary>Per-player accumulators for the current roll episode ("on a single roll" titles).</summary>
    public sealed class RollStats
    {
        /// <summary>Money earned from sales/pours this roll (Big Earner).</summary>
        public int Earned { get; set; }
        /// <summary>Money stolen via roll triggers/traps this roll (Clean Getaway).</summary>
        public int MoneyStolen { get; set; }
        /// <summary>Cards stolen via roll triggers this roll (Sticky Fingers).</summary>
        public int CardsStolen { get; set; }
        /// <summary>Cards drawn and kept from roll triggers this roll (Double Scoop).</summary>
        public int CardsKept { get; set; }
    }
}
