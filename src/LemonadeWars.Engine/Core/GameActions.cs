using System.Collections.Generic;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// A player decision submitted to the engine. Actions are the ONLY way state changes
    /// after setup — the same pipeline will serve local UI, AI opponents, and network play.
    /// </summary>
    public abstract class GameAction
    {
        public int PlayerId { get; set; }
    }

    // ---------------------------------------------------------------- setup

    /// <summary>Keep exactly 2 of the 3 Lemon Lord titles dealt to you (rulebook p5).</summary>
    public sealed class ChooseLemonLords : GameAction
    {
        public List<string> KeepTitleIds { get; set; } = new List<string>();
    }

    /// <summary>
    /// One pick in the pre-game snake draft: buy the mandatory Stand, optionally a
    /// Black Market card, then pass (rulebook p5).
    /// </summary>
    public sealed class InitialBuyStand : GameAction
    {
        public string StandTypeId { get; set; } = "";
    }

    /// <summary>Optional Black Market purchase during the initial draft, or pass by ending.</summary>
    public sealed class InitialBuyEnd : GameAction
    {
    }

    // ------------------------------------------------------------ main turn

    /// <summary>Action: draw 1 Lemon card.</summary>
    public sealed class DrawLemonCard : GameAction
    {
    }

    /// <summary>Action: purchase and place a Stand (base price + $1 per Stand already owned).</summary>
    public sealed class BuyStand : GameAction
    {
        public string StandTypeId { get; set; } = "";
    }

    /// <summary>Action: purchase a face-up Black Market card and equip it immediately.</summary>
    public sealed class BuyBlackMarket : GameAction
    {
        /// <summary>Index into the face-up market row.</summary>
        public int MarketIndex { get; set; }
        /// <summary>Target Stand instance id for Stand upgrades; null for Turf upgrades.</summary>
        public int? TargetStandInstanceId { get; set; }
        /// <summary>Equipped card instance id to discard when the target is at its slot limit.</summary>
        public int? ReplaceInstanceId { get; set; }
    }

    /// <summary>Action: buy the top Bragging Rights card (limit 1 per turn).</summary>
    public sealed class BuyBraggingRights : GameAction
    {
    }

    /// <summary>Free action, once per turn: pay $1 to discard and refill the Black Market row.</summary>
    public sealed class RefreshMarket : GameAction
    {
    }

    /// <summary>End the Play phase: roll the sale die, resolve payouts, pass the turn.</summary>
    public sealed class EndTurn : GameAction
    {
    }

    // -------------------------------------------------------- lemon plays

    /// <summary>
    /// Play a plan or attack from hand (1 action; instants respond through
    /// <see cref="RespondToWindow"/> instead). Card-specific parameters ride along and are
    /// validated per card. Also used to satisfy FreePlayOffer/ForcedPlay decisions.
    /// </summary>
    public sealed class PlayLemonCard : GameAction
    {
        public int CardInstanceId { get; set; }

        /// <summary>Victim for attacks (Taxes, Trash Pandas, ...).</summary>
        public int? TargetPlayerId { get; set; }
        /// <summary>Own stand (Doorbuster Sale, Rebrand).</summary>
        public int? TargetStandInstanceId { get; set; }
        /// <summary>An equipped Black Market card (That's Not Fair!, Finders Keepers).</summary>
        public int? TargetEquippedInstanceId { get; set; }
        /// <summary>A tantrum in a pile (Apologize: own; Blame Changer: own, given to target).</summary>
        public int? TantrumInstanceId { get; set; }
        /// <summary>Market row index (Connections).</summary>
        public int? MarketIndex { get; set; }
        /// <summary>A card in the Black Market discard (Reduce and Reuse).</summary>
        public int? DiscardedBmInstanceId { get; set; }
        /// <summary>A card in the Lemon discard (Reverse Engineer).</summary>
        public int? DiscardedLemonInstanceId { get; set; }
        /// <summary>Reverse Engineer: true to draw 2 instead of recovering a card.</summary>
        public bool DrawInstead { get; set; }
        /// <summary>New stand type (Rebrand).</summary>
        public string NewStandTypeId { get; set; } = "";
        /// <summary>Own equipped Black Market cards to sell/discard (Rummage Sale, Rebrand overflow).</summary>
        public List<int> SelectedInstanceIds { get; set; } = new List<int>();
        /// <summary>Where an acquired Black Market card gets equipped (Connections, Finders Keepers, Reduce and Reuse).</summary>
        public int? EquipStandInstanceId { get; set; }
        public int? EquipReplaceInstanceId { get; set; }
    }

    /// <summary>Skip an optional FreePlayOffer decision (Smear Campaign follow-up).</summary>
    public sealed class SkipFreePlay : GameAction
    {
    }

    // ---------------------------------------------------- window responses

    /// <summary>
    /// Play an instant into the open window: Tantrum / Tag, You're It! / I'm Rubber, You're
    /// Glue against the stack top, Out of Stock against a pending roll, Profit Share against
    /// a resolved theft. Free — never costs an action.
    /// </summary>
    public sealed class RespondToWindow : GameAction
    {
        public int CardInstanceId { get; set; }
        /// <summary>Tag, You're It!: the player the attack moves to.</summary>
        public int? RedirectTargetId { get; set; }
    }

    /// <summary>Decline to respond to the current window.</summary>
    public sealed class PassWindow : GameAction
    {
    }

    // ---------------------------------------------------------- decisions

    /// <summary>Discard specific hand cards (Timeout hand limit, Whiniest Baby turn-start).</summary>
    public sealed class SubmitDiscard : GameAction
    {
        public List<int> InstanceIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// Pay a Timeout fine, first selling the listed assets at full base price
    /// (Black Market cards to the discard, Stands back under their stack).
    /// </summary>
    public sealed class SubmitTimeoutPayment : GameAction
    {
        public List<int> SellStandInstanceIds { get; set; } = new List<int>();
        public List<int> SellBmInstanceIds { get; set; } = new List<int>();
    }

    /// <summary>Supply fresh specifics after your attack was Tagged to a new player.</summary>
    public sealed class SubmitRetarget : GameAction
    {
        public int StackItemId { get; set; }
        /// <summary>New equipped-card target (That's Not Fair!, Finders Keepers).</summary>
        public int? TargetEquippedInstanceId { get; set; }
        public int? EquipStandInstanceId { get; set; }
        public int? EquipReplaceInstanceId { get; set; }
    }
}
