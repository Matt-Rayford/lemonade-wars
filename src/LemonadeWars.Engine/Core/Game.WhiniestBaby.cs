using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
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
        /// Designer rulings: most tantrums; a tie goes to whoever most recently PLAYED one (a Blame
        /// Changer transfer doesn't count), then to the incumbent. No tantrums anywhere: nobody.
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
