namespace LemonadeWars.Engine.Core
{
    /// <summary>Why a die is currently waiting for the Out of Stock window to close.</summary>
    public enum RollPurpose
    {
        /// <summary>End-of-turn sale: applies to every player.</summary>
        TurnSale,
        /// <summary>Night Shifts: applies only to the roller.</summary>
        NightShifts,
        /// <summary>Spoiled Rotten's free turn-start roll: applies only to the roller.</summary>
        SpoiledRotten,
        /// <summary>Liquid Energy: an extra sale roll that applies to every player.</summary>
        ExtraSale,
        /// <summary>Trade Winds: an end-of-turn roll for one specific Stand.</summary>
        TradeWinds,
    }
}
