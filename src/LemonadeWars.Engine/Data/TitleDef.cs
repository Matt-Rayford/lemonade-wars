namespace LemonadeWars.Engine.Data
{
    /// <summary>A First Dibs or Lemon Lord title card. Every title is worth 1 VP.</summary>
    public sealed class TitleDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public TitleKind Kind { get; set; }
        public int VictoryPoints { get; set; } = 1;
        /// <summary>Human-readable condition text; machine evaluation is wired up per-title in the engine.</summary>
        public string Condition { get; set; } = "";
        public string Flavor { get; set; } = "";
        /// <summary>For icon-count conditions: the icon to count on owned/held cards.</summary>
        public string? CountedIcon { get; set; }
        /// <summary>For icon-count conditions: how many are required.</summary>
        public int? RequiredCount { get; set; }
    }
}
