using System.Collections.Generic;

namespace LemonadeWars.Engine.Core
{
    /// <summary>Cards owed to a player by draw effects; processed front to back.</summary>
    public sealed class PendingDraw
    {
        public int PlayerId { get; set; }
        public int Count { get; set; }
        /// <summary>True when the draw came from a roll trigger (Double Scoop tracking).</summary>
        public bool CountsForRoll { get; set; }
        /// <summary>Record drawn instance ids (Whiniest Baby must discard one of them).</summary>
        public bool TrackDrawnIds { get; set; }
        /// <summary>Every card this batch has put in the hand so far (Timeout resumes included).</summary>
        public List<int> DrawnIds { get; } = new List<int>();
        /// <summary>
        /// Whispers of Fate: once the batch lands, its drawer discards this many OF THE DRAWN
        /// CARDS (0 = none). The decision is created then: the ids do not exist at queue time.
        /// </summary>
        public int DiscardAfterCount { get; set; }
        /// <summary>The equipped card behind <see cref="DiscardAfterCount"/>, for the decision's source.</summary>
        public int? DiscardAfterSourceInstanceId { get; set; }
        /// <summary>What caused this batch — stamped onto every CardDrawn it produces.</summary>
        public DrawReason Reason { get; set; }
    }
}
