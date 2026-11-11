namespace LemonadeWars.Engine.Core
{
    /// <summary>A tantrum sitting in a player's pile, with the order it was gained (Whiniest Baby tiebreak).</summary>
    public sealed class TantrumRecord
    {
        public int InstanceId { get; set; }
        public int GainSeq { get; set; }
    }
}
