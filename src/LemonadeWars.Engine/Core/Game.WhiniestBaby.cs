using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Tantrum piles and the Whiniest Baby status card they feed: who holds the Baby,
    /// and when it moves. The Baby's turn-start draw penalty runs in Game.TurnFlow.cs;
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
        /// Who the Whiniest Baby belongs to right now (designer rulings): most
        /// tantrums; tie goes to whoever most recently PLAYED a tantrum (receiving
        /// one via Blame Changer doesn't count); a still-standing tie sticks with the
        /// incumbent; and with no tantrums anywhere, NOBODY is the baby.
        /// </summary>
        private int? BabyHolderByRule()
        {
            int max = State.Players.Max(p => p.TantrumPile.Count);
            if (max == 0)
            {
                return null;
            }
            return State.Players
                .Where(p => p.TantrumPile.Count == max)
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
