using System.Collections.Generic;
using LemonadeWars.Engine.Data;

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

    // -------------------------------------------------- game and turn flow

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

    /// <summary>
    /// Raised by whoever hosts the table (the server's action timer), never by the rules: the
    /// actions that follow it were made on the player's behalf.
    /// </summary>
    public sealed class ActionTimerExpired : GameEvent
    {
        public int PlayerId { get; set; }
        public override string ToString() => $"P{PlayerId} ran out of time";
    }

    public sealed class DecisionRequired : GameEvent
    {
        public int PlayerId { get; set; }
        public DecisionKind Kind { get; set; }
        public override string ToString() => $"P{PlayerId} must decide: {Kind}";
    }

    public sealed class GameEnded : GameEvent
    {
        public List<int> Winners { get; set; } = new List<int>();
        /// <summary>Final victory points per player (in-game VP + met Lemon Lord titles).</summary>
        public Dictionary<int, int> Scores { get; set; } = new Dictionary<int, int>();
        public override string ToString() => $"Game over. Winner(s): {string.Join(", ", Winners)}";
    }

    // --------------------------------------------------------------- cards

    public sealed class CardDrawn : GameEvent
    {
        public int PlayerId { get; set; }
        public int InstanceId { get; set; }
        public string DefId { get; set; } = "";
        /// <summary>Public (everyone sees an action being spent), so it survives the per-seat
        /// redaction that strips the card's identity.</summary>
        public DrawReason Reason { get; set; }
        public override string ToString() => $"P{PlayerId} drew {DefId} ({Reason})";
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

    // ------------------------------------------------- money and purchases

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
        /// <summary>Dollars paid; 0 when the stand arrived by a Rebrand swap.</summary>
        public int Price { get; set; }
        public override string ToString() => $"P{PlayerId} bought {StandTypeId} stand";
    }

    /// <summary>
    /// An in-game purchase was paid for and is open to a Tantrum: its shelf slot stays empty until
    /// the purchase resolves (BlackMarketPurchased, or a cancel) and MarketRefilled follows.
    /// </summary>
    public sealed class BlackMarketPurchaseStarted : GameEvent
    {
        public int PlayerId { get; set; }
        public int InstanceId { get; set; }
        public string DefId { get; set; } = "";
        public Shape Shape { get; set; }
        public int MarketIndex { get; set; }
        public int Price { get; set; }
        public override string ToString() => $"P{PlayerId} is buying {DefId}";
    }

    public sealed class BlackMarketPurchased : GameEvent
    {
        public int PlayerId { get; set; }
        public int InstanceId { get; set; }
        public string DefId { get; set; } = "";
        /// <summary>Stand instance id, or null when equipped to Turf.</summary>
        public int? TargetStandInstanceId { get; set; }
        /// <summary>Dollars paid; 0 for free takes (Connections, steals, discard recoveries).</summary>
        public int Price { get; set; }
        public override string ToString() => $"P{PlayerId} equipped {DefId}";
    }

    public sealed class MarketRefilled : GameEvent
    {
        public List<int> Market { get; set; } = new List<int>();
        /// <summary>Set only when a player paid to refresh the whole row; null for the
        /// ordinary refill that follows a purchase.</summary>
        public int? RefreshedByPlayerId { get; set; }
        public override string ToString() => RefreshedByPlayerId is int p
            ? $"P{p} refreshed the Black Market"
            : "Market refilled";
    }

    public sealed class BraggingRightsPurchased : GameEvent
    {
        public int PlayerId { get; set; }
        public int Price { get; set; }
        public override string ToString() => $"P{PlayerId} bought Bragging Rights for ${Price}";
    }

    // ---------------------------------------------------------- sale rolls

    public sealed class SaleRolled : GameEvent
    {
        public int PlayerId { get; set; }
        public int Value { get; set; }
        /// <summary>Shown to the table: a player must be able to tell a Night Shifts roll from
        /// the turn sale before spending Out of Stock. Travels as camelCase ("nightShifts").</summary>
        public RollPurpose Purpose { get; set; }
        /// <summary>Trade Winds only: the single stand this roll applies to (null = not stand-scoped).</summary>
        public int? StandInstanceId { get; set; }
        public override string ToString() => $"P{PlayerId} rolled a {Value} ({Purpose})";
    }

    public sealed class RollModified : GameEvent
    {
        public int PlayerId { get; set; }
        public string SourceDefId { get; set; } = "";
        public int NewValue { get; set; }
        public override string ToString() => $"P{PlayerId} used {SourceDefId} -> {NewValue}";
    }

    public sealed class DieRerolled : GameEvent
    {
        public int ByPlayerId { get; set; }
        public int NewValue { get; set; }
        public override string ToString() => $"P{ByPlayerId} rerolled -> {NewValue}";
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

    // ---------------------------------------------- black market abilities

    public sealed class AbilityTriggered : GameEvent
    {
        public int PlayerId { get; set; }
        public string DefId { get; set; } = "";
        public override string ToString() => $"P{PlayerId}'s {DefId} triggered";
    }

    public sealed class AbilityDeclined : GameEvent
    {
        public int PlayerId { get; set; }
        public string DefId { get; set; } = "";
        public override string ToString() => $"P{PlayerId} passed on {DefId}";
    }

    // --------------------------------------------- titles and status cards

    public sealed class TitleClaimed : GameEvent
    {
        public int PlayerId { get; set; }
        public string TitleId { get; set; } = "";
        public override string ToString() => $"P{PlayerId} claimed {TitleId}!";
    }

    public sealed class LemonLordMet : GameEvent
    {
        public int PlayerId { get; set; }
        public string TitleId { get; set; } = "";
        public override string ToString() => $"P{PlayerId} fulfilled {TitleId}";
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

    // ------------------------------------------- lemon plays and responses

    public sealed class LemonCardPlayed : GameEvent
    {
        public int PlayerId { get; set; }
        public int InstanceId { get; set; }
        public string DefId { get; set; } = "";
        /// <summary>Victim, when the card is an attack.</summary>
        public int? TargetPlayerId { get; set; }
        /// <summary>Responses (Tantrum, Tag, Rubber Glue): the owner and def id of the play or
        /// purchase this answers, so a reveal can say "on Jeremy's Smear Campaign".</summary>
        public int? RespondingToOwnerId { get; set; }
        public string? RespondingToDefId { get; set; }
        /// <summary>Tag, You're It!: who the attack will move to if the Tag is not stopped.</summary>
        public int? RedirectTargetId { get; set; }
        /// <summary>Finders Keepers / That's Not Fair: the equipped Black Market card the play
        /// is aimed at (public table info), shown beside the attack.</summary>
        public string? StolenDefId { get; set; }
        public Shape? StolenShape { get; set; }
        /// <summary>Connections / Reduce and Reuse / Reverse Engineer: the shelf or discard card
        /// the play takes. Null for a Reverse Engineer that draws instead.</summary>
        public string? TakenDefId { get; set; }
        public Shape? TakenShape { get; set; }
        public bool TakenIsLemon { get; set; }
        public override string ToString() => $"P{PlayerId} played {DefId}" +
            (TargetPlayerId != null ? $" -> P{TargetPlayerId}" : "") +
            (RespondingToDefId != null ? $" (vs P{RespondingToOwnerId}'s {RespondingToDefId})" : "");
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
        /// <summary>Who cancelled it, and with what: an Inflatable Decoy fires with no play
        /// event of its own.</summary>
        public int? ByPlayerId { get; set; }
        public string? WithDefId { get; set; }
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

    // ----------------------------------------------------- attack outcomes

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
        /// <summary>An equipped Black Market card taken off a board (Finders Keepers), not Lemon cards from a hand.</summary>
        public bool IsBlackMarketCard { get; set; }
        public override string ToString() => $"P{ToPlayerId} stole {Count} card(s) from P{FromPlayerId}";
    }

    public sealed class HandsTraded : GameEvent
    {
        public int PlayerA { get; set; }
        public int PlayerB { get; set; }
        public override string ToString() => $"P{PlayerA} and P{PlayerB} traded hands";
    }

    public sealed class TrapPlaced : GameEvent
    {
        public int OwnerId { get; set; }
        public int OnPlayerId { get; set; }
        public override string ToString() => $"P{OwnerId} placed a trap on P{OnPlayerId}'s turf";
    }
}
