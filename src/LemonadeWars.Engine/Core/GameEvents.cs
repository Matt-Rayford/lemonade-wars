using System.Collections.Generic;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Facts emitted by the engine as rules resolve. The presentation layer (Unity) animates
    /// from these instead of diffing state; the network layer can relay them to clients.
    /// </summary>
    public abstract class GameEvent
    {
        public override string ToString() => GetType().Name;
    }

    public sealed class StageChanged : GameEvent
    {
        public GameStage Stage { get; set; }
        public override string ToString() => $"Stage -> {Stage}";
    }

    public sealed class TurnStarted : GameEvent
    {
        public int PlayerId { get; set; }
        public override string ToString() => $"P{PlayerId} turn started";
    }

    public sealed class CardDrawn : GameEvent
    {
        public int PlayerId { get; set; }
        public int InstanceId { get; set; }
        public string DefId { get; set; } = "";
        public override string ToString() => $"P{PlayerId} drew {DefId}";
    }

    public sealed class TimeoutDrawn : GameEvent
    {
        public int PlayerId { get; set; }
        public override string ToString() => $"P{PlayerId} drew a Timeout!";
    }

    public sealed class CardsDiscarded : GameEvent
    {
        public int PlayerId { get; set; }
        public List<int> InstanceIds { get; set; } = new List<int>();
        public override string ToString() => $"P{PlayerId} discarded {InstanceIds.Count} card(s)";
    }

    public sealed class MoneyChanged : GameEvent
    {
        public int PlayerId { get; set; }
        /// <summary>Positive = gained, negative = paid.</summary>
        public int Amount { get; set; }
        public string Reason { get; set; } = "";
        public override string ToString() => $"P{PlayerId} {(Amount >= 0 ? "+" : "")}{Amount} ({Reason})";
    }

    public sealed class StandPurchased : GameEvent
    {
        public int PlayerId { get; set; }
        public int StandInstanceId { get; set; }
        public string StandTypeId { get; set; } = "";
        public override string ToString() => $"P{PlayerId} bought {StandTypeId} stand";
    }

    public sealed class BlackMarketPurchased : GameEvent
    {
        public int PlayerId { get; set; }
        public int InstanceId { get; set; }
        public string DefId { get; set; } = "";
        /// <summary>Stand instance id, or null when equipped to Turf.</summary>
        public int? TargetStandInstanceId { get; set; }
        public override string ToString() => $"P{PlayerId} equipped {DefId}";
    }

    public sealed class MarketRefilled : GameEvent
    {
        public List<int> Market { get; set; } = new List<int>();
    }

    public sealed class BraggingRightsPurchased : GameEvent
    {
        public int PlayerId { get; set; }
        public int Price { get; set; }
        public override string ToString() => $"P{PlayerId} bought Bragging Rights for ${Price}";
    }

    public sealed class SaleRolled : GameEvent
    {
        public int PlayerId { get; set; }
        public int Value { get; set; }
        public override string ToString() => $"P{PlayerId} rolled a {Value}";
    }

    public sealed class StandSold : GameEvent
    {
        public int PlayerId { get; set; }
        public int StandInstanceId { get; set; }
        public int Earnings { get; set; }
        public override string ToString() => $"P{PlayerId} stand sold for ${Earnings}";
    }

    public sealed class PowerPourTriggered : GameEvent
    {
        public int PlayerId { get; set; }
        public override string ToString() => $"P{PlayerId} power pour!";
    }

    public sealed class GameEnded : GameEvent
    {
        public List<int> Winners { get; set; } = new List<int>();
        public override string ToString() => $"Game over. Winner(s): {string.Join(", ", Winners)}";
    }
}
