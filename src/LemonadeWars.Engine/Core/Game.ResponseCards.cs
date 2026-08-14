using System.Collections.Generic;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// The five instant response cards — Tantrum, Tag You're It!, I'm Rubber You're Glue,
    /// Out of Stock, Profit Share — each as a RespondWith* (validate and push) and a
    /// Resolve* (effect when the stack unwinds) pair. The window/stack machinery that
    /// routes to these lives in Game.Responses.cs.
    /// </summary>
    public sealed partial class Game
    {
        // ----------------------------------------------------------- tantrum

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
            player.LastTantrumPlaySeq = player.TantrumPile[player.TantrumPile.Count - 1].GainSeq;
            // Swear Jar: throwing a tantrum against this player's card costs $2 per jar.
            ChargeSwearJars(player, Player(top.OwnerId), events);

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
                    ByPlayerId = tantrum.OwnerId,
                    WithDefId = TantrumId,
                });
            }
            // The tantrum card itself stays in its owner's pile (gained at play time).
        }

        // ----------------------------------------------------- tag, you're it!

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

        private void ResolveTag(StackItem tag, List<GameEvent> events)
        {
            var target = FindStackItem(tag.RespondingToItemId);
            if (target != null && !target.Cancelled)
            {
                if (State.Players.Count == 2)
                {
                    // 2-player: "discard it instead" (card text).
                    target.Cancelled = true;
                    events.Add(new PlayCancelled
                    {
                        OwnerId = target.OwnerId,
                        DefId = target.LemonDefId,
                        ByPlayerId = tag.OwnerId,
                        WithDefId = TagId,
                    });
                }
                else
                {
                    target.AttackTargetId = tag.RedirectTargetId;
                    // The card-specific pick (Finders Keepers / That's Not Fair) named
                    // a card of the OLD victim — clear it now, not at resolution, or
                    // every view keeps showing the old victim's card as the loot for
                    // the whole response window. The attacker re-picks on resolve.
                    ClearStaleAttackSpecifics(target);
                    events.Add(new AttackRedirected
                    {
                        ByPlayerId = tag.OwnerId,
                        NewTargetId = tag.RedirectTargetId!.Value,
                    });
                }
            }
        }

        // -------------------------------------------- i'm rubber, you're glue

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

        private void ResolveRubberGlue(StackItem rubber, List<GameEvent> events)
        {
            var target = FindStackItem(rubber.RespondingToItemId);
            if (target != null && !target.Cancelled)
            {
                // "Place the attack on the attacker instead, as if you played it."
                int oldAttacker = target.OwnerId;
                target.OwnerId = rubber.OwnerId;
                target.AttackTargetId = oldAttacker;
                ClearStaleAttackSpecifics(target); // same staleness as a Tag redirect
                events.Add(new AttackReflected { ByPlayerId = rubber.OwnerId, NewTargetId = oldAttacker });
            }
        }

        /// <summary>
        /// A redirect/reflection changed the attack's victim, so any card-specific
        /// choices (the equipped card to steal/discard, and where a Finders Keepers
        /// steal would go) refer to the WRONG player now. Clearing them keeps the
        /// stack honest for the response-window views; resolution then routes through
        /// RequestRetargetOrFizzle exactly as it would for an invalid pick.
        /// </summary>
        private static void ClearStaleAttackSpecifics(StackItem attack)
        {
            attack.TargetEquippedInstanceId = null;
            attack.EquipStandInstanceId = null;
            attack.EquipReplaceInstanceId = null;
        }

        // ------------------------------------------------------ out of stock

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

        private void ResolveOutOfStock(StackItem card, List<GameEvent> events)
        {
            if (State.PendingRoll != null)
            {
                State.PendingRoll.Value = _rng.Roll(Db.Config.SaleDieSides);
                events.Add(new DieRerolled { ByPlayerId = card.OwnerId, NewValue = State.PendingRoll.Value });
            }
        }

        // ------------------------------------------------------ profit share

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
    }
}
