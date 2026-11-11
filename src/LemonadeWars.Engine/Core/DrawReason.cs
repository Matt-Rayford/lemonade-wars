namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Where a draw came from, so the action log can tell a spent ACTION apart from the
    /// automatic turn-start draw. Travels as a camelCase string ("turnStart") on the wire.
    /// </summary>
    public enum DrawReason
    {
        /// <summary>A card effect or Black Market ability handed the cards over.</summary>
        Effect,
        /// <summary>The automatic draw at the top of a turn (2 for the Whiniest Baby).</summary>
        TurnStart,
        /// <summary>The player spent an action on Draw a Lemon Card.</summary>
        Action,
    }
}
