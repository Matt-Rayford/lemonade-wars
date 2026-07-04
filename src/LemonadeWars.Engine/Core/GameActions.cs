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
}
