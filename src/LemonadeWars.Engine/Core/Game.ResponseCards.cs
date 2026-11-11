using System.Collections.Generic;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// The five instant response cards (Tantrum, Tag You're It!, I'm Rubber You're Glue, Out of
    /// Stock, Profit Share), each a RespondWith* (validate and push) and a Resolve* (effect when
    /// the stack unwinds) pair.
    /// </summary>
    public sealed partial class Game
    {
        /// <summary>Response cards never cost an action.</summary>
        private void PushResponseCard(PlayerState player, int cardInstanceId, string defId, List<GameEvent> events,
            int? respondingToItemId = null, int? redirectTargetId = null)
        {
            PushStackItem(new StackItem
            {
                Kind = StackItemKind.LemonPlay,
                OwnerId = player.PlayerId,
                LemonInstanceId = cardInstanceId,
                LemonDefId = defId,
                RespondingToItemId = respondingToItemId,
                RedirectTargetId = redirectTargetId,
                FreePlay = true,
            }, events);
        }

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
            ChargeSwearJars(player, Player(top.OwnerId), events);

            PushResponseCard(player, action.CardInstanceId, LemonCardIds.Tantrum, events, respondingToItemId: top.ItemId);

            bool answersATantrum = top.Kind == StackItemKind.LemonPlay && top.LemonDefId == LemonCardIds.Tantrum;
            if (answersATantrum)
            {
                CounterTantrum(counter: TopStackItem(), countered: top, events);
            }
        }

        /// <summary>
        /// A Tantrum thrown at a Tantrum lands at once: both are spent, and the card the first was
        /// aimed at is OPEN AGAIN to the whole table. Reactions end only when everyone passes.
        /// </summary>
        private void CounterTantrum(StackItem counter, StackItem countered, List<GameEvent> events)
        {
            var reopened = FindStackItem(countered.RespondingToItemId);
            ResolveTantrum(counter, events);
            PopResolved(counter, events);
            PopCancelled(countered, events);
            if (reopened != null && !reopened.Cancelled)
            {
                reopened.WindowOffered = false;
            }
        }

        /// <summary>The Tantrum card itself stays in its owner's pile (gained at play time).</summary>
        private void ResolveTantrum(StackItem tantrum, List<GameEvent> events)
        {
            var target = FindStackItem(tantrum.RespondingToItemId);
            if (target != null && !target.Cancelled)
            {
                target.Cancelled = true;
                events.Add(new PlayCancelled
                {
                    OwnerId = target.OwnerId,
                    DefId = StackItemDefId(target),
                    ByPlayerId = tantrum.OwnerId,
                    WithDefId = LemonCardIds.Tantrum,
                });
            }
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
            // Any player may tag (designer ruling), but not onto the attacker (card text): that is
            // Rubber-Glue's job. In a 2-player game the attack is discarded instead.
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
            PushResponseCard(player, action.CardInstanceId, LemonCardIds.TagYoureIt, events,
                respondingToItemId: top.ItemId, redirectTargetId: action.RedirectTargetId);
        }

        private void ResolveTag(StackItem tag, List<GameEvent> events)
        {
            var target = FindStackItem(tag.RespondingToItemId);
            if (target == null || target.Cancelled)
            {
                return;
            }
            if (State.Players.Count == 2)
            {
                // 2-player: "discard it instead" (card text).
                target.Cancelled = true;
                events.Add(new PlayCancelled
                {
                    OwnerId = target.OwnerId,
                    DefId = target.LemonDefId,
                    ByPlayerId = tag.OwnerId,
                    WithDefId = LemonCardIds.TagYoureIt,
                });
                return;
            }
            target.AttackTargetId = tag.RedirectTargetId;
            target.WindowOffered = false; // newly aimed: its new victim gets to answer
            ClearStaleAttackSpecifics(target);
            events.Add(new AttackRedirected
            {
                ByPlayerId = tag.OwnerId,
                NewTargetId = tag.RedirectTargetId!.Value,
            });
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
            PushResponseCard(player, action.CardInstanceId, LemonCardIds.ImRubberYoureGlue, events, respondingToItemId: top.ItemId);
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
                target.WindowOffered = false; // newly aimed: its new victim gets to answer
                ClearStaleAttackSpecifics(target);
                events.Add(new AttackReflected { ByPlayerId = rubber.OwnerId, NewTargetId = oldAttacker });
            }
        }

        /// <summary>
        /// A re-aimed attack's picks named the OLD victim's cards. Cleared at once, so no view shows
        /// the old loot during the window; resolution re-asks through RequestRetargetOrFizzle.
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
            PushResponseCard(player, action.CardInstanceId, LemonCardIds.OutOfStock, events);
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
            PushResponseCard(player, action.CardInstanceId, LemonCardIds.ProfitShare, events);
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
