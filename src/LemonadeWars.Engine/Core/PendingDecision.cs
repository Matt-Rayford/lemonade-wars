using System.Collections.Generic;

namespace LemonadeWars.Engine.Core
{
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
        /// <summary>The play that provoked this decision. Bouncer's resolved attack is
        /// already off the stack, so its def and owner ride here for the UI.</summary>
        public string? ContextDefId { get; set; }
        public int? ContextPlayerId { get; set; }
        /// <summary>Victim chosen in an earlier stage of the same ability.</summary>
        public int? ChosenPlayerId { get; set; }
        /// <summary>Card stolen in an earlier stage (the give-back must differ from it).</summary>
        public int? StolenCardId { get; set; }
        /// <summary>When set, the answer may only use these cards (Whiniest Baby: the cards just drawn).</summary>
        public List<int>? EligibleCardIds { get; set; }
    }
}
