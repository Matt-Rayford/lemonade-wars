using System.Collections.Generic;

namespace LemonadeWars.Engine.Core
{
    /// <summary>What kind of thing is sitting on the response stack.</summary>
    public enum StackItemKind
    {
        /// <summary>A plan, attack, or instant Lemon card being played.</summary>
        LemonPlay,
        /// <summary>A Black Market purchase (already paid; refunded if cancelled).</summary>
        BlackMarketPurchase,
    }

    /// <summary>
    /// One pending play/purchase on the response stack. Items resolve last-in-first-out
    /// once every eligible player has passed on the top item. A Tantrum, when it resolves,
    /// cancels the item it was played against; cancelled items pop without effect.
    /// </summary>
    public sealed class StackItem
    {
        public int ItemId { get; set; }
        public StackItemKind Kind { get; set; }
        public int OwnerId { get; set; }
        public bool Cancelled { get; set; }
        /// <summary>True when this play bypassed the action cost (instants, Smear Campaign's follow-up, forced plays).</summary>
        public bool FreePlay { get; set; }

        // ---- LemonPlay ----
        public int? LemonInstanceId { get; set; }
        public string LemonDefId { get; set; } = "";
        /// <summary>For responses (Tantrum/Tag/Rubber): the ItemId this was played against.</summary>
        public int? RespondingToItemId { get; set; }

        // ---- Attack state (mutated by Tag / I'm Rubber, You're Glue) ----
        /// <summary>Current victim of an attack card; may change while on the stack.</summary>
        public int? AttackTargetId { get; set; }
        /// <summary>Original victim, kept so retargeting can detect stale specific targets.</summary>
        public int? OriginalTargetId { get; set; }

        // ---- Play parameters captured from the submitting action ----
        public int? TargetStandInstanceId { get; set; }
        public int? TargetEquippedInstanceId { get; set; }
        public int? TantrumInstanceId { get; set; }
        public int? MarketIndex { get; set; }
        public int? DiscardedLemonInstanceId { get; set; }
        public int? DiscardedBmInstanceId { get; set; }
        public string NewStandTypeId { get; set; } = "";
        public List<int> SelectedInstanceIds { get; set; } = new List<int>();
        public bool DrawInstead { get; set; }
        /// <summary>Tag, You're It!: the player the attack moves to.</summary>
        public int? RedirectTargetId { get; set; }
        /// <summary>Equip destination for effects that play a Black Market card (Connections, etc.).</summary>
        public int? EquipStandInstanceId { get; set; }
        public int? EquipReplaceInstanceId { get; set; }

        // ---- BlackMarketPurchase ----
        public int? BmInstanceId { get; set; }
        /// <summary>Price actually paid, for the refund on cancellation.</summary>
        public int PaidCost { get; set; }
    }

    /// <summary>Why a die is currently waiting for the Out of Stock window to close.</summary>
    public enum RollPurpose
    {
        /// <summary>End-of-turn sale: applies to every player.</summary>
        TurnSale,
        /// <summary>Night Shifts: applies only to the roller.</summary>
        NightShifts,
        /// <summary>Spoiled Rotten's free turn-start roll: applies only to the roller.</summary>
        SpoiledRotten,
        /// <summary>Liquid Energy: an extra sale roll that applies to every player.</summary>
        ExtraSale,
        /// <summary>Trade Winds: an end-of-turn roll for one specific Stand.</summary>
        TradeWinds,
    }

    /// <summary>A die that has been rolled but not yet applied (re-rollable via Out of Stock).</summary>
    public sealed class PendingRoll
    {
        public int Value { get; set; }
        public RollPurpose Purpose { get; set; }
        public int RollerId { get; set; }
        /// <summary>Trade Winds: the single Stand this roll applies to.</summary>
        public int? StandInstanceId { get; set; }
    }

    /// <summary>Cards owed to a player by draw effects; processed front to back.</summary>
    public sealed class PendingDraw
    {
        public int PlayerId { get; set; }
        public int Count { get; set; }
        /// <summary>True when the draw came from a roll trigger (Double Scoop tracking).</summary>
        public bool CountsForRoll { get; set; }
        /// <summary>Record drawn instance ids (Whiniest Baby must discard one of them).</summary>
        public bool TrackDrawnIds { get; set; }
    }

