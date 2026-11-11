using System.Collections.Generic;
using System.Linq;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Legal responses for a player the open window is waiting on (see
    /// Game.LegalMoves.cs). Passing is always first; what else is offered depends on
    /// what the window is about: the top of the response stack, a theft, or a roll.
    /// </summary>
    public sealed partial class Game
    {
        private void AddWindowMoves(List<GameAction> moves, PlayerState player)
        {
            moves.Add(new PassWindow { PlayerId = player.PlayerId });

            if (State.ResponseStack.Count > 0)
            {
                AddStackResponses(moves, player, TopStackItem());
            }
            else if (State.TheftQueue.Count > 0)
            {
                if (State.TheftQueue[0].VictimId == player.PlayerId)
                {
                    AddCardResponse(moves, player, ProfitShareId);
                }
            }
            else if (State.PendingRoll != null)
            {
                AddRollResponses(moves, player);
            }
        }

        private void AddStackResponses(List<GameAction> moves, PlayerState player, StackItem top)
        {
            if (top.OwnerId == player.PlayerId)
            {
                return;
            }
            if (IsTantrummable(top))
            {
                AddCardResponse(moves, player, TantrumId);
            }
            if (!IsAttackItem(top))
            {
                return;
            }
            AddTagResponses(moves, player, top);
            AddCardResponse(moves, player, RubberGlueId);
            bool isVictim = top.AttackTargetId == player.PlayerId;
            if (isVictim)
            {
                AddDecoyResponse(moves, player);
            }
        }

        /// <summary>
        /// Tag redirects the attack to anyone but its owner and current target. With two
        /// players there is no one to redirect to: Tag discards the attack and names no target.
        /// </summary>
        private void AddTagResponses(List<GameAction> moves, PlayerState player, StackItem attack)
        {
            if (!(FirstInHand(player, TagId) is int tagId))
            {
                return;
            }
            if (State.Players.Count == 2)
            {
                moves.Add(new RespondToWindow { PlayerId = player.PlayerId, CardInstanceId = tagId });
                return;
            }
            foreach (var target in State.Players)
            {
                if (target.PlayerId != attack.OwnerId && target.PlayerId != attack.AttackTargetId)
                {
                    moves.Add(new RespondToWindow
                    {
                        PlayerId = player.PlayerId,
                        CardInstanceId = tagId,
                        RedirectTargetId = target.PlayerId,
                    });
                }
            }
        }

        /// <summary>Inflatable Decoy defends only its owner: it is offered to the attack's target alone.</summary>
        private void AddDecoyResponse(List<GameAction> moves, PlayerState player)
        {
            foreach (int decoyId in player.Turf.Equipped
                .Where(id => EquippedDef(id).Id == "inflatable-decoy").Take(1))
            {
                moves.Add(new RespondToWindow { PlayerId = player.PlayerId, EquippedInstanceId = decoyId });
            }
        }

        private void AddRollResponses(List<GameAction> moves, PlayerState player)
        {
            AddCardResponse(moves, player, OutOfStockId);
            if (player.PlayerId != State.ActivePlayer)
            {
                return;
            }
            foreach (int equippedId in player.Turf.Equipped)
            {
                string abilityId = EquippedDef(equippedId).Id;
                if (RollAbilityIds.Contains(abilityId) &&
                    !State.UsedTurnAbilities.Contains(equippedId) &&
                    RollAbilityUsable(abilityId))
                {
                    moves.Add(new UseTurnAbility { PlayerId = player.PlayerId, EquippedInstanceId = equippedId });
                }
            }
        }

        private void AddCardResponse(List<GameAction> moves, PlayerState player, string defId)
        {
            if (FirstInHand(player, defId) is int cardId)
            {
                moves.Add(new RespondToWindow { PlayerId = player.PlayerId, CardInstanceId = cardId });
            }
        }

        /// <summary>Copies of a card are interchangeable, so a response offers only the first held.</summary>
        private int? FirstInHand(PlayerState player, string defId)
        {
            foreach (int cardId in player.Hand)
            {
                if (State.LemonInstances[cardId].DefId == defId)
                {
                    return cardId;
                }
            }
            return null;
        }
    }
}
