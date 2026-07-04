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
        /// <summary>Tantrum instance ids played by this player, kept below their Turf.</summary>
        public List<int> TantrumsPlayed { get; } = new List<int>();
        /// <summary>Lemon Lord titles dealt during setup (3), before the keep-2 choice.</summary>
        public List<string> LemonLordDealt { get; } = new List<string>();
        /// <summary>The 2 secret Lemon Lord titles kept; scored at game end.</summary>
        public List<string> LemonLordKept { get; } = new List<string>();
        /// <summary>First Dibs titles claimed (1 VP each, public).</summary>
        public List<string> FirstDibsClaimed { get; } = new List<string>();
        /// <summary>Bragging Rights cards purchased (1 VP each).</summary>
        public int BraggingRights { get; set; }
        public bool HasWhiniestBaby { get; set; }
        public bool HasSpoiledRotten { get; set; }

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

        // ---- End of game ----
        /// <summary>Player whose turn-end hit the VP target and triggered the final round.</summary>
        public int? EndTriggeredBy { get; set; }
        public List<int> Winners { get; } = new List<int>();

        /// <summary>RNG state; advances with every shuffle and roll.</summary>
        public ulong RngState { get; set; }
    }
}
