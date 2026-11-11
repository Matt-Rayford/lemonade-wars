namespace LemonadeWars.Engine.Data
{
    /// <summary>A unique Lemon deck card (one definition may have several physical copies).</summary>
    public sealed class LemonCardDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public LemonCardType Type { get; set; }
        public int Count { get; set; }
        public string Effect { get; set; } = "";
        public string Flavor { get; set; } = "";
        /// <summary>Play condition for instants (e.g. "Play after an attack is played"); null when unrestricted.</summary>
        public string? Condition { get; set; }
    }
}
