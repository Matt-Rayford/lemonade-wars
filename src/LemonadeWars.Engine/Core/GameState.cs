using System.Collections.Generic;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>High-level lifecycle of a game.</summary>
    public enum GameStage
    {
        /// <summary>Players are choosing which 2 of 3 Lemon Lord titles to keep.</summary>
        ChoosingLemonLords,
        /// <summary>Pre-game snake-draft purchases (must buy a Stand, may buy a Black Market card), twice around.</summary>
        InitialBuys,
        /// <summary>Normal play.</summary>
        Playing,
        /// <summary>A player ended their turn at the VP target; finishing the round so all get equal turns.</summary>
        FinalRound,
        Finished,
    }

    /// <summary>Phases within a single turn (rulebook p6).</summary>
    public enum TurnPhase
    {
        /// <summary>Draw 1 Lemon card (2-discard-1 for the Whiniest Baby).</summary>
        Start,
        /// <summary>Up to 2 actions plus free actions.</summary>
        Play,
        /// <summary>Sale die roll and payouts, then pass to the next player.</summary>
        Sell,
    }

    /// <summary>A physical Lemon card copy. Identity matters for hands/discards.</summary>
    public sealed class LemonCardInstance
    {
        public int InstanceId { get; set; }
        public string DefId { get; set; } = "";
    }

    /// <summary>A physical Black Market card copy. Copies of one def differ by printed shape.</summary>
    public sealed class BlackMarketCardInstance
    {
        public int InstanceId { get; set; }
        public string DefId { get; set; } = "";
        public Shape Shape { get; set; }
    }

    /// <summary>A Stand in play (or the shape of one still in the supply stack).</summary>
    public sealed class StandInstance
    {
        public int InstanceId { get; set; }
        public string StandTypeId { get; set; } = "";
        public Shape Shape { get; set; }
        /// <summary>Instance ids of equipped Black Market cards, in tuck order.</summary>
        public List<int> Equipped { get; } = new List<int>();
    }

    /// <summary>A player's Turf card and everything attached to it.</summary>
    public sealed class TurfState
    {
        public int PowerPourNumber { get; set; }
        /// <summary>Instance ids of equipped Turf Black Market cards (max 5).</summary>
        public List<int> Equipped { get; } = new List<int>();
        /// <summary>Steal the Cashbox trap: lemon instance sitting on this turf, if any.</summary>
        public int? TrapInstanceId { get; set; }
        /// <summary>Who played the trap and collects the stolen earnings.</summary>
        public int? TrapOwnerId { get; set; }
    }

    public sealed class PlayerState
    {
        public int PlayerId { get; set; }
        public string Name { get; set; } = "";
        public int Money { get; set; }
        public TurfState Turf { get; set; } = new TurfState();
        /// <summary>Lemon card instance ids in hand. Hand size is public knowledge; contents are hidden info.</summary>
        public List<int> Hand { get; } = new List<int>();
        public List<StandInstance> Stands { get; } = new List<StandInstance>();
        /// <summary>Tantrums gained by this player (kept below their Turf), with gain order.</summary>
        public List<TantrumRecord> TantrumPile { get; } = new List<TantrumRecord>();
        /// <summary>Lemon Lord titles dealt during setup (3), before the keep-2 choice.</summary>
        public List<string> LemonLordDealt { get; } = new List<string>();
        /// <summary>The 2 secret Lemon Lord titles kept; scored at game end.</summary>
        public List<string> LemonLordKept { get; } = new List<string>();
        /// <summary>First Dibs titles claimed (1 VP each, public).</summary>
        public List<string> FirstDibsClaimed { get; } = new List<string>();
        /// <summary>Bragging Rights cards purchased (1 VP each).</summary>
        public int BraggingRights { get; set; }

        /// <summary>VP visible during play; the game-end trigger checks this (Lemon Lords score later).</summary>
        public int InGameVictoryPoints => FirstDibsClaimed.Count + BraggingRights;
    }

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
        /// <summary>Whether the current initial-buy player has bought their mandatory Stand.</summary>
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
        /// choice is owed even if a Timeout passed the Baby card on mid-draw.</summary>
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

        /// <summary>RNG state; advances with every shuffle and roll.</summary>
        public ulong RngState { get; set; }
    }
}
