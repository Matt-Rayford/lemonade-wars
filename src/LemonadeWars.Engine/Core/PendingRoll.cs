namespace LemonadeWars.Engine.Core
{
    /// <summary>A die that has been rolled but not yet applied (re-rollable via Out of Stock).</summary>
    public sealed class PendingRoll
    {
        public int Value { get; set; }
        public RollPurpose Purpose { get; set; }
        public int RollerId { get; set; }
        /// <summary>Trade Winds: the single Stand this roll applies to.</summary>
        public int? StandInstanceId { get; set; }
    }
}
