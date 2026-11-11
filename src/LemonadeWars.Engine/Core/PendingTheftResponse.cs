namespace LemonadeWars.Engine.Core
{
    /// <summary>A window offering the victim of a money steal the chance to play Profit Share.</summary>
    public sealed class PendingTheftResponse
    {
        public int VictimId { get; set; }
        public int AttackerId { get; set; }
        public int AmountStolen { get; set; }
    }
}