    /// <summary>Per-player accumulators for the current roll episode ("on a single roll" titles).</summary>
    public sealed class RollStats
    {
        /// <summary>Money earned from sales/pours this roll (Big Earner).</summary>
        public int Earned { get; set; }
        /// <summary>Money stolen via roll triggers/traps this roll (Clean Getaway).</summary>
        public int MoneyStolen { get; set; }
        /// <summary>Cards stolen via roll triggers this roll (Sticky Fingers).</summary>
        public int CardsStolen { get; set; }
        /// <summary>Cards drawn and kept from roll triggers this roll (Double Scoop).</summary>
        public int CardsKept { get; set; }
    }

    /// <summary>A window offering the victim of a money steal the chance to play Profit Share.</summary>
    public sealed class PendingTheftResponse
    {
        public int VictimId { get; set; }
        public int AttackerId { get; set; }
        public int AmountStolen { get; set; }
    }

    public enum DecisionKind
    {
        /// <summary>Timeout: discard down to the hand limit. Payload: RequiredCount.</summary>
        DiscardToHandLimit,
        /// <summary>Whiniest Baby turn start: drew 2, discard 1. Payload: RequiredCount = 1.</summary>
        WhiniestBabyDiscard,
        /// <summary>Timeout: pay $3 per played tantrum, selling assets if needed. Payload: RequiredMoney.</summary>
        TimeoutFine,
        /// <summary>Attack moved to a new player needs fresh specifics from its owner. Payload: StackItemId.</summary>
        AttackRetarget,
        /// <summary>Smear Campaign: may play one plan/attack from hand for free (or skip).</summary>
        FreePlayOffer,
        /// <summary>Reverse Engineer: must play the recovered card now.</summary>
        ForcedPlay,
        /// <summary>A steal ability triggered: its owner picks the victim. Payload: SourceInstanceId.</summary>
        AbilityVictim,
        /// <summary>Don's Blessings: pick a card from the victim's revealed hand. Payload: SourceInstanceId, ChosenPlayerId.</summary>
        AbilityPickCard,
        /// <summary>Give the victim a different Lemon card back. Payload: SourceInstanceId, ChosenPlayerId, StolenCardId.</summary>
        AbilityGiveBack,
        /// <summary>Whispers of Fate: discard RequiredCount card(s) after drawing.</summary>
        AbilityDiscard,
        /// <summary>Innovation: choose one of your other Power Pour cards to copy. Payload: SourceInstanceId.</summary>
        InnovationCopy,
        /// <summary>Word of Mouth: choose one of your Stands to sell immediately.</summary>
        WordOfMouthStand,
        /// <summary>Bouncer: you were attacked and may play an attack for free (or skip).</summary>
        BouncerAttack,
    }

    /// <summary>Input the engine is blocked on, outside of response windows.</summary>
    public sealed class PendingDecision
    {
        public int PlayerId { get; set; }
        public DecisionKind Kind { get; set; }
        public int RequiredCount { get; set; }
        public int RequiredMoney { get; set; }
        /// <summary>Stack item the decision belongs to (retarget/forced play contexts).</summary>
        public int? StackItemId { get; set; }
        /// <summary>Card that must be played for ForcedPlay.</summary>
        public int? CardInstanceId { get; set; }
        /// <summary>The equipped Black Market card whose ability raised this decision.</summary>
        public int? SourceInstanceId { get; set; }
        /// <summary>Victim chosen in an earlier stage of the same ability.</summary>
        public int? ChosenPlayerId { get; set; }
        /// <summary>Card stolen in an earlier stage (the give-back must differ from it).</summary>
        public int? StolenCardId { get; set; }
        /// <summary>When set, the answer may only use these cards (Whiniest Baby: the cards just drawn).</summary>
        public List<int>? EligibleCardIds { get; set; }
    }

    /// <summary>A tantrum sitting in a player's pile, with the order it was gained (Whiniest Baby tiebreak).</summary>
    public sealed class TantrumRecord
    {
        public int InstanceId { get; set; }
        public int GainSeq { get; set; }
    }
}
