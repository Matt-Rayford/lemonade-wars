using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    public sealed partial class Game
    {
        /// <summary>
        /// Inflatable Decoy is immediate and untantrummable (card text): the attack and the
        /// decoy both die on the spot, and nothing goes on the stack.
        /// </summary>
        private void RespondWithDecoy(PlayerState player, int decoyId, List<GameEvent> events)
        {
            if (!player.Turf.Equipped.Contains(decoyId) ||
                EquippedDef(decoyId).Id != BlackMarketCardIds.InflatableDecoy)
            {
                throw new InvalidActionException("Only an equipped Inflatable Decoy can respond this way.");
            }
            var attack = RequireStackTop("Inflatable Decoy");
            if (!IsAttackItem(attack))
            {
                throw new InvalidActionException("Inflatable Decoy discards an attack that was just played.");
            }
            if (attack.OwnerId == player.PlayerId)
            {
                throw new InvalidActionException("You cannot react to your own attack.");
            }
            if (attack.AttackTargetId != player.PlayerId)
            {
                throw new InvalidActionException(
                    "Inflatable Decoy only protects its owner — you must be the attack's target.");
            }

            attack.Cancelled = true;
            player.Turf.Equipped.Remove(decoyId);
            State.BlackMarketDiscard.Add(decoyId);
            events.Add(new PlayCancelled
            {
                OwnerId = attack.OwnerId,
                DefId = attack.LemonDefId,
                ByPlayerId = player.PlayerId,
                WithDefId = BlackMarketCardIds.InflatableDecoy,
            });
            events.Add(new CardsDiscarded { PlayerId = player.PlayerId, InstanceIds = new List<int> { decoyId } });
            BumpRevision();
        }

        /// <summary>Swear Jar: each tantrum played against this player's card costs the thrower $2 per jar.</summary>
        private void ChargeSwearJars(PlayerState thrower, PlayerState target, List<GameEvent> events)
        {
            int jars = CountOnTurf(target, BlackMarketCardIds.SwearJar);
            if (jars == 0)
            {
                return;
            }
            int charge = Math.Min(2 * jars, thrower.Money);
            if (charge > 0)
            {
                thrower.Money -= charge;
                target.Money += charge;
                events.Add(new MoneyStolen
                {
                    FromPlayerId = thrower.PlayerId,
                    ToPlayerId = target.PlayerId,
                    Amount = charge,
                    Reason = "swear jar",
                });
            }
        }

        /// <summary>Bouncer: after an attack resolves against its owner, they may play an attack for free.</summary>
        private void CheckBouncer(StackItem resolvedAttack, List<GameEvent> events)
        {
            if (!(resolvedAttack.AttackTargetId is int victimId))
            {
                return;
            }
            var victim = Player(victimId);
            var bouncers = victim.Turf.Equipped
                .Where(id => EquippedDef(id).Id == BlackMarketCardIds.Bouncer)
                .ToList();
            if (bouncers.Count == 0)
            {
                return;
            }
            bool hasAttack = victim.Hand.Any(id =>
                Db.Lemon(State.LemonInstances[id].DefId).Type == LemonCardType.Attack);
            if (!hasAttack)
            {
                return;
            }
            foreach (int bouncerId in bouncers)
            {
                AddDecision(new PendingDecision
                {
                    PlayerId = victimId,
                    Kind = DecisionKind.BouncerAttack,
                    SourceInstanceId = bouncerId,
                    // The attack has already left the stack, so who hit you with what
                    // rides on the decision for the UI.
                    ContextDefId = resolvedAttack.LemonDefId,
                    ContextPlayerId = resolvedAttack.OwnerId,
                }, events);
            }
        }
    }
}
