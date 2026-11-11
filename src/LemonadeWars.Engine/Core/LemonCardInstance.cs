namespace LemonadeWars.Engine.Core
{
    /// <summary>A physical Lemon card copy. Identity matters for hands/discards.</summary>
    public sealed class LemonCardInstance
    {
        public int InstanceId { get; set; }
        public string DefId { get; set; } = "";
    }
}
