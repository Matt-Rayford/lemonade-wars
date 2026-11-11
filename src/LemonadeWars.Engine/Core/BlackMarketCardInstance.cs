using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>A physical Black Market card copy. Copies of one def differ by printed shape.</summary>
    public sealed class BlackMarketCardInstance
    {
        public int InstanceId { get; set; }
        public string DefId { get; set; } = "";
        public Shape Shape { get; set; }
    }
}
