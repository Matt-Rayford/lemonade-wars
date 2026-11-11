using System.Collections.Generic;

namespace LemonadeWars.Engine.Core
{
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
        /// <summary>NextTantrumGainSeq stamp of the last tantrum this player PLAYED: the Whiniest
        /// Baby tie-break. Gifts (Blame Changer) don't count; it outlives the played copy.</summary>
        public int LastTantrumPlaySeq { get; set; }
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
}
