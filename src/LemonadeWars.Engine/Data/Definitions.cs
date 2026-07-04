using System.Collections.Generic;

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

    /// <summary>A unique Black Market card. Physical copies differ only by printed shape.</summary>
    public sealed class BlackMarketCardDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public EquipTarget Target { get; set; }
        public int Cost { get; set; }
        public int Count { get; set; }
        public EffectTiming Timing { get; set; }
        /// <summary>Designer taxonomy (e.g. "Defense", "Roll Modification"); Lemon Lord titles count these.</summary>
        public string Category { get; set; } = "";
        public string Effect { get; set; } = "";
        public string Flavor { get; set; } = "";
        /// <summary>One shape per physical copy, index-aligned with copy order.</summary>
        public IReadOnlyList<Shape> Shapes { get; set; } = new List<Shape>();
        /// <summary>Icon glyphs printed on the card; some Lemon Lord titles count cards bearing an icon.</summary>
        public IReadOnlyList<string> Icons { get; set; } = new List<string>();
        /// <summary>Die face added by numbered cards (Pushy Salesman: sale numbers; Spiked Lemonade: power pour numbers).</summary>
        public int? Number { get; set; }
    }

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

    /// <summary>One of the three purchasable stand tiers.</summary>
    public sealed class StandTypeDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int BaseCost { get; set; }
        public int UpgradeSlots { get; set; }
        public IReadOnlyList<int> SaleNumbers { get; set; } = new List<int>();
        public int BaseEarnings { get; set; }
        public int Count { get; set; }
        /// <summary>How many physical copies bear each shape.</summary>
        public IReadOnlyDictionary<Shape, int> Shapes { get; set; } = new Dictionary<Shape, int>();
    }

    /// <summary>Static facts about Turf cards (all six are identical except their power pour number).</summary>
    public sealed class TurfDef
    {
        public int Count { get; set; }
        public int UpgradeSlots { get; set; }
        public IReadOnlyList<int> PowerPourNumbers { get; set; } = new List<int>();
        /// <summary>Base ability: take $1 from the bank when your power pour number is rolled.</summary>
        public int BasePowerPourMoney { get; set; } = 1;
    }

    /// <summary>Bragging Rights economy plus the two status cards.</summary>
    public sealed class SupportingDef
    {
        public IReadOnlyList<int> BraggingRightsPrices { get; set; } = new List<int>();
        public int BraggingRightsPurchaseLimitPerTurn { get; set; } = 1;
    }

    /// <summary>Table-level constants from config.json.</summary>
    public sealed class GameConfig
    {
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int VictoryPointsToTriggerEnd { get; set; }
        public int SaleDieSides { get; set; }
        public int StartingMoney { get; set; }
        public int StartingHandSize { get; set; }
        public int LemonLordDealt { get; set; }
        public int LemonLordKept { get; set; }
        public int BlackMarketFaceUp { get; set; }
        public int BlackMarketFaceUp2Player { get; set; }
        public int TurnStartDraw { get; set; }
        public int ActionsPerTurn { get; set; }
        public int TimeoutHandLimit { get; set; }
        public int BlackMarketRefreshCost { get; set; }
        public int StandCostEscalationPerOwned { get; set; }
    }
}
