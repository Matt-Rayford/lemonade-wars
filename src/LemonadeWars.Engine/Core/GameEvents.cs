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

    // ------------------------------------------------------- interactions

    public sealed class LemonCardPlayed : GameEvent
    {
        public int PlayerId { get; set; }
        public int InstanceId { get; set; }
        public string DefId { get; set; } = "";
        /// <summary>Victim, when the card is an attack.</summary>
        public int? TargetPlayerId { get; set; }
        public override string ToString() => $"P{PlayerId} played {DefId}" +
            (TargetPlayerId != null ? $" -> P{TargetPlayerId}" : "");
    }

    public sealed class ResponseWindowOpened : GameEvent
    {
        public List<int> AwaitingPlayers { get; set; } = new List<int>();
        public string Context { get; set; } = "";
        public override string ToString() => $"Window ({Context}): awaiting {string.Join(",", AwaitingPlayers)}";
    }

    public sealed class PlayCancelled : GameEvent
    {
        public int OwnerId { get; set; }
        public string DefId { get; set; } = "";
        public override string ToString() => $"P{OwnerId}'s {DefId} was cancelled";
    }

    public sealed class PlayResolved : GameEvent
    {
        public int OwnerId { get; set; }
        public string DefId { get; set; } = "";
        public override string ToString() => $"P{OwnerId}'s {DefId} resolved";
    }

    public sealed class AttackRedirected : GameEvent
    {
        public int ByPlayerId { get; set; }
        public int NewTargetId { get; set; }
        public override string ToString() => $"P{ByPlayerId} tagged the attack to P{NewTargetId}";
    }

    public sealed class AttackReflected : GameEvent
    {
        public int ByPlayerId { get; set; }
        public int NewTargetId { get; set; }
        public override string ToString() => $"P{ByPlayerId} reflected the attack onto P{NewTargetId}";
    }

    public sealed class AttackFizzled : GameEvent
    {
        public int OwnerId { get; set; }
        public string DefId { get; set; } = "";
        public override string ToString() => $"P{OwnerId}'s {DefId} fizzled (no valid target)";
    }

    public sealed class MoneyStolen : GameEvent
    {
        public int FromPlayerId { get; set; }
        public int ToPlayerId { get; set; }
        public int Amount { get; set; }
        public string Reason { get; set; } = "";
        public override string ToString() => $"P{ToPlayerId} stole ${Amount} from P{FromPlayerId} ({Reason})";
    }

    public sealed class CardsStolen : GameEvent
    {
        public int FromPlayerId { get; set; }
        public int ToPlayerId { get; set; }
        public int Count { get; set; }
        public override string ToString() => $"P{ToPlayerId} stole {Count} card(s) from P{FromPlayerId}";
    }

    public sealed class HandsTraded : GameEvent
    {
        public int PlayerA { get; set; }
        public int PlayerB { get; set; }
        public override string ToString() => $"P{PlayerA} and P{PlayerB} traded hands";
    }

    public sealed class DieRerolled : GameEvent
    {
        public int ByPlayerId { get; set; }
        public int NewValue { get; set; }
        public override string ToString() => $"P{ByPlayerId} rerolled -> {NewValue}";
    }

    public sealed class WhiniestBabyMoved : GameEvent
    {
        public int? FromPlayerId { get; set; }
        public int? ToPlayerId { get; set; }
        public override string ToString() => $"Whiniest Baby -> {(ToPlayerId is int p ? $"P{p}" : "market")}";
    }

    public sealed class SpoiledRottenMoved : GameEvent
    {
        public int? FromPlayerId { get; set; }
        public int? ToPlayerId { get; set; }
        public override string ToString() => $"Spoiled Rotten -> {(ToPlayerId is int p ? $"P{p}" : "market")}";
    }

    public sealed class DecisionRequired : GameEvent
    {
        public int PlayerId { get; set; }
        public DecisionKind Kind { get; set; }
        public override string ToString() => $"P{PlayerId} must decide: {Kind}";
    }

    public sealed class TrapPlaced : GameEvent
    {
        public int OwnerId { get; set; }
        public int OnPlayerId { get; set; }
        public override string ToString() => $"P{OwnerId} placed a trap on P{OnPlayerId}'s turf";
    }
}
