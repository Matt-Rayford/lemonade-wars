using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Passing is always offered first; the rest depends on what the window is about: the top of
    /// the response stack, a theft, or a roll.
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
                    AddCardResponse(moves, player, LemonCardIds.ProfitShare);
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
                AddCardResponse(moves, player, LemonCardIds.Tantrum);
            }
            if (!IsAttackItem(top))
            {
                return;
            }
            AddTagResponses(moves, player, top);
            AddCardResponse(moves, player, LemonCardIds.ImRubberYoureGlue);
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
            if (!(FirstInHand(player, LemonCardIds.TagYoureIt) is int tagId))
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
                .Where(id => EquippedDef(id).Id == BlackMarketCardIds.InflatableDecoy).Take(1))
            {
                moves.Add(new RespondToWindow { PlayerId = player.PlayerId, EquippedInstanceId = decoyId });
            }
        }

        private void AddRollResponses(List<GameAction> moves, PlayerState player)
        {
            AddCardResponse(moves, player, LemonCardIds.OutOfStock);
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
