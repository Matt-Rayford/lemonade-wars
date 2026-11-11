using System.Collections.Generic;

namespace LemonadeWars.Engine.Core
{
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
        /// <summary>
        /// This item has had its response window: the stack now resolves top-down without
        /// asking again. Reset when the Tantrum answering it is itself Tantrummed, and when
        /// an attack is re-aimed at a new victim.
        /// </summary>
        public bool WindowOffered { get; set; }

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
        /// <summary>The shelf slot the card left; it stays empty until the purchase resolves, then refills in place.</summary>
        public int? VacatedMarketIndex { get; set; }
    }

    public enum StackItemKind
    {
        /// <summary>A plan, attack, or instant Lemon card being played.</summary>
        LemonPlay,
        /// <summary>A Black Market purchase (already paid; refunded if cancelled).</summary>
        BlackMarketPurchase,
    }
}
