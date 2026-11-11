namespace LemonadeWars.Engine.Core
{
    /// <summary>Phases within a single turn (rulebook p6).</summary>
    public enum TurnPhase
    {
        /// <summary>Draw 1 Lemon card (2-discard-1 for the Whiniest Baby).</summary>
        Start,
        /// <summary>Up to 2 actions plus free actions.</summary>
        Play,
        /// <summary>Sale die roll and payouts, then pass to the next player.</summary>
        Sell,
    }
}
