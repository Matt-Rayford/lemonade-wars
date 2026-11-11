using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Tantrum piles and the Whiniest Baby status card they feed: who holds the Baby,
    /// and when it moves. The Baby's turn-start extra draw runs in Game.TurnFlow.cs;
    /// its Timeout fine in Game.Draws.cs.
    /// </summary>
    public sealed partial class Game
    {
        private void GainTantrum(PlayerState player, int instanceId, List<GameEvent> events)
        {
            player.TantrumPile.Add(new TantrumRecord
            {
                InstanceId = instanceId,
                GainSeq = State.NextTantrumGainSeq++,
            });
            State.EpisodeHadTantrums = true;
        }

        /// <summary>
        /// Designer rulings: most tantrums; a tie goes to whoever most recently PLAYED a
        /// tantrum (receiving one via Blame Changer doesn't count); a still-standing tie
        /// sticks with the incumbent; with no tantrums anywhere, nobody is the baby.
        /// </summary>
        private int? BabyHolderByRule()
        {
            int mostTantrums = State.Players.Max(p => p.TantrumPile.Count);
            if (mostTantrums == 0)
            {
                return null;
            }
            return State.Players
                .Where(p => p.TantrumPile.Count == mostTantrums)
                .OrderByDescending(p => p.LastTantrumPlaySeq)
                .ThenByDescending(p => p.PlayerId == State.WhiniestBabyHolder)
                .ThenBy(p => p.PlayerId)
                .First().PlayerId;
        }

        private void ReassignWhiniestBaby(List<GameEvent> events)
        {
            int? holder = BabyHolderByRule();
            if (State.WhiniestBabyHolder != holder)
            {
                events.Add(new WhiniestBabyMoved
                {
                    FromPlayerId = State.WhiniestBabyHolder,
                    ToPlayerId = holder,
                });
                State.WhiniestBabyHolder = holder;
            }
        }
    }
}
