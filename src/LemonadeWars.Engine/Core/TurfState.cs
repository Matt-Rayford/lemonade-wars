using System.Collections.Generic;

namespace LemonadeWars.Engine.Core
{
    public sealed class TurfState
    {
        public int PowerPourNumber { get; set; }
        /// <summary>Instance ids of equipped Turf Black Market cards (max 5).</summary>
        public List<int> Equipped { get; } = new List<int>();
        /// <summary>Steal the Cashbox trap: lemon instance sitting on this turf, if any.</summary>
        public int? TrapInstanceId { get; set; }
        /// <summary>Who played the trap and collects the stolen earnings.</summary>
        public int? TrapOwnerId { get; set; }
    }
}
