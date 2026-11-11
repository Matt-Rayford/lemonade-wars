using System.Collections.Generic;
using System.Linq;
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
        private const int TimeoutFinePerTantrum = 3;

        // ----------------------------------------------------- draw queue

        private void ApplyDrawLemonCard(DrawLemonCard action, List<GameEvent> events)
        {
            RequireTurnAction(action);
            SpendAction();
            QueueDraws(action.PlayerId, 1, reason: DrawReason.Action);
            Pump(events);
        }

        private void QueueDraws(int playerId, int count, bool countsForRoll = false,
            bool trackDrawnIds = false, DrawReason reason = DrawReason.Effect,
            int discardAfterCount = 0, int? discardAfterSourceInstanceId = null)
        {
            State.PendingDraws.Add(new PendingDraw
            {
                PlayerId = playerId,
                Count = count,
                CountsForRoll = countsForRoll,
                TrackDrawnIds = trackDrawnIds,
                Reason = reason,
                DiscardAfterCount = discardAfterCount,
                DiscardAfterSourceInstanceId = discardAfterSourceInstanceId,
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
                    bool pausedByTimeout = !DrawOneCard(player, entry.Reason, events);
                    if (player.Hand.Count > handBefore)
                    {
                        RecordDrawnCard(entry, player);
                    }
                    if (pausedByTimeout)
                    {
                        // The replacement is owed on this SAME entry, so its tracking,
                        // roll-counting and queue position carry over. Not when only
                        // Timeouts remain: the same one would be drawn forever.
                        if (AnyRealLemonCardToDraw())
                        {
                            entry.Count++;
                        }
                        return;
                    }
                    if (entry.CountsForRoll)
                    {
                        StatsFor(player.PlayerId).CardsKept++;
                    }
                }
                State.PendingDraws.Remove(entry);
                RaiseDiscardOfDrawn(entry, player, events);
            }
        }

        private void RecordDrawnCard(PendingDraw entry, PlayerState player)
        {
            int drawnId = player.Hand[player.Hand.Count - 1];
            entry.DrawnIds.Add(drawnId);
            if (entry.TrackDrawnIds)
            {
                State.TrackedDrawnCards.Add(drawnId);
            }
        }

        /// <summary>
        /// "+N cards, then discard 1 of them": the discard is restricted to the cards
        /// this batch actually delivered (a Timeout may have thinned them already), and
        /// skipped when the deck ran dry and nothing arrived.
        /// </summary>
        private void RaiseDiscardOfDrawn(PendingDraw entry, PlayerState player, List<GameEvent> events)
        {
            if (entry.DiscardAfterCount <= 0)
            {
                return;
            }
            var eligible = entry.DrawnIds.Where(player.Hand.Contains).ToList();
            if (eligible.Count == 0)
            {
                return;
            }
            AddDecision(new PendingDecision
            {
                PlayerId = player.PlayerId,
                Kind = DecisionKind.AbilityDiscard,
                RequiredCount = System.Math.Min(entry.DiscardAfterCount, eligible.Count),
                SourceInstanceId = entry.DiscardAfterSourceInstanceId,
                EligibleCardIds = eligible,
            }, events);
        }

        /// <summary>A non-Timeout card remains in the deck or the discard it would reshuffle from.</summary>
        private bool AnyRealLemonCardToDraw() =>
            State.LemonDeck.Concat(State.LemonDiscard)
                .Any(id => Db.Lemon(State.LemonInstances[id].DefId).Type != LemonCardType.Timeout);

        /// <summary>Returns false when a Timeout was drawn and paused the game; true otherwise, dry deck included.</summary>
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

        /// <summary>A Timeout was drawn: everyone discards to the hand limit and the Baby is fined (rulebook p12).</summary>
        private void BeginTimeout(PlayerState drawer, int timeoutInstanceId, List<GameEvent> events)
        {
            events.Add(new TimeoutDrawn { PlayerId = drawer.PlayerId });
            State.TimeoutDrawerId = drawer.PlayerId;
            State.LemonDiscard.Add(timeoutInstanceId);

            RaiseHandLimitDiscards(events);
            RaiseTimeoutFine(events);
        }

        private void RaiseHandLimitDiscards(List<GameEvent> events)
        {
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
        }

        private void RaiseTimeoutFine(List<GameEvent> events)
        {
            if (!(State.WhiniestBabyHolder is int babyId))
            {
                return;
            }
            int fine = TimeoutFinePerTantrum * Player(babyId).TantrumPile.Count;
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
