using System.Collections.Generic;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>A Stand in play (or the shape of one still in the supply stack).</summary>
    public sealed class StandInstance
    {
        public int InstanceId { get; set; }
        public string StandTypeId { get; set; } = "";
        public Shape Shape { get; set; }
        /// <summary>Instance ids of equipped Black Market cards, in tuck order.</summary>
        public List<int> Equipped { get; } = new List<int>();
    }
}
