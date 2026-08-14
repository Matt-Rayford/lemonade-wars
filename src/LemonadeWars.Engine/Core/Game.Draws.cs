using System.Collections.Generic;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// The draw queue and the Timeout episode a draw can trigger. Draws are queued rather
    /// than immediate so a drawn Timeout can pause the whole pipeline mid-batch, run its
    /// discard/fine decisions, and resume owing the drawer a replacement card.
    /// </summary>
    public sealed partial class Game
    {
        // ----------------------------------------------------- draw queue

        private void QueueDraws(int playerId, int count, bool countsForRoll = false,
            bool trackDrawnIds = false, DrawReason reason = DrawReason.Effect)
        {
            State.PendingDraws.Add(new PendingDraw
            {
                PlayerId = playerId,
                Count = count,
                CountsForRoll = countsForRoll,
                TrackDrawnIds = trackDrawnIds,
                Reason = reason,
            });
        }

        private void ProcessPendingDraws(List<GameEvent> events)
        {
            while (State.PendingDraws.Count > 0)
            {
                var entry = State.PendingDraws[0];
                var player = Player(entry.PlayerId);
                while (entry.Count > 0)
                {
                    entry.Count--;
                    int handBefore = player.Hand.Count;
                    bool drewCard = DrawOneCard(player, entry.Reason, events);
                    if (entry.TrackDrawnIds && player.Hand.Count > handBefore)
                    {
                        State.TrackedDrawnCards.Add(player.Hand[player.Hand.Count - 1]);
                    }
                    if (drewCard && entry.CountsForRoll)
                    {
                        StatsFor(player.PlayerId).CardsKept++;
                    }
                    if (!drewCard)
                    {
                        // Timeout interrupted: the drawer is owed a replacement, on
                        // this SAME entry — so tracking (the Whiniest Baby's keep
                        // pool), roll-counting, and queue position all carry over.
                        // The baby always ends up choosing between real cards.
                        entry.Count++;
                        return;
                    }
                }
                State.PendingDraws.Remove(entry);
            }
        }

        /// <summary>Draw one card for the player. Returns false when a Timeout paused the game.</summary>
        private bool DrawOneCard(PlayerState player, DrawReason reason, List<GameEvent> events)
        {
            if (State.LemonDeck.Count == 0)
            {
                State.LemonDeck.AddRange(State.LemonDiscard);
                State.LemonDiscard.Clear();
                _rng.Shuffle(State.LemonDeck);
                if (State.LemonDeck.Count == 0)
                {
                    return true;
                }
            }

            int instanceId = State.LemonDeck[0];
            State.LemonDeck.RemoveAt(0);
            var def = Db.Lemon(State.LemonInstances[instanceId].DefId);

            if (def.Type != LemonCardType.Timeout)
            {
                player.Hand.Add(instanceId);
                events.Add(new CardDrawn
                {
                    PlayerId = player.PlayerId,
                    InstanceId = instanceId,
                    DefId = def.Id,
                    Reason = reason,
                });
                return true;
            }

            BeginTimeout(player, instanceId, events);
            return false;
        }

        // --------------------------------------------------------- timeout

        /// <summary>A Timeout was drawn: open discard/fine decisions, stash the card (rulebook p12).</summary>
        private void BeginTimeout(PlayerState drawer, int timeoutInstanceId, List<GameEvent> events)
        {
            events.Add(new TimeoutDrawn { PlayerId = drawer.PlayerId });
            State.TimeoutDrawerId = drawer.PlayerId;
            State.LemonDiscard.Add(timeoutInstanceId);
            // The replacement draw is owed on the interrupted entry itself (see
            // ProcessPendingDraws), so its tracking and position carry over.

            foreach (var player in State.Players)
            {
                int excess = player.Hand.Count - Db.Config.TimeoutHandLimit;
                if (excess > 0)
                {
                    State.PendingDecisions.Add(new PendingDecision
                    {
                        PlayerId = player.PlayerId,
                        Kind = DecisionKind.DiscardToHandLimit,
                        RequiredCount = excess,
                    });
                    events.Add(new DecisionRequired
                    {
                        PlayerId = player.PlayerId,
                        Kind = DecisionKind.DiscardToHandLimit,
                    });
                }
            }

            if (State.WhiniestBabyHolder is int babyId)
            {
                var baby = Player(babyId);
                int fine = 3 * baby.TantrumPile.Count;
                if (fine > 0)
                {
                    State.PendingDecisions.Add(new PendingDecision
                    {
                        PlayerId = babyId,
                        Kind = DecisionKind.TimeoutFine,
                        RequiredMoney = fine,
                    });
                    events.Add(new DecisionRequired { PlayerId = babyId, Kind = DecisionKind.TimeoutFine });
                }
            }
        }

        /// <summary>All Timeout decisions settled: discard the baby's tantrums and pass the card on.</summary>
        private void FinishTimeout(List<GameEvent> events)
        {
            State.TimeoutDrawerId = null;

            if (State.WhiniestBabyHolder is int babyId)
            {
                var baby = Player(babyId);
                foreach (var record in baby.TantrumPile)
                {
                    State.LemonDiscard.Add(record.InstanceId);
                }
                baby.TantrumPile.Clear();

                // Pass to whoever now has the most tantrums, or back to the market (rulebook p12).
                int? newHolder = BabyHolderByRule();
                if (newHolder != State.WhiniestBabyHolder)
                {
                    events.Add(new WhiniestBabyMoved { FromPlayerId = babyId, ToPlayerId = newHolder });
                    State.WhiniestBabyHolder = newHolder;
                }
            }
        }
    }
}
