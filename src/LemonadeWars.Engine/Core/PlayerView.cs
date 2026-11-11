using System.Collections.Generic;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// What one seat is allowed to see, and the ONLY state object that may cross the network to a
    /// client: the full <see cref="GameState"/> (deck order, other hands, secret titles, RNG
    /// state) stays server-side.
    /// </summary>
    public sealed class PlayerView
    {
        public int ViewerId { get; set; }
        public GameStage Stage { get; set; }
        public TurnPhase Phase { get; set; }
        public int ActivePlayer { get; set; }
        public int FirstPlayer { get; set; }
        public int ActionsRemaining { get; set; }
        public int BmOnlyActionsRemaining { get; set; }
        /// <summary>Seats the game currently waits on (turn, window, or decisions).</summary>
        public List<int> ActingPlayers { get; set; } = new List<int>();
        public int? CurrentInitialBuyer { get; set; }

        public List<PlayerPanel> Players { get; set; } = new List<PlayerPanel>();

        // ---- Viewer-private ----
        public List<CardInfo> Hand { get; set; } = new List<CardInfo>();
        public List<string> LemonLordDealt { get; set; } = new List<string>();
        public List<LordStatus> LemonLordStatus { get; set; } = new List<LordStatus>();
        /// <summary>Don's Blessings mid-resolution: the revealed victim hand, else null.</summary>
        public List<CardInfo>? RevealedHand { get; set; }

        // ---- Shared zones ----
        public int LemonDeckCount { get; set; }
        public List<CardInfo> LemonDiscard { get; set; } = new List<CardInfo>();
        public int BlackMarketDeckCount { get; set; }
        public List<CardInfo> Market { get; set; } = new List<CardInfo>();
        /// <summary>Price the viewer would pay per market slot (Peddlin' Pete included).</summary>
        public List<int> MarketPrices { get; set; } = new List<int>();
        public List<CardInfo> BlackMarketDiscard { get; set; } = new List<CardInfo>();
        public List<string> FirstDibsRow { get; set; } = new List<string>();
        public Dictionary<string, int> StandSupplyCounts { get; set; } = new Dictionary<string, int>();
        /// <summary>Shape on top of each non-empty supply stack — the stand you would get.</summary>
        public Dictionary<string, Shape> SupplyTopShapes { get; set; } = new Dictionary<string, Shape>();
        /// <summary>Price the viewer would pay per stand type (ownership escalation included).</summary>
        public Dictionary<string, int> SupplyPrices { get; set; } = new Dictionary<string, int>();
        public int? NextBraggingRightsPrice { get; set; }
        /// <summary>The shelf slot a contested purchase left empty: Market is one short and the gap sits here.</summary>
        public int? MarketGapIndex { get; set; }
        public int? WhiniestBabyHolder { get; set; }
        public int? SpoiledRottenHolder { get; set; }

        // ---- Interaction surface ----
        public List<int> AwaitingResponse { get; set; } = new List<int>();
        public int? PendingRollValue { get; set; }
        public RollPurpose? PendingRollPurpose { get; set; }
        public int? PendingRollerId { get; set; }
        /// <summary>Trade Winds only: the single stand this roll applies to (null = not stand-scoped).</summary>
        public int? PendingRollStandInstanceId { get; set; }
        /// <summary>Top of the response stack, when a window is open.</summary>
        public StackTopInfo? StackTop { get; set; }
        public List<DecisionInfo> MyDecisions { get; set; } = new List<DecisionInfo>();
        /// <summary>Who owes a Timeout fine right now — the whole table sees the card — and how much.</summary>
        public int? TimeoutFinePlayerId { get; set; }
        public int TimeoutFineAmount { get; set; }
        /// <summary>A money steal against the viewer awaits a Profit Share response; the other Theft fields are set only then.</summary>
        public bool TheftOnMe { get; set; }
        public int? TheftAttackerId { get; set; }
        public int? TheftAmount { get; set; }
        /// <summary>What Profit Share would claw back right now: half rounded up, capped by what the attacker still holds.</summary>
        public int? TheftRecoverable { get; set; }
        public List<int> Winners { get; set; } = new List<int>();

        /// <summary>One player's public board.</summary>
        public sealed class PlayerPanel
        {
            public int PlayerId { get; set; }
            public string Name { get; set; } = "";
            public int Money { get; set; }
            public int HandCount { get; set; }
            public int TantrumCount { get; set; }
            public int TurfPowerPourNumber { get; set; }
            /// <summary>All pour numbers including Spiked Lemonades.</summary>
            public List<int> PourNumbers { get; set; } = new List<int>();
            public List<CardInfo> TurfEquipped { get; set; } = new List<CardInfo>();
            public bool TurfTrapped { get; set; }
            public List<StandPanel> Stands { get; set; } = new List<StandPanel>();
            public List<string> FirstDibsClaimed { get; set; } = new List<string>();
            public int BraggingRights { get; set; }
            public int InGameVictoryPoints { get; set; }
            /// <summary>Kept Lemon Lords — revealed to everyone once the game is Finished.</summary>
            public List<LordStatus> FinishedLords { get; set; } = new List<LordStatus>();
        }

        public sealed class StandPanel
        {
            public int InstanceId { get; set; }
            public string StandTypeId { get; set; } = "";
            public Shape Shape { get; set; }
            public List<CardInfo> Equipped { get; set; } = new List<CardInfo>();
            public List<int> SaleNumbers { get; set; } = new List<int>();
            public int Earnings { get; set; }
        }

        public sealed class CardInfo
        {
            public int InstanceId { get; set; }
            public string DefId { get; set; } = "";
            /// <summary>Printed shape for Black Market copies; null for Lemon cards.</summary>
            public Shape? Shape { get; set; }
        }

        public sealed class LordStatus
        {
            public string TitleId { get; set; } = "";
            public bool Met { get; set; }
        }

        public sealed class StackTopInfo
        {
            public int OwnerId { get; set; }
            public bool IsPurchase { get; set; }
            public string DefId { get; set; } = "";
            public Shape? Shape { get; set; }
            public int? AttackTargetId { get; set; }
            /// <summary>Finders Keepers / That's Not Fair: the equipped card being taken (public table info).</summary>
            public string? StolenDefId { get; set; }
            public Shape? StolenShape { get; set; }
            /// <summary>Connections / Reduce and Reuse / Reverse Engineer: the shelf or
            /// discard card being taken — a Lemon card when TakenIsLemon.</summary>
            public string? TakenDefId { get; set; }
            public Shape? TakenShape { get; set; }
            public bool TakenIsLemon { get; set; }
            /// <summary>Responses (Tantrum/Tag/Rubber): the stack item this answers.</summary>
            public string? RespondingToDefId { get; set; }
            public int? RespondingToOwnerId { get; set; }
            public bool RespondingToIsPurchase { get; set; }
            public Shape? RespondingToShape { get; set; }
            /// <summary>Tag, You're It!: where the attack is being sent.</summary>
            public int? RedirectTargetId { get; set; }
        }

        public sealed class DecisionInfo
        {
            public DecisionKind Kind { get; set; }
            public int RequiredCount { get; set; }
            public int RequiredMoney { get; set; }
            public int? StolenCardId { get; set; }
            public int? ChosenPlayerId { get; set; }
            public int? CardInstanceId { get; set; }
            public int? StackItemId { get; set; }
            /// <summary>The equipped Black Market card whose ability raised this decision.</summary>
            public int? SourceInstanceId { get; set; }
            /// <summary>The play that provoked this decision (Bouncer: the attack).</summary>
            public string? ContextDefId { get; set; }
            public int? ContextPlayerId { get; set; }
            /// <summary>When set, only these hand cards may answer (Whiniest Baby's drawn pair).</summary>
            public List<int>? EligibleCardIds { get; set; }
        }
    }
}
