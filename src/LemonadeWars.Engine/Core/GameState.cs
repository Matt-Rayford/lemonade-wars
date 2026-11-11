using System.Collections.Generic;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Complete, serializable game state. Pure data — all rules live in <see cref="Game"/>.
    /// Snapshotting this object (plus the RNG state inside) is enough to resume or replay a game.
    /// </summary>
    public sealed class GameState
    {
        public GameStage Stage { get; set; } = GameStage.ChoosingLemonLords;
        public TurnPhase Phase { get; set; } = TurnPhase.Start;

        public List<PlayerState> Players { get; } = new List<PlayerState>();
        public int FirstPlayer { get; set; }
        public int ActivePlayer { get; set; }

        // ---- Turn-scoped counters ----
        public int ActionsRemaining { get; set; }
        public bool MarketRefreshUsedThisTurn { get; set; }
        public bool BraggingRightsBoughtThisTurn { get; set; }

        // ---- Card zones (instance ids; index 0 = top of deck) ----
        public List<int> LemonDeck { get; } = new List<int>();
        public List<int> LemonDiscard { get; } = new List<int>();
        public List<int> BlackMarketDeck { get; } = new List<int>();
        /// <summary>Face-up Black Market row.</summary>
        public List<int> Market { get; } = new List<int>();
        public List<int> BlackMarketDiscard { get; } = new List<int>();

        /// <summary>Face-up First Dibs titles still claimable (playerCount + 1 dealt at setup).</summary>
        public List<string> FirstDibsRow { get; } = new List<string>();

        /// <summary>Shapes remaining in each stand supply stack, keyed by stand type id; index 0 = top.</summary>
        public Dictionary<string, List<Shape>> StandSupply { get; } = new Dictionary<string, List<Shape>>();

        /// <summary>Number of Bragging Rights sold so far; prices escalate with each sale.</summary>
        public int BraggingRightsSold { get; set; }

        // ---- Instance tables ----
        public Dictionary<int, LemonCardInstance> LemonInstances { get; } = new Dictionary<int, LemonCardInstance>();
        public Dictionary<int, BlackMarketCardInstance> BlackMarketInstances { get; } = new Dictionary<int, BlackMarketCardInstance>();
        public int NextInstanceId { get; set; } = 1;

        // ---- Setup-only bookkeeping ----
        /// <summary>Player ids in snake-draft order for initial buys; consumed front to back.</summary>
        public List<int> InitialBuyQueue { get; } = new List<int>();
        public bool InitialBuyStandDone { get; set; }

        // ---- Interaction machinery ----
        /// <summary>Pending plays/purchases, bottom-first. Non-empty means a response window is open on the last item.</summary>
        public List<StackItem> ResponseStack { get; } = new List<StackItem>();
        public int NextStackItemId { get; set; } = 1;
        /// <summary>Players who may still respond to the current window (stack top, roll, or theft).</summary>
        public List<int> AwaitingResponse { get; } = new List<int>();
        /// <summary>A rolled die waiting out its Out of Stock window before applying.</summary>
        public PendingRoll? PendingRoll { get; set; }
        /// <summary>Profit Share windows after money steals, processed front to back.</summary>
        public List<PendingTheftResponse> TheftQueue { get; } = new List<PendingTheftResponse>();
        /// <summary>Blocking player decisions (discards, fines, retargets, free plays).</summary>
        public List<PendingDecision> PendingDecisions { get; } = new List<PendingDecision>();
        /// <summary>Draws still owed, possibly to several players (e.g. sale triggers); FIFO.</summary>
        public List<PendingDraw> PendingDraws { get; } = new List<PendingDraw>();
        /// <summary>Instance ids drawn by a TrackDrawnIds draw (Whiniest Baby's discard pool).</summary>
        public List<int> TrackedDrawnCards { get; } = new List<int>();
        /// <summary>The active player started this turn as the Whiniest Baby: the keep-1
        /// choice is owed even if a Timeout passes the Baby card on mid-draw.</summary>
        public bool BabyDiscardOwed { get; set; }
        /// <summary>True while resolving a turn-start (Spoiled Rotten roll / Whiniest Baby draw) before Play.</summary>
        public bool TurnStartInProgress { get; set; }
        /// <summary>Progress through turn-start steps: 0 = Spoiled Rotten roll, 1 = draws, 2 = baby discard, 3 = enter Play.</summary>
        public int TurnStartStep { get; set; }
        /// <summary>Bumped on every stack push/pop/reroll; a window recomputes its audience when it differs from LastWindowRevision.</summary>
        public int InteractionRevision { get; set; }
        public int LastWindowRevision { get; set; } = -1;
        /// <summary>What to run once the pending roll (and its aftermath) fully settles.</summary>
        public RollPurpose? PostRollContinuation { get; set; }
        /// <summary>Set while a drawn Timeout is being resolved; null otherwise.</summary>
        public int? TimeoutDrawerId { get; set; }
        /// <summary>Whether any tantrum was gained during the current stack episode (Whiniest Baby check).</summary>
        public bool EpisodeHadTantrums { get; set; }
        /// <summary>Monotonic counter ordering tantrum gains (Whiniest Baby tiebreak).</summary>
        public int NextTantrumGainSeq { get; set; } = 1;

        // ---- Black Market ability bookkeeping ----
        /// <summary>Equipped instance ids whose once-per-turn ability was used this turn.</summary>
        public List<int> UsedTurnAbilities { get; } = new List<int>();
        /// <summary>Extra buy actions usable only for Black Market purchases (Shopping Spree).</summary>
        public int BmOnlyActionsRemaining { get; set; }
        /// <summary>Trade Winds: stands still owed their end-of-turn roll.</summary>
        public List<int> TradeWindsQueue { get; } = new List<int>();
        public bool TradeWindsBuilt { get; set; }

        // ---- Title trackers ----
        /// <summary>Per-player accumulators for the current roll episode, keyed by player id.</summary>
        public Dictionary<int, RollStats> RollStats { get; } = new Dictionary<int, RollStats>();
        /// <summary>Money spent by the active player this turn, excluding Bragging Rights (Shopaholic).</summary>
        public int SpentThisTurn { get; set; }

        // ---- Status cards ----
        public int? WhiniestBabyHolder { get; set; }
        public int? SpoiledRottenHolder { get; set; }

        // ---- End of game ----
        /// <summary>Player whose turn-end hit the VP target and triggered the final round.</summary>
        public int? EndTriggeredBy { get; set; }
        public List<int> Winners { get; } = new List<int>();

        public ulong RngState { get; set; }
    }
}
