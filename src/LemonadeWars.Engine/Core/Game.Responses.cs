using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Response stack, windows, and pending-decision machinery.
    ///
    /// The model is a simplified Magic-style stack: plays and Black Market purchases go on
    /// <see cref="GameState.ResponseStack"/>; a window invites eligible players to respond
    /// (Tantrum / Tag / Rubber-Glue against the top item, Out of Stock against a pending
    /// roll, Profit Share against a resolved theft). Responses push new items. When everyone
    /// passes, the top item resolves; Tantrums mark their target cancelled, and cancelled
    /// items pop without effect (purchases get their money back — not the action).
    ///
    /// <see cref="Pump"/> is the single driver that advances everything until player input
    /// is required. Players who cannot legally respond are auto-passed, so uncontested plays
    /// resolve synchronously inside one Apply call. The response cards themselves live in
    /// Game.ResponseCards.cs.
    /// </summary>
    public sealed partial class Game
    {
        private const string TantrumId = "tantrum";
        private const string TagId = "tag-youre-it";
        private const string RubberGlueId = "im-rubber-youre-glue";
        private const string OutOfStockId = "out-of-stock";
        private const string ProfitShareId = "profit-share";

        /// <summary>Card defs that the rules exempt from tantrums.</summary>
        private static readonly HashSet<string> Untantrummable = new HashSet<string>
        {
            "apologize", // card text: "This card cannot be tantrummed"
        };

        // ------------------------------------------------------------- pump

        /// <summary>Advance all pending machinery until the game needs player input.</summary>
        private void Pump(List<GameEvent> events)
        {
            while (true)
            {
                // Concurrent decisions can invalidate each other (two steal abilities racing
                // over the same near-empty hand); drop the ones that can no longer be answered.
                PruneStaleDecisions();

                // A finished Timeout episode completes once its decisions are settled.
                if (State.TimeoutDrawerId != null && State.PendingDecisions.Count == 0)
                {
                    FinishTimeout(events);
                    continue;
                }

                if (State.PendingDecisions.Count > 0)
                {
                    return; // blocked on explicit decisions
                }

                if (State.ResponseStack.Count > 0)
                {
                    var top = State.ResponseStack[State.ResponseStack.Count - 1];
                    if (top.Cancelled)
                    {
                        PopCancelled(top, events);
                        continue;
                    }
                    if (!EnsureWindow(events))
                    {
                        return; // window open, waiting on players
                    }
                    if (!ResolveTopItem(events))
                    {
                        return; // resolution paused (retarget decision)
                    }
                    continue;
                }

                // Stack fully unwound: settle Whiniest Baby before anything else proceeds.
                if (State.EpisodeHadTantrums)
                {
                    State.EpisodeHadTantrums = false;
                    ReassignWhiniestBaby(events);
                    continue;
                }

                if (State.PendingDraws.Count > 0)
                {
                    ProcessPendingDraws(events);
                    continue;
                }

                if (State.TheftQueue.Count > 0)
                {
                    if (!EnsureWindow(events))
                    {
                        return;
                    }
                    State.TheftQueue.RemoveAt(0); // window closed with no split
                    BumpRevision();
                    continue;
                }

                if (State.PendingRoll != null)
                {
                    if (!EnsureWindow(events))
                    {
                        return;
                    }
                    FinalizePendingRoll(events);
                    continue;
                }

                if (State.PostRollContinuation is RollPurpose purpose)
                {
                    State.PostRollContinuation = null;
                    if (purpose == RollPurpose.TurnSale || purpose == RollPurpose.TradeWinds)
                    {
                        FinishEndTurn(events);
                    }
                    // NightShifts / SpoiledRotten / ExtraSale: nothing further to run.
                    continue;
                }

                if (State.TurnStartInProgress)
                {
                    AdvanceTurnStart(events);
                    continue;
                }

                return; // quiescent: normal actions are legal again
            }
        }

        private void BumpRevision() => State.InteractionRevision++;

        /// <summary>Remove pending decisions whose answers no longer exist.</summary>
        private void PruneStaleDecisions()
        {
            for (int i = State.PendingDecisions.Count - 1; i >= 0; i--)
            {
                var decision = State.PendingDecisions[i];
                var player = Player(decision.PlayerId);
                bool stale = false;
                switch (decision.Kind)
                {
                    case DecisionKind.AbilityPickCard:
                        stale = Player(decision.ChosenPlayerId!.Value).Hand.Count == 0;
                        break;
                    case DecisionKind.AbilityGiveBack:
                        stale = !player.Hand.Any(id => id != decision.StolenCardId);
                        break;
                    case DecisionKind.AbilityDiscard:
                    case DecisionKind.DiscardToHandLimit:
                        if (player.Hand.Count < decision.RequiredCount)
                        {
                            decision.RequiredCount = player.Hand.Count;
                            stale = decision.RequiredCount == 0;
                        }
                        break;
                    case DecisionKind.WhiniestBabyDiscard:
                        // Restricted to the drawn cards; drop any that left the hand since.
                        decision.EligibleCardIds = decision.EligibleCardIds?
                            .Where(player.Hand.Contains).ToList();
                        int eligibleCount = decision.EligibleCardIds?.Count ?? player.Hand.Count;
                        if (eligibleCount < decision.RequiredCount)
                        {
                            decision.RequiredCount = eligibleCount;
                            stale = decision.RequiredCount == 0;
                        }
                        break;
                    case DecisionKind.WordOfMouthStand:
                        stale = player.Stands.Count == 0;
                        break;
                    case DecisionKind.InnovationCopy:
                        stale = !player.Turf.Equipped.Any(id =>
                            id != decision.SourceInstanceId &&
                            EquippedDef(id).Timing == Data.EffectTiming.PowerPour &&
                            EquippedDef(id).Id != "innovation");
                        break;
                }
                if (stale)
                {
                    State.PendingDecisions.RemoveAt(i);
                }
            }
        }

        // ----------------------------------------------------------- windows

        /// <summary>
        /// Make sure the current context's window has been opened for this revision.
        /// Returns true when the window is already settled (nobody left to ask).
        /// </summary>
        private bool EnsureWindow(List<GameEvent> events)
        {
            if (State.LastWindowRevision != State.InteractionRevision)
            {
                State.LastWindowRevision = State.InteractionRevision;
                State.AwaitingResponse.Clear();
                State.AwaitingResponse.AddRange(EligibleResponders());
                if (State.AwaitingResponse.Count > 0)
                {
                    events.Add(new ResponseWindowOpened
                    {
                        AwaitingPlayers = State.AwaitingResponse.ToList(),
                        Context = CurrentWindowContext(),
                    });
                }
            }
            return State.AwaitingResponse.Count == 0;
        }

        private string CurrentWindowContext()
        {
            if (State.ResponseStack.Count > 0)
            {
                var top = State.ResponseStack[State.ResponseStack.Count - 1];
                return top.Kind == StackItemKind.BlackMarketPurchase
                    ? $"purchase:{State.BlackMarketInstances[top.BmInstanceId!.Value].DefId}"
                    : $"play:{top.LemonDefId}";
            }
            if (State.TheftQueue.Count > 0)
            {
                return "theft";
            }
            if (State.PendingRoll != null)
            {
                return $"roll:{State.PendingRoll.Value}";
            }
            return "none";
        }

        /// <summary>Who may respond to the current context — players holding a qualifying card.</summary>
        private IEnumerable<int> EligibleResponders()
        {
            bool Holds(PlayerState p, string defId) =>
                p.Hand.Any(id => State.LemonInstances[id].DefId == defId);

            if (State.ResponseStack.Count > 0)
            {
                var top = State.ResponseStack[State.ResponseStack.Count - 1];
                bool tantrummable = IsTantrummable(top);
                bool isAttack = IsAttackItem(top);
                foreach (var p in State.Players)
                {
                    if (p.PlayerId == top.OwnerId)
                    {
                        continue; // cannot respond to your own play/purchase
                    }
                    // Inflatable Decoy defends its owner only: you must BE the target.
                    bool hasDecoy = isAttack && top.AttackTargetId == p.PlayerId &&
                        p.Turf.Equipped.Any(id => EquippedDef(id).Id == "inflatable-decoy");
                    if ((tantrummable && Holds(p, TantrumId)) ||
                        (isAttack && (Holds(p, TagId) || Holds(p, RubberGlueId))) ||
                        hasDecoy)
                    {
                        yield return p.PlayerId;
                    }
                }
                yield break;
            }

            if (State.TheftQueue.Count > 0)
            {
                var theft = State.TheftQueue[0];
                var victim = State.Players[theft.VictimId];
                if (Holds(victim, ProfitShareId))
                {
                    yield return victim.PlayerId;
                }
                yield break;
            }

            if (State.PendingRoll != null)
            {
                foreach (var p in State.Players)
                {
                    // Out of Stock from hand, or the active player's unused die abilities
                    // (Downsell / Sugared Up / Take Two).
                    if (Holds(p, OutOfStockId) ||
                        (p.PlayerId == State.ActivePlayer && HasUnusedRollAbility(p)))
                    {
                        yield return p.PlayerId;
                    }
                }
            }
        }

        private bool IsTantrummable(StackItem item) =>
            item.Kind == StackItemKind.BlackMarketPurchase || !Untantrummable.Contains(item.LemonDefId);

        private bool IsAttackItem(StackItem item) =>
            item.Kind == StackItemKind.LemonPlay &&
            Db.Lemon(item.LemonDefId).Type == LemonCardType.Attack &&
            !item.Cancelled;

        // -------------------------------------------------- window actions

        private void ApplyPassWindow(PassWindow action, List<GameEvent> events)
        {
            if (!State.AwaitingResponse.Remove(action.PlayerId))
            {
                throw new InvalidActionException($"P{action.PlayerId} is not being awaited by any window.");
            }
            Pump(events);
        }

        private void ApplyRespondToWindow(RespondToWindow action, List<GameEvent> events)
        {
            if (!State.AwaitingResponse.Contains(action.PlayerId))
            {
                throw new InvalidActionException($"P{action.PlayerId} is not being awaited by any window.");
            }
            var player = Player(action.PlayerId);

            // Inflatable Decoy: an equipped reaction, not a hand card. Immediate and
            // untantrummable (card text): the attack dies on the spot, so does the decoy.
            if (action.EquippedInstanceId is int decoyId)
            {
                if (!player.Turf.Equipped.Contains(decoyId) ||
                    EquippedDef(decoyId).Id != "inflatable-decoy")
                {
                    throw new InvalidActionException("Only an equipped Inflatable Decoy can respond this way.");
                }
                var top = RequireStackTop("Inflatable Decoy");
                if (!IsAttackItem(top))
                {
                    throw new InvalidActionException("Inflatable Decoy discards an attack that was just played.");
                }
                if (top.OwnerId == player.PlayerId)
                {
                    throw new InvalidActionException("You cannot react to your own attack.");
                }
                if (top.AttackTargetId != player.PlayerId)
                {
                    throw new InvalidActionException(
                        "Inflatable Decoy only protects its owner — you must be the attack's target.");
                }
                top.Cancelled = true;
                player.Turf.Equipped.Remove(decoyId);
                State.BlackMarketDiscard.Add(decoyId);
                events.Add(new PlayCancelled
                {
                    OwnerId = top.OwnerId,
                    DefId = top.LemonDefId,
                    ByPlayerId = player.PlayerId,
                    WithDefId = "inflatable-decoy",
                });
                events.Add(new CardsDiscarded { PlayerId = player.PlayerId, InstanceIds = new List<int> { decoyId } });
                BumpRevision();
                Pump(events);
                return;
            }

            if (!player.Hand.Contains(action.CardInstanceId))
            {
                throw new InvalidActionException("That card is not in your hand.");
            }
            string defId = State.LemonInstances[action.CardInstanceId].DefId;

            switch (defId)
            {
                case TantrumId:
                    RespondWithTantrum(player, action, events);
                    break;
                case TagId:
                    RespondWithTag(player, action, events);
                    break;
                case RubberGlueId:
                    RespondWithRubberGlue(player, action, events);
                    break;
                case OutOfStockId:
                    RespondWithOutOfStock(player, action, events);
                    break;
                case ProfitShareId:
                    RespondWithProfitShare(player, action, events);
                    break;
                default:
                    throw new InvalidActionException($"{defId} is not a window response card.");
            }
            Pump(events);
        }

        private StackItem RequireStackTop(string forWhat)
        {
            if (State.ResponseStack.Count == 0)
            {
                throw new InvalidActionException($"{forWhat} needs a pending play or purchase to respond to.");
            }
            return State.ResponseStack[State.ResponseStack.Count - 1];
        }

        // ---------------------------------------------------- stack machinery

        private void PushStackItem(StackItem item, List<GameEvent> events)
        {
            item.ItemId = State.NextStackItemId++;
            State.ResponseStack.Add(item);
            BumpRevision();
            if (item.Kind == StackItemKind.LemonPlay)
            {
                var answered = item.RespondingToItemId is int answeredId
                    ? State.ResponseStack.FirstOrDefault(s => s.ItemId == answeredId)
                    : null;
                BlackMarketCardInstance? stolen = null;
                if (item.TargetEquippedInstanceId is int stolenId)
                {
                    State.BlackMarketInstances.TryGetValue(stolenId, out stolen);
                }
                var (takenDefId, takenShape, takenIsLemon) = TakenByPlay(item);
                events.Add(new LemonCardPlayed
                {
                    PlayerId = item.OwnerId,
                    InstanceId = item.LemonInstanceId ?? 0,
                    DefId = item.LemonDefId,
                    TargetPlayerId = item.AttackTargetId,
                    RespondingToOwnerId = answered?.OwnerId,
                    RespondingToDefId = answered == null ? null : StackItemDefId(answered),
                    StolenDefId = stolen?.DefId,
                    StolenShape = stolen?.Shape,
                    TakenDefId = takenDefId,
                    TakenShape = takenShape,
                    TakenIsLemon = takenIsLemon,
                });
            }
        }

        /// <summary>The card a stack item IS: the bought Black Market card, or the played Lemon card.</summary>
        private string StackItemDefId(StackItem item) =>
            item.Kind == StackItemKind.BlackMarketPurchase
                ? State.BlackMarketInstances[item.BmInstanceId!.Value].DefId
                : item.LemonDefId;

        /// <summary>
        /// The card a take is aimed at — a shelf card (Connections), a discarded Black
        /// Market card (Reduce and Reuse) or a discarded Lemon card (Reverse Engineer) —
        /// so the table can show the loot beside the play.
        /// </summary>
        private (string? DefId, Shape? Shape, bool IsLemon) TakenByPlay(StackItem item)
        {
            if (item.MarketIndex is int shelfIndex && shelfIndex >= 0 && shelfIndex < State.Market.Count)
            {
                var shelfCard = State.BlackMarketInstances[State.Market[shelfIndex]];
                return (shelfCard.DefId, shelfCard.Shape, false);
            }
            if (item.DiscardedBmInstanceId is int discardedBm &&
                State.BlackMarketInstances.TryGetValue(discardedBm, out var discardedBmCard))
            {
                return (discardedBmCard.DefId, discardedBmCard.Shape, false);
            }
            if (item.DiscardedLemonInstanceId is int discardedLemon &&
                State.LemonInstances.TryGetValue(discardedLemon, out var discardedLemonCard))
            {
                return (discardedLemonCard.DefId, null, true);
            }
            return (null, null, false);
        }

        /// <summary>Resolve the top stack item. Returns false if resolution paused on a decision.</summary>
        private bool ResolveTopItem(List<GameEvent> events)
        {
            var top = State.ResponseStack[State.ResponseStack.Count - 1];

            if (top.Kind == StackItemKind.BlackMarketPurchase)
            {
                CompleteBlackMarketPurchase(top, events);
                PopResolved(top, events);
                return true;
            }

            switch (top.LemonDefId)
            {
                case TantrumId:
                    ResolveTantrum(top, events);
                    PopResolved(top, events);
                    return true;
                case TagId:
                    ResolveTag(top, events);
                    PopResolved(top, events);
                    return true;
                case RubberGlueId:
                    ResolveRubberGlue(top, events);
                    PopResolved(top, events);
                    return true;
                case OutOfStockId:
                    ResolveOutOfStock(top, events);
                    PopResolved(top, events);
                    return true;
                case ProfitShareId:
                    ResolveProfitShare(top, events);
                    PopResolved(top, events);
                    return true;
                default:
                    // Plans and attacks: card-specific handlers (Game.LemonEffects.cs).
                    // May pause for an AttackRetarget decision after a Tag.
                    if (!ResolveLemonPlay(top, events))
                    {
                        return false;
                    }
                    bool wasAttack = Db.Lemon(top.LemonDefId).Type == LemonCardType.Attack;
                    PopResolved(top, events);
                    if (wasAttack)
                    {
                        // Bouncer: the victim of a resolved attack may fire back for free.
                        CheckBouncer(top, events);
                    }
                    return true;
            }
        }

        private StackItem? FindStackItem(int? itemId) =>
            itemId == null ? null : State.ResponseStack.FirstOrDefault(i => i.ItemId == itemId);

        /// <summary>Pop an item that resolved; its card (if any, and not a tantrum/trap) goes to the discard.</summary>
        private void PopResolved(StackItem item, List<GameEvent> events)
        {
            State.ResponseStack.Remove(item);
            BumpRevision();
            if (item.Kind == StackItemKind.LemonPlay)
            {
                events.Add(new PlayResolved { OwnerId = item.OwnerId, DefId = item.LemonDefId });
                DiscardPlayedLemon(item);
            }
        }

        /// <summary>Pop a cancelled item: purchases refund money; cards go to the discard.</summary>
        private void PopCancelled(StackItem item, List<GameEvent> events)
        {
            State.ResponseStack.Remove(item);
            // The window that was open on this item dies with it. An Inflatable Decoy
            // cancels WITHOUT pushing a new item, so nothing else would recompute the
            // wait — and the players still listed would be shown an empty "Pass"
            // window for a play that no longer exists.
            State.AwaitingResponse.Clear();
            BumpRevision();
            if (item.Kind == StackItemKind.BlackMarketPurchase)
            {
                var buyer = Player(item.OwnerId);
                buyer.Money += item.PaidCost;
                events.Add(new MoneyChanged
                {
                    PlayerId = buyer.PlayerId,
                    Amount = item.PaidCost,
                    Reason = "purchase refunded",
                });
                State.BlackMarketDiscard.Add(item.BmInstanceId!.Value);
            }
            else
            {
                DiscardPlayedLemon(item);
            }
        }

        private void DiscardPlayedLemon(StackItem item)
        {
            if (item.LemonInstanceId is int id)
            {
                string defId = State.LemonInstances[id].DefId;
                // Tantrums live in piles; a placed Steal the Cashbox trap lives on a turf.
                if (defId != TantrumId && !IsPlacedTrap(id))
                {
                    State.LemonDiscard.Add(id);
                }
            }
        }

        private bool IsPlacedTrap(int instanceId) =>
            State.Players.Any(p => p.Turf.TrapInstanceId == instanceId);
    }
}
