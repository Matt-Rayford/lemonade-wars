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
    /// resolve synchronously inside one Apply call.
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
                        PopAndDiscard(top, events);
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

                if (State.PendingDrawCount > 0)
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
                    if (purpose == RollPurpose.TurnSale)
                    {
                        FinishEndTurn(events);
                    }
                    // NightShifts / SpoiledRotten: nothing further; turn-start advances below.
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
                    if ((tantrummable && Holds(p, TantrumId)) ||
                        (isAttack && (Holds(p, TagId) || Holds(p, RubberGlueId))))
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
                    if (Holds(p, OutOfStockId))
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

        private void RespondWithTantrum(PlayerState player, RespondToWindow action, List<GameEvent> events)
        {
            var top = RequireStackTop("Tantrum");
            if (top.OwnerId == player.PlayerId)
            {
                throw new InvalidActionException("You cannot tantrum your own card.");
            }
            if (!IsTantrummable(top))
            {
                throw new InvalidActionException("That cannot be tantrummed.");
            }

            player.Hand.Remove(action.CardInstanceId);
            // The tantrum is gained the moment it is thrown — even if later cancelled (rulebook p11).
            GainTantrum(player, action.CardInstanceId, events);

            PushStackItem(new StackItem
            {
                Kind = StackItemKind.LemonPlay,
                OwnerId = player.PlayerId,
                LemonInstanceId = action.CardInstanceId,
                LemonDefId = TantrumId,
                RespondingToItemId = top.ItemId,
                FreePlay = true,
            }, events);
        }

        private void RespondWithTag(PlayerState player, RespondToWindow action, List<GameEvent> events)
        {
            var top = RequireStackTop("Tag, You're It!");
            if (!IsAttackItem(top))
            {
                throw new InvalidActionException("Tag, You're It! responds to an attack.");
            }
            if (top.OwnerId == player.PlayerId)
            {
                throw new InvalidActionException("You cannot react to your own attack.");
            }
            // Any player may tag (designer ruling), but the new target may not be the
            // attacker (card text) — reflecting onto the attacker is Rubber-Glue's job.
            // In a 2-player game the attack is discarded instead.
            if (State.Players.Count > 2)
            {
                if (!(action.RedirectTargetId is int newTarget) || newTarget < 0 ||
                    newTarget >= State.Players.Count || newTarget == top.AttackTargetId)
                {
                    throw new InvalidActionException("Tag needs a new target different from the current one.");
                }
                if (newTarget == top.OwnerId)
                {
                    throw new InvalidActionException(
                        "Tag cannot move the attack onto the attacker (card text).");
                }
            }

            player.Hand.Remove(action.CardInstanceId);
            PushStackItem(new StackItem
            {
                Kind = StackItemKind.LemonPlay,
                OwnerId = player.PlayerId,
                LemonInstanceId = action.CardInstanceId,
                LemonDefId = TagId,
                RespondingToItemId = top.ItemId,
                RedirectTargetId = action.RedirectTargetId,
                FreePlay = true,
            }, events);
        }

        private void RespondWithRubberGlue(PlayerState player, RespondToWindow action, List<GameEvent> events)
        {
            var top = RequireStackTop("I'm Rubber, You're Glue");
            if (!IsAttackItem(top))
            {
                throw new InvalidActionException("I'm Rubber, You're Glue responds to an attack.");
            }
            if (top.OwnerId == player.PlayerId)
            {
                throw new InvalidActionException("You cannot react to your own attack.");
            }

            player.Hand.Remove(action.CardInstanceId);
            PushStackItem(new StackItem
            {
                Kind = StackItemKind.LemonPlay,
                OwnerId = player.PlayerId,
                LemonInstanceId = action.CardInstanceId,
                LemonDefId = RubberGlueId,
                RespondingToItemId = top.ItemId,
                FreePlay = true,
            }, events);
        }

        private void RespondWithOutOfStock(PlayerState player, RespondToWindow action, List<GameEvent> events)
        {
            if (State.ResponseStack.Count > 0 || State.PendingRoll == null)
            {
                throw new InvalidActionException("Out of Stock responds to a die that was just rolled.");
            }

            player.Hand.Remove(action.CardInstanceId);
            PushStackItem(new StackItem
            {
                Kind = StackItemKind.LemonPlay,
                OwnerId = player.PlayerId,
                LemonInstanceId = action.CardInstanceId,
                LemonDefId = OutOfStockId,
                FreePlay = true,
            }, events);
        }

        private void RespondWithProfitShare(PlayerState player, RespondToWindow action, List<GameEvent> events)
        {
            if (State.ResponseStack.Count > 0 || State.TheftQueue.Count == 0)
            {
                throw new InvalidActionException("Profit Share responds to a resolved theft.");
            }
            if (State.TheftQueue[0].VictimId != player.PlayerId)
            {
                throw new InvalidActionException("Only the robbed player may play Profit Share.");
            }

            player.Hand.Remove(action.CardInstanceId);
            PushStackItem(new StackItem
            {
                Kind = StackItemKind.LemonPlay,
                OwnerId = player.PlayerId,
                LemonInstanceId = action.CardInstanceId,
                LemonDefId = ProfitShareId,
                FreePlay = true,
            }, events);
        }

        private void PushStackItem(StackItem item, List<GameEvent> events)
        {
            item.ItemId = State.NextStackItemId++;
            State.ResponseStack.Add(item);
            BumpRevision();
            if (item.Kind == StackItemKind.LemonPlay)
            {
                events.Add(new LemonCardPlayed
                {
                    PlayerId = item.OwnerId,
                    InstanceId = item.LemonInstanceId ?? 0,
                    DefId = item.LemonDefId,
                    TargetPlayerId = item.AttackTargetId,
                });
            }
        }

        // -------------------------------------------------------- resolution

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
                    PopResolved(top, events);
                    return true;
            }
        }

        private StackItem? FindStackItem(int? itemId) =>
            itemId == null ? null : State.ResponseStack.FirstOrDefault(i => i.ItemId == itemId);

        private void ResolveTantrum(StackItem tantrum, List<GameEvent> events)
        {
            var target = FindStackItem(tantrum.RespondingToItemId);
            if (target != null && !target.Cancelled)
            {
                target.Cancelled = true;
                events.Add(new PlayCancelled
                {
                    OwnerId = target.OwnerId,
                    DefId = target.Kind == StackItemKind.BlackMarketPurchase
                        ? State.BlackMarketInstances[target.BmInstanceId!.Value].DefId
                        : target.LemonDefId,
                });
            }
            // The tantrum card itself stays in its owner's pile (gained at play time).
        }

        private void ResolveTag(StackItem tag, List<GameEvent> events)
        {
            var target = FindStackItem(tag.RespondingToItemId);
            if (target != null && !target.Cancelled)
            {
                if (State.Players.Count == 2)
                {
                    // 2-player: "discard it instead" (card text).
                    target.Cancelled = true;
                    events.Add(new PlayCancelled { OwnerId = target.OwnerId, DefId = target.LemonDefId });
                }
                else
                {
                    target.AttackTargetId = tag.RedirectTargetId;
                    events.Add(new AttackRedirected
                    {
                        ByPlayerId = tag.OwnerId,
                        NewTargetId = tag.RedirectTargetId!.Value,
                    });
                }
            }
        }

        private void ResolveRubberGlue(StackItem rubber, List<GameEvent> events)
        {
            var target = FindStackItem(rubber.RespondingToItemId);
            if (target != null && !target.Cancelled)
            {
                // "Place the attack on the attacker instead, as if you played it."
                int oldAttacker = target.OwnerId;
                target.OwnerId = rubber.OwnerId;
                target.AttackTargetId = oldAttacker;
                events.Add(new AttackReflected { ByPlayerId = rubber.OwnerId, NewTargetId = oldAttacker });
            }
        }

        private void ResolveOutOfStock(StackItem card, List<GameEvent> events)
        {
            if (State.PendingRoll != null)
            {
                State.PendingRoll.Value = _rng.Roll(Db.Config.SaleDieSides);
                events.Add(new DieRerolled { ByPlayerId = card.OwnerId, NewValue = State.PendingRoll.Value });
            }
        }

        private void ResolveProfitShare(StackItem card, List<GameEvent> events)
        {
            if (State.TheftQueue.Count == 0)
            {
                return;
            }
            var theft = State.TheftQueue[0];
            State.TheftQueue.RemoveAt(0);

            // "Split the money with the attacker (rounding your half up)."
            int victimShare = (theft.AmountStolen + 1) / 2;
            var attacker = Player(theft.AttackerId);
            var victim = Player(theft.VictimId);
            int repaid = System.Math.Min(victimShare, attacker.Money);
            attacker.Money -= repaid;
            victim.Money += repaid;
            events.Add(new MoneyStolen
            {
                FromPlayerId = attacker.PlayerId,
                ToPlayerId = victim.PlayerId,
                Amount = repaid,
                Reason = "profit share",
            });
        }

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
        private void PopAndDiscard(StackItem item, List<GameEvent> events)
        {
            State.ResponseStack.Remove(item);
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

        // -------------------------------------------------- tantrum piles

        private void GainTantrum(PlayerState player, int instanceId, List<GameEvent> events)
        {
            player.TantrumPile.Add(new TantrumRecord
            {
                InstanceId = instanceId,
                GainSeq = State.NextTantrumGainSeq++,
            });
            State.EpisodeHadTantrums = true;
        }

        /// <summary>Whiniest Baby goes to whoever has the most tantrums (latest gain breaks ties).</summary>
        private void ReassignWhiniestBaby(List<GameEvent> events)
        {
            int max = State.Players.Max(p => p.TantrumPile.Count);
            if (max == 0)
            {
                return;
            }
            var holder = State.Players
                .Where(p => p.TantrumPile.Count == max)
                .OrderByDescending(p => p.TantrumPile.Max(t => t.GainSeq))
                .First();
            if (State.WhiniestBabyHolder != holder.PlayerId)
            {
                events.Add(new WhiniestBabyMoved
                {
                    FromPlayerId = State.WhiniestBabyHolder,
                    ToPlayerId = holder.PlayerId,
                });
                State.WhiniestBabyHolder = holder.PlayerId;
            }
        }

        // ----------------------------------------------------- draw queue

        private void QueueDraws(int playerId, int count)
        {
            State.PendingDrawPlayerId = playerId;
            State.PendingDrawCount += count;
        }

        private void ProcessPendingDraws(List<GameEvent> events)
        {
            var player = Player(State.PendingDrawPlayerId);
            while (State.PendingDrawCount > 0)
            {
                State.PendingDrawCount--;
                if (!DrawOneCard(player, events))
                {
                    return; // Timeout interrupted; remaining draws stay queued.
                }
            }
        }

        /// <summary>Draw one card for the player. Returns false when a Timeout paused the game.</summary>
        private bool DrawOneCard(PlayerState player, List<GameEvent> events)
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
            // The drawer draws a replacement once everything settles.
            State.PendingDrawCount++;

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
                int max = State.Players.Max(p => p.TantrumPile.Count);
                int? newHolder = null;
                if (max > 0)
                {
                    newHolder = State.Players
                        .Where(p => p.TantrumPile.Count == max)
                        .OrderByDescending(p => p.TantrumPile.Max(t => t.GainSeq))
                        .First().PlayerId;
                }
                if (newHolder != State.WhiniestBabyHolder)
                {
                    events.Add(new WhiniestBabyMoved { FromPlayerId = babyId, ToPlayerId = newHolder });
                    State.WhiniestBabyHolder = newHolder;
                }
            }
        }

        // -------------------------------------------------- decision actions

        private PendingDecision RequireDecision(int playerId, DecisionKind kind)
        {
            var decision = State.PendingDecisions.FirstOrDefault(
                d => d.PlayerId == playerId && d.Kind == kind);
            if (decision == null)
            {
                throw new InvalidActionException($"P{playerId} has no pending {kind} decision.");
            }
            return decision;
        }

        private void ApplySubmitDiscard(SubmitDiscard action, List<GameEvent> events)
        {
            var decision = State.PendingDecisions.FirstOrDefault(d =>
                d.PlayerId == action.PlayerId &&
                (d.Kind == DecisionKind.DiscardToHandLimit || d.Kind == DecisionKind.WhiniestBabyDiscard));
            if (decision == null)
            {
                throw new InvalidActionException($"P{action.PlayerId} has no pending discard.");
            }

            var player = Player(action.PlayerId);
            if (action.InstanceIds.Count != decision.RequiredCount ||
                action.InstanceIds.Distinct().Count() != action.InstanceIds.Count ||
                action.InstanceIds.Any(id => !player.Hand.Contains(id)))
            {
                throw new InvalidActionException(
                    $"Must discard exactly {decision.RequiredCount} distinct card(s) from your hand.");
            }

            foreach (int id in action.InstanceIds)
            {
                player.Hand.Remove(id);
                State.LemonDiscard.Add(id);
            }
            events.Add(new CardsDiscarded { PlayerId = player.PlayerId, InstanceIds = action.InstanceIds.ToList() });

            State.PendingDecisions.Remove(decision);
            Pump(events);
        }

        private void ApplySubmitTimeoutPayment(SubmitTimeoutPayment action, List<GameEvent> events)
        {
            var decision = RequireDecision(action.PlayerId, DecisionKind.TimeoutFine);
            var player = Player(action.PlayerId);

            // Sell listed assets at full base price: stands under their stack, cards to the discard.
            foreach (int standId in action.SellStandInstanceIds.Distinct())
            {
                var stand = player.Stands.FirstOrDefault(s => s.InstanceId == standId)
                    ?? throw new InvalidActionException($"No owned stand {standId}.");
                var type = Db.StandType(stand.StandTypeId);
                // Equipped cards on a sold stand are discarded with it.
                foreach (int equipped in stand.Equipped)
                {
                    State.BlackMarketDiscard.Add(equipped);
                }
                player.Stands.Remove(stand);
                State.StandSupply[stand.StandTypeId].Add(stand.Shape); // bottom of the stack
                player.Money += type.BaseCost;
                events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = type.BaseCost, Reason = "sold stand" });
            }
            foreach (int bmId in action.SellBmInstanceIds.Distinct())
            {
                bool fromStand = player.Stands.Any(s => s.Equipped.Remove(bmId));
                bool fromTurf = !fromStand && player.Turf.Equipped.Remove(bmId);
                if (!fromStand && !fromTurf)
                {
                    throw new InvalidActionException($"No equipped Black Market card {bmId}.");
                }
                var def = Db.BlackMarket(State.BlackMarketInstances[bmId].DefId);
                State.BlackMarketDiscard.Add(bmId);
                player.Money += def.Cost;
                events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = def.Cost, Reason = "sold card" });
            }

            int owed = decision.RequiredMoney;
            bool bankrupt = player.Money < owed &&
                player.Stands.Count == 0 &&
                player.Turf.Equipped.Count == 0;
            if (player.Money < owed && !bankrupt)
            {
                throw new InvalidActionException(
                    $"Fine is ${owed}; sell enough assets to cover it (have ${player.Money}).");
            }

            int paid = System.Math.Min(owed, player.Money);
            player.Money -= paid;
            events.Add(new MoneyChanged { PlayerId = player.PlayerId, Amount = -paid, Reason = "timeout fine" });

            State.PendingDecisions.Remove(decision);
            Pump(events);
        }

        private void ApplySkipFreePlay(SkipFreePlay action, List<GameEvent> events)
        {
            var decision = RequireDecision(action.PlayerId, DecisionKind.FreePlayOffer);
            State.PendingDecisions.Remove(decision);
            Pump(events);
        }

        // ------------------------------------------------------ turn start

        /// <summary>Begin the active player's turn; steps advance through the pump.</summary>
        private void StartTurn(List<GameEvent> events)
        {
            State.Phase = TurnPhase.Start;
            State.ActionsRemaining = Db.Config.ActionsPerTurn;
            State.MarketRefreshUsedThisTurn = false;
            State.BraggingRightsBoughtThisTurn = false;
            State.TurnStartInProgress = true;
            State.TurnStartStep = 0;
            events.Add(new TurnStarted { PlayerId = State.ActivePlayer });
        }

        private void AdvanceTurnStart(List<GameEvent> events)
        {
            var active = Player(State.ActivePlayer);
            switch (State.TurnStartStep)
            {
                case 0: // Spoiled Rotten: free sale roll for yourself (Supporting Cards p80).
                    State.TurnStartStep = 1;
                    if (State.SpoiledRottenHolder == State.ActivePlayer)
                    {
                        OpenRollWindow(RollPurpose.SpoiledRotten, State.ActivePlayer, events);
                    }
                    break;
                case 1: // Draw: 1 normally, 2 for the Whiniest Baby (who discards 1 below).
                    State.TurnStartStep = 2;
                    QueueDraws(State.ActivePlayer,
                        State.WhiniestBabyHolder == State.ActivePlayer
                            ? Db.Config.TurnStartDraw + 1
                            : Db.Config.TurnStartDraw);
                    break;
                case 2: // Whiniest Baby: discard 1 of the 2 drawn.
                    State.TurnStartStep = 3;
                    if (State.WhiniestBabyHolder == State.ActivePlayer && active.Hand.Count > 0)
                    {
                        State.PendingDecisions.Add(new PendingDecision
                        {
                            PlayerId = State.ActivePlayer,
                            Kind = DecisionKind.WhiniestBabyDiscard,
                            RequiredCount = 1,
                        });
                        events.Add(new DecisionRequired
                        {
                            PlayerId = State.ActivePlayer,
                            Kind = DecisionKind.WhiniestBabyDiscard,
                        });
                    }
                    break;
                default:
                    State.TurnStartInProgress = false;
                    State.Phase = TurnPhase.Play;
                    break;
            }
        }

        // ------------------------------------------------------ sale rolls

        private void OpenRollWindow(RollPurpose purpose, int rollerId, List<GameEvent> events)
        {
            State.PendingRoll = new PendingRoll
            {
                Value = _rng.Roll(Db.Config.SaleDieSides),
                Purpose = purpose,
                RollerId = rollerId,
            };
            BumpRevision();
            events.Add(new SaleRolled { PlayerId = rollerId, Value = State.PendingRoll.Value });
        }

        private void FinalizePendingRoll(List<GameEvent> events)
        {
            var roll = State.PendingRoll!;
            State.PendingRoll = null;
            BumpRevision();

            bool allPlayers = roll.Purpose == RollPurpose.TurnSale;
            FinalizeSale(roll.Value, allPlayers, roll.RollerId, events);
            State.PostRollContinuation = roll.Purpose;
        }

        /// <summary>
        /// Apply a final die value: every in-scope player's matching Stands pay out and matching
        /// power pours fire (all players sell on every roll — the roller just throws the die).
        /// </summary>
        private void FinalizeSale(int roll, bool allPlayers, int rollerId, List<GameEvent> events)
        {
            var earned = new Dictionary<int, int>();
            int n = State.Players.Count;
            for (int offset = 0; offset < n; offset++)
            {
                var player = State.Players[(rollerId + offset) % n];
                if (!allPlayers && player.PlayerId != rollerId)
                {
                    continue;
                }

                int gained = 0;
                foreach (var stand in player.Stands)
                {
                    var standType = Db.StandType(stand.StandTypeId);
                    if (!standType.SaleNumbers.Contains(roll))
                    {
                        continue;
                    }
                    // TODO(effects): equipped Black Market "On Sale" triggers and earning bonuses.
                    player.Money += standType.BaseEarnings;
                    gained += standType.BaseEarnings;
                    events.Add(new StandSold
                    {
                        PlayerId = player.PlayerId,
                        StandInstanceId = stand.InstanceId,
                        Earnings = standType.BaseEarnings,
                    });
                }

                if (player.Turf.PowerPourNumber == roll)
                {
                    // TODO(effects): equipped "Power Pour" Black Market triggers.
                    events.Add(new PowerPourTriggered { PlayerId = player.PlayerId });
                    player.Money += Db.Turf.BasePowerPourMoney;
                    gained += Db.Turf.BasePowerPourMoney;
                    events.Add(new MoneyChanged
                    {
                        PlayerId = player.PlayerId,
                        Amount = Db.Turf.BasePowerPourMoney,
                        Reason = "power pour",
                    });
                }

                earned[player.PlayerId] = gained;
            }

            // Steal the Cashbox traps: skim up to $10 of what the trapped player just earned.
            foreach (var player in State.Players)
            {
                if (!(player.Turf.TrapInstanceId is int trapId) ||
                    !(player.Turf.TrapOwnerId is int thief) ||
                    !earned.TryGetValue(player.PlayerId, out int gain))
                {
                    continue;
                }
                int stolen = System.Math.Min(gain, 10);
                if (stolen > 0)
                {
                    player.Money -= stolen;
                    Player(thief).Money += stolen;
                    events.Add(new MoneyStolen
                    {
                        FromPlayerId = player.PlayerId,
                        ToPlayerId = thief,
                        Amount = stolen,
                        Reason = "steal the cashbox",
                    });
                    OpenTheftWindow(player.PlayerId, thief, stolen);
                }
                // "Discard after rolling" — the trap goes whether or not it caught anything.
                player.Turf.TrapInstanceId = null;
                player.Turf.TrapOwnerId = null;
                State.LemonDiscard.Add(trapId);
            }
        }

        private void OpenTheftWindow(int victimId, int attackerId, int amount)
        {
            State.TheftQueue.Add(new PendingTheftResponse
            {
                VictimId = victimId,
                AttackerId = attackerId,
                AmountStolen = amount,
            });
            BumpRevision();
        }

        /// <summary>After the end-of-turn sale settles: check the VP trigger and pass the turn.</summary>
        private void FinishEndTurn(List<GameEvent> events)
        {
            var active = Player(State.ActivePlayer);
            if (State.Stage == GameStage.Playing &&
                active.InGameVictoryPoints >= Db.Config.VictoryPointsToTriggerEnd)
            {
                State.Stage = GameStage.FinalRound;
                State.EndTriggeredBy = active.PlayerId;
                events.Add(new StageChanged { Stage = State.Stage });
            }

            int nextPlayer = (State.ActivePlayer + 1) % State.Players.Count;
            if (State.Stage == GameStage.FinalRound && nextPlayer == State.FirstPlayer)
            {
                FinishGame(events);
                return;
            }

            State.ActivePlayer = nextPlayer;
            StartTurn(events);
        }
    }
}
